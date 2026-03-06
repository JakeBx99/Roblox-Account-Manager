using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BloxManager.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly ILogger<SettingsService> _logger;
        private readonly IEncryptionService _encryptionService;
        private readonly Dictionary<string, object> _settings = new();
        private readonly string _settingsFilePath;

        private bool _isLoaded = false;
        private readonly SemaphoreSlim _settingsIoLock = new(1, 1);

        public SettingsService(ILogger<SettingsService> logger, IEncryptionService encryptionService)
        {
            _logger = logger;
            _encryptionService = encryptionService;
            
            var appDataPath = AppDomain.CurrentDomain.BaseDirectory;
            
            _settingsFilePath = Path.Combine(appDataPath, "settings.json");
            
            LoadDefaultSettings();
        }

        public async Task<T> GetSettingAsync<T>(string key, T defaultValue = default!)
        {
            if (!_isLoaded)
            {
                await LoadSettingsAsync();
                _isLoaded = true;
            }

            if (_settings.TryGetValue(key, out var value))
            {
                try
                {
                    if (value is Newtonsoft.Json.Linq.JValue jValue)
                    {
                        return (T)Convert.ChangeType(jValue.Value!, typeof(T));
                    }
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }

            return defaultValue;
        }

        public async Task SetSettingAsync<T>(string key, T value)
        {
            _settings[key] = value;
            await SaveSettingsAsync();
        }

        public async Task<bool> GetMultiRobloxEnabledAsync()
        {
            return await GetSettingAsync("MultiRobloxEnabled", false);
        }

        public async Task SetMultiRobloxEnabledAsync(bool enabled)
        {
            await SetSettingAsync("MultiRobloxEnabled", enabled);
        }

        public async Task<bool> GetSavePasswordsEnabledAsync()
        {
            return await GetSettingAsync("SavePasswordsEnabled", true);
        }

        public async Task SetSavePasswordsEnabledAsync(bool enabled)
        {
            await SetSettingAsync("SavePasswordsEnabled", enabled);
        }

        public async Task<string> GetThemeAsync()
        {
            return await GetSettingAsync("Theme", "Dark");
        }

        public async Task SetThemeAsync(string theme)
        {
            await SetSettingAsync("Theme", theme);
        }

        public async Task<bool> GetDeveloperModeEnabledAsync()
        {
            return await GetSettingAsync("DeveloperModeEnabled", false);
        }

        public async Task SetDeveloperModeEnabledAsync(bool enabled)
        {
            await SetSettingAsync("DeveloperModeEnabled", enabled);
        }

        public async Task<int> GetWebServerPortAsync()
        {
            return await GetSettingAsync("WebServerPort", 8080);
        }

        public async Task SetWebServerPortAsync(int port)
        {
            await SetSettingAsync("WebServerPort", port);
        }

        public async Task<string> GetDefaultGroupAsync()
        {
            return await GetSettingAsync("DefaultGroup", "Default");
        }

        public async Task SetDefaultGroupAsync(string group)
        {
            await SetSettingAsync("DefaultGroup", group);
        }

        public async Task<string> GetPlaceIdAsync()
        {
            return await GetSettingAsync("PlaceId", string.Empty);
        }

        public async Task SetPlaceIdAsync(string placeId)
        {
            await SetSettingAsync("PlaceId", placeId);
        }

        public async Task<string> GetJobIdAsync()
        {
            return await GetSettingAsync("JobId", string.Empty);
        }

        public async Task SetJobIdAsync(string jobId)
        {
            await SetSettingAsync("JobId", jobId);
        }

        public async Task<string> GetLaunchDataAsync()
        {
            return await GetSettingAsync("LaunchData", string.Empty);
        }


        public async Task<string> GetUpdateChannelAsync()
        {
            return await GetSettingAsync("UpdateChannel", "Premium");
        }

        public async Task SetUpdateChannelAsync(string channel)
        {
            await SetSettingAsync("UpdateChannel", channel);
        }

        public async Task<bool> GetLowMemoryModeAsync()
        {
            return await GetSettingAsync("LowMemoryMode", true);
        }

        public async Task SetLowMemoryModeAsync(bool enabled)
        {
            await SetSettingAsync("LowMemoryMode", enabled);
        }
        public async Task SetLaunchDataAsync(string launchData)
        {
            await SetSettingAsync("LaunchData", launchData);
        }

        public async Task<string?> GetLicenseUsernameAsync()
        {
            return await GetSettingAsync<string?>("LicenseUsername", null);
        }

        public async Task SetLicenseUsernameAsync(string? username)
        {
            await SetSettingAsync("LicenseUsername", username);
        }

        public async Task<bool> IsLicenseVerifiedAsync()
        {
            return await GetSettingAsync("LicenseVerified", false);
        }

        public async Task SetLicenseVerifiedAsync(bool verified)
        {
            await SetSettingAsync("LicenseVerified", verified);
        }

        public async Task<List<string>> GetPersistentGroupsAsync()
        {
            var json = await GetSettingAsync("PersistentGroups", "[\"Default\"]");
            try { return JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string> { "Default" }; }
            catch { return new List<string> { "Default" }; }
        }

        public async Task SetPersistentGroupsAsync(List<string> groups)
        {
            var json = JsonConvert.SerializeObject(groups);
            await SetSettingAsync("PersistentGroups", json);
        }

        private void LoadDefaultSettings()
        {
            _settings["MultiRobloxEnabled"] = true;
            _settings["SavePasswordsEnabled"] = true;
            _settings["Theme"] = "Dark";
            _settings["DeveloperModeEnabled"] = false;
            _settings["WebServerPort"] = 8080;
            _settings["DefaultGroup"] = "Default";
            _settings["AutoRefreshCookies"] = true;
            _settings["AutoRefreshInterval"] = 3600; // 1 hour
            _settings["MaxAccountsPerGroup"] = 100;
            _settings["EnableNotifications"] = true;
            _settings["MinimizeToTray"] = true;
            _settings["StartWithWindows"] = false;
            _settings["CheckForUpdates"] = true;
            _settings["Language"] = "en";
            _settings["PlaceId"] = string.Empty;
            _settings["JobId"] = string.Empty;
            _settings["LaunchData"] = string.Empty;
            _settings["LowMemoryMode"] = true;
            _settings["AltAccountBackgroundColor"] = string.Empty; 
            _settings["AltAccountOpacity"] = 100.0;
            _settings["GroupBackgroundColor"] = string.Empty;
            _settings["GroupOpacity"] = 100.0;
            _settings["CustomTextColor"] = "#FFFFFF"; // White default
        }

        private async Task LoadSettingsAsync()
        {
            await _settingsIoLock.WaitAsync();
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    LoadDefaultSettings();
                    return;
                }

                var encrypted = await File.ReadAllTextAsync(_settingsFilePath);
                var json = await _encryptionService.DecryptAsync(encrypted);
                
                if (!string.IsNullOrEmpty(json))
                {
                    var loadedSettings = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (loadedSettings != null)
                    {
                        _settings.Clear();
                        foreach (var kvp in loadedSettings)
                        {
                            _settings[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings");
                LoadDefaultSettings();
            }
            finally
            {
                _settingsIoLock.Release();
            }
        }

        private async Task SaveSettingsAsync()
        {
            await _settingsIoLock.WaitAsync();
            try
            {
                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                var encrypted = await _encryptionService.EncryptAsync(json);

                var dir = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var tmpPath = _settingsFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpPath, encrypted);

                if (File.Exists(_settingsFilePath))
                {
                    File.Replace(tmpPath, _settingsFilePath, null);
                }
                else
                {
                    File.Move(tmpPath, _settingsFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings");
            }
            finally
            {
                _settingsIoLock.Release();
            }
        }
    }
}
