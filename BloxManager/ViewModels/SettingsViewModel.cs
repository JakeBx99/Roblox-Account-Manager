using BloxManager.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;

namespace BloxManager.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ILogger<SettingsViewModel> _logger;
        private readonly ISettingsService _settingsService;
        private readonly IGameService _gameService;

        [ObservableProperty]
        private bool _multiRobloxEnabled;

        partial void OnMultiRobloxEnabledChanged(bool value)
        {
            _ = _settingsService.SetMultiRobloxEnabledAsync(value);
        }

        [ObservableProperty]
        private bool _savePasswordsEnabled;

        partial void OnSavePasswordsEnabledChanged(bool value)
        {
            _ = _settingsService.SetSavePasswordsEnabledAsync(value);
        }

        [ObservableProperty]
        private string _selectedTheme = "Dark";

        partial void OnSelectedThemeChanged(string value)
        {
            _ = _settingsService.SetThemeAsync(value);
        }

        [ObservableProperty]
        private bool _developerModeEnabled;

        partial void OnDeveloperModeEnabledChanged(bool value)
        {
            _ = _settingsService.SetDeveloperModeEnabledAsync(value);
        }

        [ObservableProperty]
        private int _webServerPort;

        partial void OnWebServerPortChanged(int value)
        {
            _ = _settingsService.SetWebServerPortAsync(value);
        }

        [ObservableProperty]
        private string _defaultGroup = "Default";

        partial void OnDefaultGroupChanged(string value)
        {
            _ = _settingsService.SetDefaultGroupAsync(value);
        }

        [ObservableProperty]
        private bool _autoRefreshCookies;

        partial void OnAutoRefreshCookiesChanged(bool value)
        {
            _ = _settingsService.SetSettingAsync("AutoRefreshCookies", value);
        }

        [ObservableProperty]
        private int _autoRefreshInterval;

        partial void OnAutoRefreshIntervalChanged(int value)
        {
            _ = _settingsService.SetSettingAsync("AutoRefreshInterval", value);
        }

        [ObservableProperty]
        private bool _enableNotifications;

        partial void OnEnableNotificationsChanged(bool value)
        {
            _ = _settingsService.SetSettingAsync("EnableNotifications", value);
        }

        [ObservableProperty]
        private bool _minimizeToTray;

        partial void OnMinimizeToTrayChanged(bool value)
        {
            _ = _settingsService.SetSettingAsync("MinimizeToTray", value);
        }

        [ObservableProperty]
        private bool _startWithWindows;

        [ObservableProperty]
        private bool _checkForUpdates;

        partial void OnCheckForUpdatesChanged(bool value)
        {
            _ = _settingsService.SetSettingAsync("CheckForUpdates", value);
        }

        [ObservableProperty]
        private bool _alwaysOnTop;

        partial void OnAlwaysOnTopChanged(bool value)
        {
            _ = _settingsService.SetSettingAsync("AlwaysOnTop", value);
        }


        [ObservableProperty]
        private string _placeId = string.Empty;

        partial void OnPlaceIdChanged(string value)
        {
            _ = _settingsService.SetPlaceIdAsync(value);
            _ = UpdateGameNameAsync(value);
        }

        [ObservableProperty]
        private string _jobId = string.Empty;

        partial void OnJobIdChanged(string value)
        {
            _ = _settingsService.SetJobIdAsync(value);
        }

        [ObservableProperty]
        private string _launchData = string.Empty;

        partial void OnLaunchDataChanged(string value)
        {
            _ = _settingsService.SetLaunchDataAsync(value);
        }

        [ObservableProperty]
        private string _gameName = string.Empty;

        [ObservableProperty]
        private string _selectedLanguage = "en";

        partial void OnSelectedLanguageChanged(string value)
        {
            _ = _settingsService.SetSettingAsync("SelectedLanguage", value);
        }

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private string _backgroundImagePath = string.Empty;


        [ObservableProperty]
        private string _backgroundImageDimensions = string.Empty;

        partial void OnBackgroundImagePathChanged(string value)
        {
            _ = _settingsService.SetSettingAsync("BackgroundImagePath", value);
            UpdateImageDimensions(value);
            AutoAdjustThemeFromBackground();
        }

        private void UpdateImageDimensions(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                BackgroundImageDimensions = string.Empty;
                return;
            }

            try
            {
                using var image = System.Drawing.Image.FromFile(imagePath);
                BackgroundImageDimensions = $"{image.Width} × {image.Height} px";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get image dimensions for {ImagePath}", imagePath);
                BackgroundImageDimensions = "Invalid image";
            }
        }

        private void AutoAdjustThemeFromBackground()
        {
            try
            {
                var app = System.Windows.Application.Current;
                if (app == null) return;
                var resources = app.Resources;
                if (resources == null) return;

                string path = BackgroundImagePath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    // No background: keep default (light-on-dark)
                    resources["CustomTextBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                    return;
                }

                double luminanceSum = 0;
                int count = 0;
                using (var bmp = new System.Drawing.Bitmap(path))
                {
                    int stepX = Math.Max(1, bmp.Width / 64);
                    int stepY = Math.Max(1, bmp.Height / 64);
                    for (int y = 0; y < bmp.Height; y += stepY)
                    {
                        for (int x = 0; x < bmp.Width; x += stepX)
                        {
                            var p = bmp.GetPixel(x, y);
                            // ITU-R BT.709 luminance
                            double l = (0.2126 * p.R + 0.7152 * p.G + 0.0722 * p.B) / 255.0;
                            luminanceSum += l;
                            count++;
                        }
                    }
                }

                double avg = count > 0 ? luminanceSum / count : 0.5;
                bool isLight = avg >= 0.6;
                var textColor = isLight ? System.Windows.Media.Colors.Black : System.Windows.Media.Colors.White;
                resources["CustomTextBrush"] = new System.Windows.Media.SolidColorBrush(textColor);

                var bgColor = isLight
                    ? System.Windows.Media.Color.FromArgb(0x19, 0x00, 0x00, 0x00)
                    : System.Windows.Media.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);
                resources["CustomAltAccountBrush"] = new System.Windows.Media.SolidColorBrush(bgColor);
                resources["CustomGroupBrush"] = new System.Windows.Media.SolidColorBrush(bgColor);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoAdjustThemeFromBackground failed");
            }
        }

        [ObservableProperty]
        private string _backgroundImageStretch = "UniformToFill";

        partial void OnBackgroundImageStretchChanged(string value)
        {
            _ = _settingsService.SetSettingAsync("BackgroundImageStretch", value);
        }

        [ObservableProperty]
        private string _backgroundImageAlignment = "Center";

        partial void OnBackgroundImageAlignmentChanged(string value)
        {
            _ = _settingsService.SetSettingAsync("BackgroundImageAlignment", value);
        }

        [ObservableProperty]
        private double _backgroundImageOpacity = 1.0;

        partial void OnBackgroundImageOpacityChanged(double value)
        {
            _ = _settingsService.SetSettingAsync("BackgroundImageOpacity", value);
        }

        [ObservableProperty]
        private string _selectedCategory = "General";
        
        [ObservableProperty]
        private int _joinDelaySeconds = 2;

        partial void OnJoinDelaySecondsChanged(int value)
        {
            _ = _settingsService.SetSettingAsync("JoinDelaySeconds", value);
        }

        [ObservableProperty]
        private string _altAccountBackgroundColor = "#000000";

        partial void OnAltAccountBackgroundColorChanged(string value)
        {
            _ = _settingsService.SetSettingAsync("AltAccountBackgroundColor", value);
            if (!IsLoading) UpdateMainAppResources();
        }

        [ObservableProperty]
        private double _altAccountOpacity = 100.0;

        partial void OnAltAccountOpacityChanged(double value)
        {
            _ = _settingsService.SetSettingAsync("AltAccountOpacity", value);
            if (!IsLoading) UpdateMainAppResources();
        }

        [ObservableProperty]
        private string _groupBackgroundColor = "#000000";

        partial void OnGroupBackgroundColorChanged(string value)
        {
            _ = _settingsService.SetSettingAsync("GroupBackgroundColor", value);
            if (!IsLoading) UpdateMainAppResources();
        }

        [ObservableProperty]
        private double _groupOpacity = 100.0;

        partial void OnGroupOpacityChanged(double value)
        {
            _ = _settingsService.SetSettingAsync("GroupOpacity", value);
            if (!IsLoading) UpdateMainAppResources();
        }

        [ObservableProperty]
        private string _customTextColor = "#FFFFFF";

        partial void OnCustomTextColorChanged(string value)
        {
            _ = _settingsService.SetSettingAsync("CustomTextColor", value);
            if (!IsLoading) UpdateMainAppResources();
        }

        private void UpdateMainAppResources()
        {
            var app = System.Windows.Application.Current;
            if (app == null) return;

            app.Dispatcher.Invoke(() =>
            {
                try
                {
                    var resources = app.Resources;
                    if (resources == null) return;

                    if (!string.IsNullOrEmpty(AltAccountBackgroundColor))
                    {
                        try 
                        {
                            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(AltAccountBackgroundColor);
                            resources["CustomAltAccountBrush"] = new System.Windows.Media.SolidColorBrush(color);
                        }
                        catch { resources["CustomAltAccountBrush"] = null; }
                    }
                    else
                    {
                        resources["CustomAltAccountBrush"] = null;
                    }
                    resources["CustomAltAccountOpacity"] = AltAccountOpacity / 100.0;

                    if (!string.IsNullOrEmpty(GroupBackgroundColor))
                    {
                        try 
                        {
                            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(GroupBackgroundColor);
                            resources["CustomGroupBrush"] = new System.Windows.Media.SolidColorBrush(color);
                        }
                        catch { resources["CustomGroupBrush"] = null; }
                    }
                    else
                    {
                        resources["CustomGroupBrush"] = null;
                    }
                    resources["CustomGroupOpacity"] = GroupOpacity / 100.0;

                    if (!string.IsNullOrEmpty(CustomTextColor))
                    {
                        try 
                        {
                            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(CustomTextColor);
                            resources["CustomTextBrush"] = new System.Windows.Media.SolidColorBrush(color);
                        }
                        catch { resources["CustomTextBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White); }
                    }
                    else
                    {
                        resources["CustomTextBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update main app resources");
                }
            });
        }

        public string[] AvailableThemes { get; } = { "Light", "Dark", "System" };
        public string[] AvailableLanguages { get; } = { "en", "es", "fr", "de", "pt", "ru", "ja", "ko", "zh" };
        public class StretchOption
        {
            public string Key { get; set; } = "";
            public string Label { get; set; } = "";
            public override string ToString() => Label;
        }
        public System.Collections.Generic.List<StretchOption> StretchModeOptions { get; } =
            new System.Collections.Generic.List<StretchOption>()
            {
                new StretchOption{ Key = "None", Label = "None" },
                new StretchOption{ Key = "Fill", Label = "Fill" },
                new StretchOption{ Key = "Uniform", Label = "Uniform" },
                new StretchOption{ Key = "UniformToFill", Label = "Uniform to fill" }
            };
        public string[] AvailableAlignments { get; } = { "Center", "Left", "Right", "Top", "Bottom", "TopLeft", "TopRight", "BottomLeft", "BottomRight" };

        public SettingsViewModel(
            ILogger<SettingsViewModel> logger,
            ISettingsService settingsService,
            IGameService gameService)
        {
            _logger = logger;
            _settingsService = settingsService;
            _gameService = gameService;
            
            _ = LoadSettingsAsync();
        }

        [ObservableProperty]
        private bool _lowMemoryMode = false;
        partial void OnLowMemoryModeChanged(bool value)
        {
            _ = _settingsService.SetLowMemoryModeAsync(value);
        }

        private async Task UpdateGameNameAsync(string placeIdStr)
        {
            if (long.TryParse(placeIdStr, out var placeId))
            {
                GameName = "Fetching game...";
                try 
                {
                    var game = await _gameService.GetGameDetailsAsync(placeId);
                    if (game != null && !string.IsNullOrEmpty(game.Name))
                    {
                        GameName = game.Name;
                    }
                    else
                    {
                        // Try secondary lookup via Roblox API if multiget failed
                        GameName = "Game not found";
                    }
                }
                catch
                {
                    GameName = "Error fetching game";
                }
            }
            else
            {
                GameName = string.Empty;
            }
        }

        [RelayCommand]
        private void SelectBackgroundImage()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Image files (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All files (*.*)|*.*";
            if (openFileDialog.ShowDialog() == true)
            {
                BackgroundImagePath = openFileDialog.FileName;
            }
        }

        [RelayCommand]
        private void ClearBackgroundImage()
        {
            BackgroundImagePath = string.Empty;
        }

        [RelayCommand]
        private void SetAlignment(string alignment)
        {
            BackgroundImageAlignment = alignment;
        }

        [RelayCommand]
        private void ResetOpacity()
        {
            BackgroundImageOpacity = 1.0;
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading settings...";

                MultiRobloxEnabled = await _settingsService.GetMultiRobloxEnabledAsync();
                SavePasswordsEnabled = await _settingsService.GetSavePasswordsEnabledAsync();
                SelectedTheme = await _settingsService.GetThemeAsync();
                DeveloperModeEnabled = await _settingsService.GetDeveloperModeEnabledAsync();
                WebServerPort = await _settingsService.GetWebServerPortAsync();
                PlaceId = await _settingsService.GetPlaceIdAsync();
                JobId = await _settingsService.GetJobIdAsync();
                LaunchData = await _settingsService.GetLaunchDataAsync();
                
                DefaultGroup = await _settingsService.GetSettingAsync<string>("DefaultGroup") ?? "Default";
                AutoRefreshCookies = await _settingsService.GetSettingAsync<bool>("AutoRefreshCookies");
                AutoRefreshInterval = await _settingsService.GetSettingAsync<int>("AutoRefreshInterval");
                EnableNotifications = await _settingsService.GetSettingAsync<bool>("EnableNotifications");
                MinimizeToTray = await _settingsService.GetSettingAsync<bool>("MinimizeToTray");
                StartWithWindows = await _settingsService.GetSettingAsync<bool>("StartWithWindows");
                CheckForUpdates = await _settingsService.GetSettingAsync<bool>("CheckForUpdates");
                AlwaysOnTop = await _settingsService.GetSettingAsync<bool>("AlwaysOnTop");
                SelectedLanguage = await _settingsService.GetSettingAsync<string>("SelectedLanguage") ?? "en";

                
                BackgroundImagePath = await _settingsService.GetSettingAsync<string>("BackgroundImagePath") ?? string.Empty;
                BackgroundImageStretch = await _settingsService.GetSettingAsync<string>("BackgroundImageStretch") ?? "UniformToFill";
                BackgroundImageAlignment = await _settingsService.GetSettingAsync<string>("BackgroundImageAlignment") ?? "Center";
                BackgroundImageOpacity = await _settingsService.GetSettingAsync<double>("BackgroundImageOpacity", 1.0);
                JoinDelaySeconds = await _settingsService.GetSettingAsync("JoinDelaySeconds", 2);
                
                AltAccountBackgroundColor = await _settingsService.GetSettingAsync("AltAccountBackgroundColor", string.Empty);
                AltAccountOpacity = await _settingsService.GetSettingAsync("AltAccountOpacity", 100.0);
                GroupBackgroundColor = await _settingsService.GetSettingAsync("GroupBackgroundColor", string.Empty);
                GroupOpacity = await _settingsService.GetSettingAsync("GroupOpacity", 100.0);
                CustomTextColor = await _settingsService.GetSettingAsync("CustomTextColor", "#FFFFFF");
                
                LowMemoryMode = await _settingsService.GetLowMemoryModeAsync();
                
                UpdateMainAppResources();
                AutoAdjustThemeFromBackground();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings");
                StatusMessage = "Error loading settings";
            }
            finally
            {
                IsLoading = false;
                StatusMessage = "Ready";
            }
        }

        [RelayCommand]
        private async Task SaveSettingsAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Saving settings...";

                await _settingsService.SetMultiRobloxEnabledAsync(MultiRobloxEnabled);
                await _settingsService.SetSavePasswordsEnabledAsync(SavePasswordsEnabled);
                await _settingsService.SetThemeAsync(SelectedTheme);
                await _settingsService.SetDeveloperModeEnabledAsync(DeveloperModeEnabled);
                await _settingsService.SetWebServerPortAsync(WebServerPort);
                await _settingsService.SetDefaultGroupAsync(DefaultGroup);
                await _settingsService.SetSettingAsync("AutoRefreshCookies", AutoRefreshCookies);
                await _settingsService.SetSettingAsync("AutoRefreshInterval", AutoRefreshInterval);
                await _settingsService.SetSettingAsync("EnableNotifications", EnableNotifications);
                await _settingsService.SetSettingAsync("MinimizeToTray", MinimizeToTray);
                await _settingsService.SetSettingAsync("StartWithWindows", StartWithWindows);
                await _settingsService.SetSettingAsync("CheckForUpdates", CheckForUpdates);
                await _settingsService.SetSettingAsync("SelectedLanguage", SelectedLanguage);
                
                await _settingsService.SetPlaceIdAsync(PlaceId);
                await _settingsService.SetJobIdAsync(JobId);
                await _settingsService.SetLaunchDataAsync(LaunchData);
                await _settingsService.SetSettingAsync("BackgroundImagePath", BackgroundImagePath);
                await _settingsService.SetSettingAsync("BackgroundImageStretch", BackgroundImageStretch);
                await _settingsService.SetSettingAsync("BackgroundImageAlignment", BackgroundImageAlignment);
                await _settingsService.SetSettingAsync("BackgroundImageOpacity", BackgroundImageOpacity);
                await _settingsService.SetSettingAsync("AltAccountBackgroundColor", AltAccountBackgroundColor);
                await _settingsService.SetSettingAsync("AltAccountOpacity", AltAccountOpacity);
                await _settingsService.SetSettingAsync("GroupBackgroundColor", GroupBackgroundColor);
                await _settingsService.SetSettingAsync("GroupOpacity", GroupOpacity);
                await _settingsService.SetSettingAsync("CustomTextColor", CustomTextColor);
                await _settingsService.SetLowMemoryModeAsync(LowMemoryMode);

                StatusMessage = "Settings saved";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings");
                StatusMessage = "Error saving settings";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ResetSettingsAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Resetting settings...";

                MultiRobloxEnabled = true;
                SavePasswordsEnabled = true;
                SelectedTheme = "Dark";
                DeveloperModeEnabled = false;
                WebServerPort = 8080;
                DefaultGroup = "Default";
                AutoRefreshCookies = true;
                AutoRefreshInterval = 3600;
                EnableNotifications = true;
                MinimizeToTray = true;
                StartWithWindows = false;
                CheckForUpdates = true;
                SelectedLanguage = "en";

                PlaceId = string.Empty;
                JobId = string.Empty;
                LaunchData = string.Empty;

                BackgroundImagePath = string.Empty;
                BackgroundImageStretch = "UniformToFill";
                BackgroundImageAlignment = "Center";
                BackgroundImageOpacity = 1.0;

                AltAccountBackgroundColor = string.Empty;
                AltAccountOpacity = 100.0;
                GroupBackgroundColor = string.Empty;
                GroupOpacity = 100.0;
                CustomTextColor = "#FFFFFF";
                
                LowMemoryMode = true;
                AlwaysOnTop = false;
                JoinDelaySeconds = 2;

                await SaveSettingsAsync();
                UpdateMainAppResources();
                StatusMessage = "Defaults restored";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset settings");
                StatusMessage = "Error resetting settings";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ExportSettingsAsync()
        {
            // TODO: Implement settings export
            StatusMessage = "Export settings feature coming soon";
        }

        [RelayCommand]
        private async Task ImportSettingsAsync()
        {
            // TODO: Implement settings import
            StatusMessage = "Import settings feature coming soon";
        }

        [RelayCommand]
        private void OpenDataFolder()
        {
            try
            {
                var appDataPath = AppDomain.CurrentDomain.BaseDirectory;
                
                System.Diagnostics.Process.Start("explorer.exe", appDataPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open data folder");
                StatusMessage = "Error opening data folder";
            }
        }

        [RelayCommand]
        private void OpenLogsFolder()
        {
            try
            {
                var appDataPath = AppDomain.CurrentDomain.BaseDirectory;
                var logsPath = System.IO.Path.Combine(appDataPath, "logs");
                System.IO.Directory.CreateDirectory(logsPath);
                System.Diagnostics.Process.Start("explorer.exe", logsPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open logs folder");
                StatusMessage = "Error opening logs folder";
            }
        }

        [RelayCommand]
        private void OpenErrorLogsFile()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var outPath = System.IO.Path.Combine(baseDir, "error_logs.txt");
                if (!System.IO.File.Exists(outPath))
                    System.IO.File.WriteAllText(outPath, "No errors logged yet.");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = outPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open error logs file");
                StatusMessage = "Error opening error logs file";
            }
        }

        [RelayCommand]
        private void ClearLogs()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var outPath = System.IO.Path.Combine(baseDir, "error_logs.txt");
                if (System.IO.File.Exists(outPath))
                {
                    System.IO.File.WriteAllText(outPath, string.Empty);
                    StatusMessage = "Logs cleared successfully";
                }
                else
                {
                    StatusMessage = "No logs found to clear";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear logs");
                StatusMessage = "Error clearing logs";
            }
        }
    }
}
