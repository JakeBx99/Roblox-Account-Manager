using BloxManager.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BloxManager.Services
{
    public class AccountService : IAccountService
    {
        private readonly ILogger<AccountService> _logger;
        private readonly IEncryptionService _encryptionService;
        private readonly ISettingsService _settingsService;
        private readonly IRobloxService _robloxService;
        private readonly IBrowserService _browserService;
        
        private List<Account> _accounts = new();
        private readonly string _accountsFilePath;

        public AccountService(
            ILogger<AccountService> logger,
            IEncryptionService encryptionService,
            ISettingsService settingsService,
            IRobloxService robloxService,
            IBrowserService browserService)
        {
            _logger = logger;
            _encryptionService = encryptionService;
            _settingsService = settingsService;
            _robloxService = robloxService;
            _browserService = browserService;
            
            var appDataPath = AppDomain.CurrentDomain.BaseDirectory;
            
            _accountsFilePath = Path.Combine(appDataPath, "accounts.json");
        }

        public async Task<List<Account>> GetAccountsAsync()
        {
            if (_accounts.Count == 0)
            {
                await LoadAccountsAsync();
            }
            return _accounts.ToList();
        }

        public async Task RefreshAccountAvatarAsync(Account account)
        {
            if (account.UserId == 0) return;

            try
            {
                var avatarUrl = await _robloxService.GetUserAvatarUrlAsync(account.UserId);
                if (!string.IsNullOrEmpty(avatarUrl) && account.AvatarUrl != avatarUrl)
                {
                    account.AvatarUrl = avatarUrl;
                    await SaveAccountsAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to refresh avatar for account {account.Username}");
            }
        }

        public async Task<Account?> GetAccountById(string accountId)
        {
            var accounts = await GetAccountsAsync();
            return accounts.FirstOrDefault(a => a.Id == accountId);
        }

        public async Task<Account?> GetAccountAsync(string id)
        {
            return await GetAccountById(id);
        }

        public async Task AddAccountAsync(Account account)
        {
            if (string.IsNullOrEmpty(account.Id))
            {
                account.Id = Guid.NewGuid().ToString();
            }

            if (account.CreatedAt == default)
            {
                account.CreatedAt = DateTime.Now;
            }

            _accounts.Add(account);
            await SaveAccountsAsync();
            
            _logger.LogInformation($"Added account: {account.Username}");
        }

        public async Task UpdateAccountAsync(Account account)
        {
            var existingAccount = _accounts.FirstOrDefault(a => a.Id == account.Id);
            if (existingAccount != null)
            {
                var index = _accounts.IndexOf(existingAccount);
                _accounts[index] = account;
                await SaveAccountsAsync();
                
                _logger.LogInformation($"Updated account: {account.Username}");
            }
        }

        public async Task DeleteAccountAsync(string id)
        {
            var account = _accounts.FirstOrDefault(a => a.Id == id);
            if (account != null)
            {
                _accounts.Remove(account);
                await SaveAccountsAsync();
                
                _logger.LogInformation($"Deleted account: {account.Username}");
            }
        }

        public async Task<bool> ValidateAccountAsync(Account account)
        {
            try
            {
                if (!string.IsNullOrEmpty(account.SecurityToken))
                {
                    var userInfo = await _robloxService.GetUserInfoAsync(account.SecurityToken);
                    if (userInfo != null)
                    {
                        account.UserId = userInfo.UserId;
                        account.Username = userInfo.Username;
                        account.IsValid = true;
                        return true;
                    }
                }
                else if (!string.IsNullOrEmpty(account.Username) && !string.IsNullOrEmpty(account.Password))
                {
                    var loginResult = await _robloxService.LoginAsync(account.Username, account.Password);
                    if (loginResult != null)
                    {
                        account.UserId = loginResult.UserId;
                        account.SecurityToken = loginResult.SecurityToken;
                        account.IsValid = true;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to validate account {account.Username}");
            }
            return false;
        }

        public async Task<Account?> LoginAsync(string username, string password, bool keepBrowserOpenUntilSuccess = false)
        {
            try
            {
                // Prefer browser-based login to match RAM behavior
                var cookie = await _browserService.LoginAndGetCookieAsync(username, password, default, closeOnlyOnSuccess: keepBrowserOpenUntilSuccess);
                LoginResult? loginResult = null;
                if (!string.IsNullOrEmpty(cookie))
                {
                    var info = await _robloxService.GetUserInfoAsync(cookie);
                    if (info != null)
                    {
                        loginResult = new LoginResult
                        {
                            SecurityToken = cookie,
                            UserId = info.UserId,
                            Username = info.Username
                        };
                    }
                }
                // Fallback to API login if browser login failed
                if (loginResult == null)
                {
                    loginResult = await _robloxService.LoginAsync(username, password);
                }
                if (loginResult != null)
                {
                    var account = new Account(username, password)
                    {
                        SecurityToken = loginResult.SecurityToken,
                        UserId = loginResult.UserId,
                        IsValid = true
                    };

                    await AddAccountAsync(account);
                    return account;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to login with username: {username}");
            }

            return null;
        }

        public async Task<Account?> LoginWithCookieAsync(string cookie)
        {
            try
            {
                var userInfo = await _robloxService.GetUserInfoAsync(cookie);
                if (userInfo != null)
                {
                    var account = new Account(cookie)
                    {
                        Username = userInfo.Username,
                        UserId = userInfo.UserId,
                        IsValid = true
                    };
                    
                    try
                    {
                        var avatar = await _robloxService.GetUserAvatarUrlAsync(userInfo.UserId);
                        if (!string.IsNullOrEmpty(avatar))
                        {
                            account.AvatarUrl = avatar;
                        }
                    }
                    catch
                    {
                        account.AvatarUrl = string.Empty;
                    }

                    await AddAccountAsync(account);
                    return account;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to login with cookie");
            }

            return null;
        }

        public async Task LogoutAsync(Account account)
        {
            try
            {
                await _robloxService.LogoutAsync(account.SecurityToken);
                account.SecurityToken = string.Empty;
                account.IsValid = false;
                await SaveAccountsAsync();
                
                _logger.LogInformation($"Logged out account: {account.Username}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to logout account {account.Username}");
            }
        }

        public async Task RefreshAccountAsync(Account account)
        {
            if (string.IsNullOrEmpty(account.SecurityToken))
            {
                await ValidateAccountAsync(account);
                return;
            }

            try
            {
                var userInfo = await _robloxService.GetUserInfoAsync(account.SecurityToken);
                if (userInfo != null)
                {
                    account.LastAttemptedRefresh = DateTime.Now;
                    account.IsValid = true;
                    await SaveAccountsAsync();
                }
                else
                {
                    account.IsValid = false;
                    await SaveAccountsAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to refresh account {account.Username}");
            }
        }

        public async Task<List<Account>> GetAccountsByGroupAsync(string group)
        {
            var accounts = await GetAccountsAsync();
            return accounts.Where(a => a.Group.Equals(group, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public async Task<List<string>> GetGroupsAsync()
        {
            var accounts = await GetAccountsAsync();
            return accounts.Select(a => a.Group).Distinct().OrderBy(g => g).ToList();
        }

        public async Task SaveAccountsAsync()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_accounts, Formatting.Indented);
                var encrypted = await _encryptionService.EncryptAsync(json);
                await File.WriteAllTextAsync(_accountsFilePath, encrypted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save accounts");
                throw;
            }
        }

        public async Task LoadAccountsAsync()
        {
            try
            {
                if (!File.Exists(_accountsFilePath))
                {
                    _accounts = new List<Account>();
                    await SaveAccountsAsync();
                    return;
                }

                var encrypted = await File.ReadAllTextAsync(_accountsFilePath);
                var json = await _encryptionService.DecryptAsync(encrypted);
                
                if (!string.IsNullOrEmpty(json))
                {
                    _accounts = JsonConvert.DeserializeObject<List<Account>>(json) ?? new List<Account>();
                }
                else
                {
                    _accounts = new List<Account>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load accounts");
                _accounts = new List<Account>();
            }
        }

        public async Task<string> ExportAccountsAsync(List<Account> accounts)
        {
            var exportData = new
            {
                ExportedAt = DateTime.Now,
                Version = "1.0",
                Accounts = accounts.Select(a => new
                {
                    a.Username,
                    a.Password,
                    a.SecurityToken,
                    a.Alias,
                    a.Description,
                    a.Group,
                    a.Fields,
                    a.IsFavorite
                })
            };

            return JsonConvert.SerializeObject(exportData, Formatting.Indented);
        }

        public async Task<List<Account>> ImportAccountsAsync(string data)
        {
            try
            {
                var importData = JsonConvert.DeserializeAnonymousType(data, new
                {
                    Accounts = new[]
                    {
                        new
                        {
                            Username = "",
                            Password = "",
                            SecurityToken = "",
                            Alias = "",
                            Description = "",
                            Group = "",
                            Fields = new Dictionary<string, string>(),
                            IsFavorite = false
                        }
                    }
                });

                var importedAccounts = new List<Account>();

                if (importData?.Accounts != null)
                {
                    foreach (var accountData in importData.Accounts)
                    {
                        var account = new Account
                        {
                            Username = accountData.Username,
                            Password = accountData.Password,
                            SecurityToken = accountData.SecurityToken,
                            Alias = accountData.Alias,
                            Description = accountData.Description,
                            Group = accountData.Group,
                            Fields = accountData.Fields ?? new Dictionary<string, string>(),
                            IsFavorite = accountData.IsFavorite
                        };

                        importedAccounts.Add(account);
                        await AddAccountAsync(account);
                    }
                }

                return importedAccounts;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import accounts");
                throw;
            }
        }
    }
}
