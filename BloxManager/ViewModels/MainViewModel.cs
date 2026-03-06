using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BloxManager.Models;
using BloxManager.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BloxManager.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly ILogger<MainViewModel> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IAccountService _accountService;
        private readonly ISettingsService _settingsService;
        private readonly IBrowserService _browserService;
        private readonly IGameService _gameService;

        private CancellationTokenSource? _presenceRefreshCts;
        private CancellationTokenSource? _uptimeCts;
        private DateTime _sessionStartTime = DateTime.MinValue;
        private int _prevAccountsInGame = 0;

        // Keeps live AccountGroup objects so IsExpanded state is preserved across refreshes
        private readonly Dictionary<string, AccountGroup> _groupCache = new();

        // ── Observable properties ────────────────────────────────────────────

        [ObservableProperty]
        private ObservableCollection<Account> _accounts = new();

        [ObservableProperty]
        private ObservableCollection<Account> _selectedAccounts = new();

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private string _appVersion = "1.0.1";

        [ObservableProperty]
        private string _updateChannel = "Made By Mr Duck";

        public string VersionChannelText => $"{AppVersion} {UpdateChannel}";

        [ObservableProperty]
        private bool _isSettingsOpen = false;

        [ObservableProperty]
        private string _backgroundImagePath = "";

        [ObservableProperty]
        private string _backgroundImageStretch = "UniformToFill";

        [ObservableProperty]
        private string _backgroundImageAlignment = "Center";

        [ObservableProperty]
        private double _backgroundImageOpacity = 1.0;

        [ObservableProperty]
        private string _backgroundTargetDimensions = "1920x1080";

        [ObservableProperty]
        private ObservableCollection<string> _groups = new();

        [ObservableProperty]
        private ObservableCollection<AccountGroup> _accountGroups = new();

        [ObservableProperty]
        private Account? _selectedAccount = null;

        [ObservableProperty]
        private string _selectedGroup = "All";

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private ObservableCollection<object> _displayItems = new();

        public ObservableCollection<string> AvailableGroupsForMove { get; } = new();

        private readonly SettingsViewModel _settingsViewModel;
        public SettingsViewModel SettingsViewModel => _settingsViewModel;

        [ObservableProperty]
        private string _placeId = "";

        [ObservableProperty]
        private string _jobId = "";

        [ObservableProperty]
        private string _launchData = "";

        [ObservableProperty]
        private int _accountsInGame = 0;

        [ObservableProperty]
        private string _inGameUptimeText = "00:00:00";

        // ── Constructor ──────────────────────────────────────────────────────

        public MainViewModel(
            ILogger<MainViewModel> logger,
            ILoggerFactory loggerFactory,
            IAccountService accountService,
            ISettingsService settingsService,
            IBrowserService browserService,
            IGameService gameService)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _accountService = accountService;
            _settingsService = settingsService;
            _browserService = browserService;
            _gameService = gameService;

            // Seed permanent groups
            Groups.Add("All");
            EnsureGroupExists("Default");

            // SettingsViewModel
            var settingsLogger = _loggerFactory.CreateLogger<SettingsViewModel>();
            _settingsViewModel = new SettingsViewModel(settingsLogger, _settingsService, _gameService);
            _settingsViewModel.PropertyChanged += SettingsViewModel_PropertyChanged;

            // Async init — all UI mutations are marshalled to the UI thread inside
            _ = InitializeAsync();
        }

        // ── Initialization ────────────────────────────────────────────────────

        private async Task InitializeAsync()
        {
            _logger.LogInformation("InitializeAsync started");
            try
            {
                await RunOnUiAsync(() => IsLoading = true);
                await LoadBackgroundSettingsAsync();

                var placeId = await _settingsService.GetPlaceIdAsync();
                var jobId = await _settingsService.GetJobIdAsync();
                var launchData = await _settingsService.GetLaunchDataAsync();
                await RunOnUiAsync(() =>
                {
                    PlaceId = placeId;
                    JobId = jobId;
                    LaunchData = launchData;
                });

                await LoadAccountsAsync();

                StartPresenceRefreshLoop();
                StartUptimeTrackingLoop();

                await RunOnUiAsync(() =>
                {
                    UpdateDisplayItems();
                    StatusMessage = "Ready";
                });
                _logger.LogInformation("InitializeAsync completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Startup failed");
                await RunOnUiAsync(() => StatusMessage = "Startup error — check logs");
            }
            finally
            {
                await RunOnUiAsync(() => IsLoading = false);
            }
        }

        private async Task LoadBackgroundSettingsAsync()
        {
            _logger.LogInformation("LoadBackgroundSettingsAsync started");
            try
            {
                var path      = await _settingsService.GetSettingAsync<string>("BackgroundImagePath")       ?? "";
                var stretch   = await _settingsService.GetSettingAsync<string>("BackgroundImageStretch")    ?? "UniformToFill";
                var alignment = await _settingsService.GetSettingAsync<string>("BackgroundImageAlignment")  ?? "Center";
                var opacity   = await _settingsService.GetSettingAsync<double>("BackgroundImageOpacity", 1.0);
                var dims      = await _settingsService.GetSettingAsync<string>("BackgroundTargetDimensions") ?? "1920x1080";

                _logger.LogInformation("Setting background settings on UI thread...");
                await RunOnUiAsync(() =>
                {
                    BackgroundImagePath        = path;
                    BackgroundImageStretch     = stretch;
                    BackgroundImageAlignment   = alignment;
                    BackgroundImageOpacity     = opacity;
                    BackgroundTargetDimensions = dims;
                });
                _logger.LogInformation("LoadBackgroundSettingsAsync completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load background settings");
                await RunOnUiAsync(() =>
                {
                    BackgroundImagePath        = "";
                    BackgroundImageStretch     = "UniformToFill";
                    BackgroundImageAlignment   = "Center";
                    BackgroundImageOpacity     = 1.0;
                    BackgroundTargetDimensions = "1920x1080";
                });
            }
        }

        private async Task LoadAccountsAsync()
        {
            _logger.LogInformation("Loading accounts...");
            try
            {
                var accounts = await _accountService.GetAccountsAsync();
                if (accounts == null) 
                {
                    _logger.LogWarning("GetAccountsAsync returned null");
                    return;
                }

                _logger.LogInformation("Normalising {Count} accounts...", accounts.Count);
                // Normalise off the UI thread
                var normalised = accounts.Select(a =>
                {
                    if (a.Status == AccountStatus.Unknown) a.Status = AccountStatus.Offline;
                    if (string.IsNullOrEmpty(a.Group))     a.Group  = "Default";
                    return a;
                }).ToList();

                _logger.LogInformation("Adding accounts to collection on UI thread...");
                // All collection mutations on the UI thread
                await RunOnUiAsync(() =>
                {
                    Accounts.Clear();
                    foreach (var a in normalised)
                        Accounts.Add(a);

                    RebuildGroupsFromAccounts();
                    UpdateDisplayItems();
                    UpdateAccountsInGame(); // Initialize the count
                });
                _logger.LogInformation("LoadAccountsAsync completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load accounts");
            }
        }

        private void StartPresenceRefreshLoop()
        {
            try
            {
                _presenceRefreshCts?.Cancel();
                _presenceRefreshCts = new CancellationTokenSource();
                var token = _presenceRefreshCts.Token;

                _ = Task.Run(() => PresenceRefreshLoopAsync(token), token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start presence refresh loop");
            }
        }

        private async Task PresenceRefreshLoopAsync(CancellationToken token)
        {
            try { await Task.Delay(500, token); } catch { return; }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var roblox = App.GetService<IRobloxService>();

                    var accounts = Accounts.ToList();
                    _logger.LogInformation("Processing {Count} accounts for presence refresh", accounts.Count);

                    var ids = accounts.Where(a => a.UserId > 0).Select(a => a.UserId).Distinct().ToArray();
                    var presenceDict = await roblox.GetUsersPresenceAsync(ids);

                    foreach (var account in accounts)
                    {
                        if (token.IsCancellationRequested) break;

                        _logger.LogDebug("Processing account: {Username}, UserId: {UserId}, IsValid: {IsValid}", 
                            account.Username, account.UserId, account.IsValid);

                        if (account.UserId <= 0)
                        {
                            _logger.LogDebug("Setting account {Username} to Offline due to invalid UserId: {UserId}", 
                                account.Username, account.UserId);
                            
                            await RunOnUiAsync(() =>
                            {
                                var oldStatus = account.Status;
                                account.Status = AccountStatus.Offline;
                                _logger.LogDebug("Updated account {Username}: {OldStatus} -> {NewStatus}", 
                                    account.Username, oldStatus, AccountStatus.Offline);
                            });
                            continue;
                        }

                        presenceDict.TryGetValue(account.UserId, out var presence);

                        string? avatarUrl = null;
                        if (string.IsNullOrWhiteSpace(account.AvatarUrl))
                        {
                            try
                            {
                                avatarUrl = await roblox.GetUserAvatarUrlAsync(account.UserId);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "Avatar fetch failed for {Username}", account.Username);
                            }
                        }

                        var status = AccountStatus.Offline;
                        if (presence != null)
                        {
                            // Prefer numeric type for reliability:
                            // 0=Offline, 1=Online, 2=InGame, 3=Studio
                            status = presence.UserPresenceType switch
                            {
                                2 => AccountStatus.InGame,
                                1 => AccountStatus.Online,
                                3 => AccountStatus.Online, // Treat Studio as online
                                _ => AccountStatus.Offline
                            };
                        }

                        // Check if account is expired by testing CSRF token
                        if (status != AccountStatus.Expired && !string.IsNullOrWhiteSpace(account.SecurityToken))
                        {
                            try
                            {
                                var csrfTest = await roblox.TestCsrfTokenAsync(account.SecurityToken);
                                if (!csrfTest)
                                {
                                    status = AccountStatus.Expired;
                                    _logger.LogInformation("Account {Username} marked as Expired due to CSRF test failure", account.Username);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "CSRF test failed for {Username}", account.Username);
                                status = AccountStatus.Expired;
                            }
                        }

                        await RunOnUiAsync(() =>
                        {
                            var oldStatus = account.Status;
                            if (presence != null)
                                account.Presence = presence;

                            account.Status = status;

                            if (status == AccountStatus.InGame)
                            {
                                if (!account.InGameSince.HasValue)
                                    account.InGameSince = DateTime.Now;
                            }
                            else
                            {
                                account.InGameSince = null;
                            }

                            if (!string.IsNullOrWhiteSpace(avatarUrl))
                                account.AvatarUrl = avatarUrl;
                                
                            _logger.LogDebug("Updated account {Username}: {OldStatus} -> {NewStatus}", 
                                account.Username, oldStatus, status);
                        });

                    }

                    UpdateAccountsInGame();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Presence refresh loop tick failed");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
                catch
                {
                    return;
                }
            }
        }

        private void UpdateAccountsInGame()
        {
            try
            {
                var totalAccounts = Accounts.Count;
                var inGameCount = Accounts.Count(a => a.Status == AccountStatus.InGame);
                var onlineCount = Accounts.Count(a => a.Status == AccountStatus.Online);
                var offlineCount = Accounts.Count(a => a.Status == AccountStatus.Offline);
                var expiredCount = Accounts.Count(a => a.Status == AccountStatus.Expired);
                
                _logger.LogInformation("Account Status Summary - Total: {Total}, InGame: {InGame}, Online: {Online}, Offline: {Offline}, Expired: {Expired}", 
                    totalAccounts, inGameCount, onlineCount, offlineCount, expiredCount);
                
                if (AccountsInGame != inGameCount)
                {
                    _prevAccountsInGame = AccountsInGame;
                    AccountsInGame = inGameCount;
                    _logger.LogInformation("Updated AccountsInGame to {Count}", inGameCount);

                    // Start session uptime when first account joins in-game
                    if (_prevAccountsInGame <= 0 && inGameCount > 0)
                    {
                        var earliest = Accounts
                            .Where(a => a.Status == AccountStatus.InGame && a.InGameSince.HasValue)
                            .Select(a => a.InGameSince!.Value)
                            .DefaultIfEmpty(DateTime.Now)
                            .Min();
                        _sessionStartTime = earliest;
                        _logger.LogInformation("Session start time set to {Start}", _sessionStartTime);
                    }

                    // Reset uptime when no accounts are in-game
                    if (inGameCount == 0)
                    {
                        _sessionStartTime = DateTime.MinValue;
                        InGameUptimeText = "00:00:00";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update accounts in game count");
            }
        }

        private void StartUptimeTrackingLoop()
        {
            try
            {
                _uptimeCts?.Cancel();
                _uptimeCts = new CancellationTokenSource();
                var token = _uptimeCts.Token;

                _ = Task.Run(() => UptimeTrackingLoopAsync(token), token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start uptime tracking loop");
            }
        }

        private async Task UptimeTrackingLoopAsync(CancellationToken token)
        {
            // Short initial delay so the UI can settle
            try { await Task.Delay(1000, token); } catch { return; }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (AccountsInGame <= 0 || _sessionStartTime == DateTime.MinValue)
                    {
                        await RunOnUiAsync(() => InGameUptimeText = "00:00:00");
                    }
                    else
                    {
                        var uptime = DateTime.Now - _sessionStartTime;
                        var uptimeText = $"{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
                        await RunOnUiAsync(() => InGameUptimeText = uptimeText);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Uptime tracking loop tick failed");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                }
                catch
                {
                    return;
                }
            }
        }

        // ── Group helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns the cached AccountGroup for <paramref name="name"/>, creating it if needed.
        /// Preserves IsExpanded state across display refreshes.
        /// Must be called on the UI thread.
        /// </summary>
        private AccountGroup EnsureGroupExists(string name)
        {
            if (!_groupCache.TryGetValue(name, out var group))
            {
                group = new AccountGroup(name) { IsExpanded = true };
                _groupCache[name] = group;
            }

            if (!Groups.Contains(name))
                Groups.Add(name);

            return group;
        }

        private void RebuildGroupsFromAccounts()
        {
            foreach (var groupName in Accounts
                         .Where(a => !string.IsNullOrEmpty(a.Group))
                         .Select(a => a.Group)
                         .Distinct())
            {
                EnsureGroupExists(groupName);
            }

            SyncAvailableGroupsForMove();
        }

        private void SyncAvailableGroupsForMove()
        {
            var moveGroups = Groups.Where(g => g != "All").ToList();
            var toRemove   = AvailableGroupsForMove.Where(g => !moveGroups.Contains(g)).ToList();
            foreach (var g in toRemove) AvailableGroupsForMove.Remove(g);
            foreach (var g in moveGroups)
                if (!AvailableGroupsForMove.Contains(g))
                    AvailableGroupsForMove.Add(g);
        }

        // ── Display items ─────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds DisplayItems. Must be called on the UI thread.
        /// </summary>
        public void UpdateDisplayItems()
        {
            try
            {
                DisplayItems.Clear();

                var filtered = string.IsNullOrWhiteSpace(SearchText)
                    ? Accounts.AsEnumerable()
                    : Accounts.Where(a => a.Username.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

                var groupNamesToShow = SelectedGroup == "All"
                    ? Groups.Where(g => g != "All").ToList()
                    : new List<string> { SelectedGroup };

                foreach (var groupName in groupNamesToShow)
                {
                    var group = EnsureGroupExists(groupName);

                    // Sync child list on the cached group object
                    group.Accounts.Clear();
                    var accountsInGroup = filtered.Where(a => a.Group == groupName).ToList();
                    foreach (var account in accountsInGroup)
                        group.Accounts.Add(account);

                    DisplayItems.Add(group);

                    if (group.IsExpanded)
                        foreach (var account in accountsInGroup)
                            DisplayItems.Add(account);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateDisplayItems failed");
            }
        }

        partial void OnSelectedGroupChanged(string value) => UpdateDisplayItems();
        partial void OnSearchTextChanged(string value)    => UpdateDisplayItems();

        // ── Settings sync ─────────────────────────────────────────────────────

        private async void SettingsViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try
            {
                switch (e.PropertyName)
                {
                    case nameof(SettingsViewModel.BackgroundImagePath):
                    {
                        var value = _settingsViewModel.BackgroundImagePath;
                        await RunOnUiAsync(() => BackgroundImagePath = value);
                        _ = _settingsService.SetSettingAsync("BackgroundImagePath", value);
                        break;
                    }
                    case nameof(SettingsViewModel.BackgroundImageStretch):
                    {
                        var value = _settingsViewModel.BackgroundImageStretch;
                        await RunOnUiAsync(() => BackgroundImageStretch = value);
                        _ = _settingsService.SetSettingAsync("BackgroundImageStretch", value);
                        break;
                    }
                    case nameof(SettingsViewModel.BackgroundImageAlignment):
                    {
                        var value = _settingsViewModel.BackgroundImageAlignment;
                        await RunOnUiAsync(() => BackgroundImageAlignment = value);
                        _ = _settingsService.SetSettingAsync("BackgroundImageAlignment", value);
                        break;
                    }
                    case nameof(SettingsViewModel.BackgroundImageOpacity):
                    {
                        var value = _settingsViewModel.BackgroundImageOpacity;
                        await RunOnUiAsync(() => BackgroundImageOpacity = value);
                        _ = _settingsService.SetSettingAsync("BackgroundImageOpacity", value);
                        break;
                    }
                    case nameof(SettingsViewModel.PlaceId):
                    {
                        var value = _settingsViewModel.PlaceId;
                        await RunOnUiAsync(() => PlaceId = value);
                        break;
                    }
                    case nameof(SettingsViewModel.JobId):
                    {
                        var value = _settingsViewModel.JobId;
                        await RunOnUiAsync(() => JobId = value);
                        break;
                    }
                    case nameof(SettingsViewModel.LaunchData):
                    {
                        var value = _settingsViewModel.LaunchData;
                        await RunOnUiAsync(() => LaunchData = value);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync background setting {Property}", e.PropertyName);
            }
        }

        // ── Commands ──────────────────────────────────────────────────────────

        private static bool TryParsePlaceId(string? input, out long placeId)
        {
            placeId = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            var trimmed = input.Trim();
            if (long.TryParse(trimmed, out placeId) && placeId > 0) return true;

            // Accept common Roblox URL formats, e.g. https://www.roblox.com/games/123456789/My-Game
            var match = Regex.Match(trimmed, @"roblox\.com/(?:[a-z]{2}/)?games/(?<id>\d+)", RegexOptions.IgnoreCase);
            if (match.Success && long.TryParse(match.Groups["id"].Value, out placeId) && placeId > 0) return true;

            // Fallback: first long-looking number in the string
            match = Regex.Match(trimmed, @"(?<id>\d{5,})");
            if (match.Success && long.TryParse(match.Groups["id"].Value, out placeId) && placeId > 0) return true;

            placeId = 0;
            return false;
        }

        [RelayCommand]
        private async Task Refresh()
        {
            try
            {
                StatusMessage = "Refreshing...";
                IsLoading = true;
                await LoadAccountsAsync();
                StatusMessage = "Ready";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Refresh failed");
                StatusMessage = "Refresh failed";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void Exit()
        {
            try { Application.Current.Shutdown(); }
            catch { /* ignore */ }
        }

        [RelayCommand]
        private async Task LaunchSelectedAccounts()
        {
            if (!SelectedAccounts.Any()) { StatusMessage = "No accounts selected"; return; }

            try
            {
                IsLoading = true;
                StatusMessage = $"Launching {SelectedAccounts.Count} account(s)...";
                int successCount = 0;

                if (SelectedAccounts.Count > 1)
                {
                    try
                    {
                        await _settingsService.SetMultiRobloxEnabledAsync(true);
                        await RunOnUiAsync(() => _settingsViewModel.MultiRobloxEnabled = true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to enable MultiRoblox before multi-launch");
                    }
                }

                var hasPlaceId = TryParsePlaceId(PlaceId, out var placeId);
                var jobIdVal = string.IsNullOrWhiteSpace(JobId) ? null : JobId.Trim().Trim('"');
                var launchDataVal = string.IsNullOrWhiteSpace(LaunchData) ? null : LaunchData.Trim();

                foreach (var account in SelectedAccounts.ToList())
                {
                    try
                    {
                        StatusMessage = $"Launching {account.Username}...";
                        bool launched = false;

                        if (hasPlaceId)
                        {
                            _logger.LogInformation("JoinServer request: User={Username} PlaceId={PlaceId} JobId={JobId} LaunchData={LaunchData}",
                                account.Username,
                                placeId,
                                jobIdVal ?? "<null>",
                                launchDataVal ?? "<null>");

                            launched = await _gameService.JoinGameAsync(account, placeId, jobIdVal, launchDataVal);
                            if (!launched)
                            {
                                _logger.LogWarning("JoinServer failed for account {Username}. PlaceId={PlaceId} JobId={JobId} LaunchData={LaunchData}",
                                    account.Username, placeId, jobIdVal ?? "<null>", launchDataVal ?? "<null>");
                                StatusMessage = $"Failed to launch Roblox for {account.Username} (check PlaceId/JobId/LaunchData)";
                                continue;
                            }
                        }
                        else
                        {
                            // No PlaceId provided: browser login is the intended fallback
                            launched = await _browserService.LaunchBrowserAsync(account);
                        }

                        if (launched)
                        {
                            successCount++;
                            StatusMessage = $"Launched {account.Username}";
                            await RunOnUiAsync(() =>
                            {
                                if (account.Status != AccountStatus.InGame)
                                    account.Status = AccountStatus.Online;
                            });
                        }
                        else
                        {
                            StatusMessage = $"Failed to launch {account.Username}";
                        }

                        account.LastUsed = DateTime.Now;
                        await _accountService.UpdateAccountAsync(account);

                        await Task.Delay(250);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to launch account {Username}", account.Username);
                        StatusMessage = $"Error launching {account.Username}";
                    }
                }

                StatusMessage = $"Launched {successCount}/{SelectedAccounts.Count} account(s)";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LaunchSelectedAccounts failed");
                StatusMessage = "Error launching accounts";
            }
            finally { IsLoading = false; }
        }

        [RelayCommand]
        private void OpenSettings() => IsSettingsOpen = true;

        [RelayCommand]
        private void OpenAccounts() => IsSettingsOpen = false;

        [RelayCommand]
        private void Help()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/JakeBx99/Roblox-Account-Manager",
                    UseShellExecute = true
                });
            }
            catch { /* ignore */ }
        }

        [RelayCommand]
        private async Task AddAccount()
        {
            try
            {
                StatusMessage = "Opening Roblox login...";
                IsLoading = true;

                var loginInfo = await _browserService.AcquireRobloxLoginInfoAsync();
                if (loginInfo == null || string.IsNullOrWhiteSpace(loginInfo.SecurityToken))
                {
                    StatusMessage = "Login cancelled or no cookie detected.";
                    return;
                }

                StatusMessage = "Adding account...";
                var account = await _accountService.LoginWithCookieAsync(loginInfo.SecurityToken.Trim());
                if (account == null) { StatusMessage = "Failed to add account. Invalid cookie?"; return; }

                account.Password = loginInfo.Password;
                if (string.IsNullOrEmpty(account.Group)) account.Group = "Default";
                await _accountService.UpdateAccountAsync(account);

                Accounts.Add(account);
                EnsureGroupExists(account.Group);
                SyncAvailableGroupsForMove();
                UpdateDisplayItems();

                StatusMessage = $"Added {account.Username}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddAccount failed");
                StatusMessage = "Error adding account";
            }
            finally { IsLoading = false; }
        }

        [RelayCommand]
        private async Task AddAccountUserPass()
        {
            try
            {
                var prompt = new BloxManager.Views.PromptWindow("Add Account", "Enter one or more User:Pass lines (e.g. user:pass)");
                if (prompt.ShowDialog() != true) return;

                var lines = (prompt.InputText ?? string.Empty)
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => l.Contains(':'))
                    .ToList();

                if (lines.Count == 0) { StatusMessage = "No valid lines. Use user:pass per line."; return; }

                IsLoading = true;
                int success = 0, fail = 0, skipped = 0;

                var existingUsers = new HashSet<string>(
                    (await _accountService.GetAccountsAsync()).Select(a => a.Username),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var line in lines)
                {
                    var parts    = line.Split(':', 2);
                    var username = parts[0].Trim();
                    var password = parts[1].Trim();

                    if (existingUsers.Contains(username)) { skipped++; continue; }

                    StatusMessage = $"Logging in as {username}...";
                    var account = await _accountService.LoginAsync(username, password, keepBrowserOpenUntilSuccess: true);

                    if (account != null)
                    {
                        if (string.IsNullOrEmpty(account.Group)) account.Group = "Default";
                        existingUsers.Add(username);
                        await RunOnUiAsync(() => Accounts.Add(account));
                        success++;
                    }
                    else fail++;
                }

                await RunOnUiAsync(() =>
                {
                    RebuildGroupsFromAccounts();
                    UpdateDisplayItems();
                });

                StatusMessage = $"Added {success}, {fail} failed, {skipped} skipped";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddAccountUserPass failed");
                StatusMessage = "Error adding accounts";
            }
            finally { IsLoading = false; }
        }

        [RelayCommand]
        private async Task RemoveSelectedAccounts()
        {
            if (!SelectedAccounts.Any()) { StatusMessage = "No accounts selected"; return; }

            try
            {
                IsLoading = true;
                var snapshot = SelectedAccounts.ToList();
                StatusMessage = $"Removing {snapshot.Count} account(s)...";

                foreach (var account in snapshot)
                {
                    await _accountService.DeleteAccountAsync(account.Id);
                    Accounts.Remove(account);
                }

                UpdateDisplayItems();
                StatusMessage = $"Removed {snapshot.Count} account(s)";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RemoveSelectedAccounts failed");
                StatusMessage = "Error removing accounts";
            }
            finally { IsLoading = false; }
        }

        [RelayCommand]
        private void CopyUsername(Account? account)
        {
            if (account == null) return;
            try { Clipboard.SetText(account.Username); StatusMessage = $"Copied username: {account.Username}"; }
            catch { StatusMessage = "Failed to copy username"; }
        }

        [RelayCommand]
        private void CopyPassword(Account? account) => StatusMessage = "Copy password not implemented";

        [RelayCommand]
        private void CopyCombo(Account? account) => StatusMessage = "Copy combo not implemented";

        [RelayCommand]
        private void CopyProfile(Account? account) => StatusMessage = "Copy profile not implemented";

        [RelayCommand]
        private void CopyUserId(Account? account)
        {
            if (account == null) return;
            try { Clipboard.SetText(account.UserId.ToString()); StatusMessage = $"Copied User ID: {account.UserId}"; }
            catch { StatusMessage = "Failed to copy User ID"; }
        }

        [RelayCommand]
        private void SortAlphabetically()
        {
            try
            {
                var sorted = Accounts.OrderBy(a => a.Username).ToList();
                Accounts.Clear();
                foreach (var a in sorted) Accounts.Add(a);
                UpdateDisplayItems();
                StatusMessage = "Accounts sorted alphabetically";
            }
            catch { StatusMessage = "Failed to sort accounts"; }
        }

        [RelayCommand]
        private void QuickLogInAsync(Account? account) => StatusMessage = "Quick login not implemented";

        [RelayCommand]
        private void CreateGroup()
        {
            try
            {
                var prompt = new BloxManager.Views.PromptWindow("Create Group", "Enter a name for the new group:");
                if (prompt.ShowDialog() != true || string.IsNullOrWhiteSpace(prompt.InputText)) return;

                var newGroup = prompt.InputText.Trim();
                if (Groups.Contains(newGroup)) { StatusMessage = "Group already exists."; return; }

                EnsureGroupExists(newGroup);
                SyncAvailableGroupsForMove();
                UpdateDisplayItems();
                StatusMessage = $"Created group: {newGroup}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateGroup failed");
                StatusMessage = "Error creating group";
            }
        }

        [RelayCommand]
        private async Task MoveAccountToGroup(object? parameter)
        {
            try
            {
                if (parameter is not object[] values || values.Length < 2) return;
                var groupName = values[1] as string;
                if (string.IsNullOrEmpty(groupName) || !SelectedAccounts.Any()) return;

                foreach (var acc in SelectedAccounts.ToList())
                {
                    acc.Group = groupName;
                    await _accountService.UpdateAccountAsync(acc);
                }

                EnsureGroupExists(groupName);
                UpdateDisplayItems();
                StatusMessage = $"Moved {SelectedAccounts.Count} account(s) to {groupName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MoveAccountToGroup failed");
                StatusMessage = "Error moving accounts";
            }
        }

        [RelayCommand]
        public void ToggleGroupExpansion(AccountGroup? group)
        {
            if (group == null) return;
            group.IsExpanded = !group.IsExpanded;
            UpdateDisplayItems();
            StatusMessage = $"Group '{group.Name}' {(group.IsExpanded ? "expanded" : "collapsed")}";
        }

        [RelayCommand]
        public async Task DeleteGroupAsync(AccountGroup? group)
        {
            if (group == null || group.Name == "Default") return;

            try
            {
                foreach (var account in Accounts.Where(a => a.Group == group.Name).ToList())
                {
                    account.Group = "Default";
                    await _accountService.UpdateAccountAsync(account);
                }

                Groups.Remove(group.Name);
                _groupCache.Remove(group.Name);
                AvailableGroupsForMove.Remove(group.Name);
                UpdateDisplayItems();
                StatusMessage = $"Deleted group: {group.Name}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteGroupAsync failed");
                StatusMessage = "Error deleting group";
            }
        }

        public void EditGroupName(AccountGroup group, string newName)
        {
            if (group == null || string.IsNullOrWhiteSpace(newName)) return;

            try
            {
                if (newName == "Default" || Groups.Contains(newName))
                {
                    StatusMessage = "Group name already exists or cannot be 'Default'";
                    return;
                }

                var oldName = group.Name;

                foreach (var account in Accounts.Where(a => a.Group == oldName))
                {
                    account.Group = newName;
                    _ = _accountService.UpdateAccountAsync(account);
                }

                var idx = Groups.IndexOf(oldName);
                if (idx >= 0) Groups[idx] = newName;

                _groupCache.Remove(oldName);
                group.Name = newName;
                _groupCache[newName] = group;

                SyncAvailableGroupsForMove();
                UpdateDisplayItems();
                StatusMessage = $"Renamed group '{oldName}' → '{newName}'";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EditGroupName failed ({Old} → {New})", group?.Name, newName);
                StatusMessage = "Error renaming group";
            }
        }

        // ── UI thread helper ──────────────────────────────────────────────────

        /// <summary>Marshals an action onto the WPF dispatcher. Safe to call from any thread.</summary>
        private static Task RunOnUiAsync(Action action)
        {
            if (Application.Current?.Dispatcher == null) return Task.CompletedTask;
            if (Application.Current.Dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }
            return Application.Current.Dispatcher.InvokeAsync(action).Task;
        }

        // ── Compatibility stubs ───────────────────────────────────────────────
        public Task ReorderItemsAsync(object sender, object e) => Task.CompletedTask;

        // ── Cleanup ───────────────────────────────────────────────────────
        public void Dispose()
        {
            try
            {
                _presenceRefreshCts?.Cancel();
                _presenceRefreshCts?.Dispose();
                _uptimeCts?.Cancel();
                _uptimeCts?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during disposal");
            }
        }
    }
}
