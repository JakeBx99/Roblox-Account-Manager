using BloxManager.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

namespace BloxManager.Services
{
    public class RobloxService : IRobloxService
    {
        private readonly ILogger<RobloxService> _logger;
        private readonly RestClient _client;
        private readonly RestClient _usersClient;
        private readonly RestClient _gamesClient;
        private readonly RestClient _authClient;
        private readonly RestClient _thumbsClient;
        private static readonly Random _sharedRandom = new Random();
        private static Mutex? _rbxMultiMutex;

        private const string BaseUrl = "https://www.roblox.com";
        private const string UsersApiUrl = "https://users.roblox.com";
        private const string GamesApiUrl = "https://games.roblox.com";
        private const string AuthApiUrl = "https://auth.roblox.com";
        private const string InventoryApiUrl = "https://inventory.roblox.com";
        private const string ThumbnailsApiUrl = "https://thumbnails.roblox.com";

        public RobloxService(ILogger<RobloxService> logger)
        {
            _logger = logger;
            _client = new RestClient(BaseUrl);
            _usersClient = new RestClient(UsersApiUrl);
            _gamesClient = new RestClient(GamesApiUrl);
            _authClient = new RestClient(AuthApiUrl);
            _thumbsClient = new RestClient(ThumbnailsApiUrl);
        }

        public async Task<bool> IsGamePassOwnedAsync(long userId, long gamePassId)
        {
            try
            {
                var inventoryClient = new RestClient(InventoryApiUrl);
                var request = new RestRequest($"/v1/users/{userId}/items/GamePass/{gamePassId}/is-owned", Method.Get);
                var response = await inventoryClient.ExecuteAsync(request);

                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    return response.Content.Trim().ToLower() == "true";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check game pass ownership for user {UserId} and game pass {GamePassId}", userId, gamePassId);
            }
            return false;
        }

        public async Task<string?> GetUserAvatarUrlAsync(long userId)
        {
            try
            {
                // Primary: Thumbnails API
                var request = new RestRequest($"/v1/users/avatar-headshot", Method.Get);
                request.AddParameter("userIds", userId);
                request.AddParameter("size", "420x420");
                request.AddParameter("format", "Png");
                request.AddParameter("isCircular", "true");
                var response = await _thumbsClient.ExecuteAsync(request);

                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    var data = JsonConvert.DeserializeObject<dynamic>(response.Content);
                    if (data?.data != null && data.data.Count > 0 && data.data[0]?.imageUrl != null)
                    {
                        return data.data[0]?.imageUrl;
                    }
                }

                // Fallback: legacy headshot endpoint (direct image)
                return $"https://www.roblox.com/headshot-thumbnail/image?userId={userId}&width=420&height=420&format=png";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get avatar URL for user {UserId}", userId);
            }
            return null;
        }

        public bool UpdateMultiRoblox(bool enabled)
        {
            try
            {
                if (enabled && _rbxMultiMutex == null)
                {
                    _rbxMultiMutex = new Mutex(true, "ROBLOX_singletonMutex");

                    if (!_rbxMultiMutex.WaitOne(TimeSpan.Zero, true))
                    {
                        _logger.LogWarning("UpdateMultiRoblox: Could not acquire ROBLOX_singletonMutex. Multi-instance might not work.");
                        return false;
                    }
                    _logger.LogInformation("UpdateMultiRoblox: Successfully acquired ROBLOX_singletonMutex.");
                }
                else if (!enabled && _rbxMultiMutex != null)
                {
                    _rbxMultiMutex.ReleaseMutex();
                    _rbxMultiMutex.Close();
                    _rbxMultiMutex = null;
                    _logger.LogInformation("UpdateMultiRoblox: Released ROBLOX_singletonMutex.");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateMultiRoblox: Error handling mutex");
                return false;
            }
        }

        public async Task<LoginResult?> LoginAsync(string username, string password)
        {
            try
            {
                var request = new RestRequest("/v2/login", Method.Post);
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("X-CSRF-TOKEN", await GetCsrfTokenAsync(string.Empty));
                request.AddJsonBody(new
                {
                    ctype = "username",
                    cvalue = username,
                    password = password
                });

                var response = await _authClient.ExecuteAsync(request);

                if (response.IsSuccessful)
                {
                    var data = JsonConvert.DeserializeObject<dynamic>(response.Content ?? string.Empty);
                    if (data?.user?.id != null)
                    {
                        return new LoginResult
                        {
                            SecurityToken = ExtractCookieFromResponse(response),
                            UserId = (long)data.user.id,
                            Username = (string)(data.user.name ?? string.Empty)
                        };
                    }
                }
                else
                {
                    _logger.LogWarning("Login failed. Status: {StatusCode}, Content: {Content}", response.StatusCode, response.Content ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed for user: {Username}", username);
            }

            return null;
        }

        public async Task<UserInfo?> GetUserInfoAsync(string cookie)
        {
            try
            {
                cookie = (cookie ?? string.Empty).Trim().Trim('"').Replace("\r", "").Replace("\n", "");
                var request = new RestRequest("/v1/users/authenticated", Method.Get);
                // Set both Cookie header and cookie container for maximum compatibility
                request.AddHeader("Cookie", $".ROBLOSECURITY={cookie}");
                request.AddCookie(".ROBLOSECURITY", cookie, "/", ".roblox.com");
                request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                request.AddHeader("Referer", "https://www.roblox.com/");
                request.AddHeader("Origin", "https://www.roblox.com");
                request.AddHeader("Accept", "application/json, text/plain, */*");
                request.AddHeader("Accept-Language", "en-US,en;q=0.9");
                // Some reverse proxies expect a CSRF header even on GET when Cookie is present
                var csrf = await GetCsrfTokenAsync(cookie);
                if (!string.IsNullOrEmpty(csrf))
                {
                    request.AddHeader("X-CSRF-TOKEN", csrf);
                }

                var response = await _usersClient.ExecuteAsync(request);

                _logger.LogInformation("GetUserInfo response status: {StatusCode}", response.StatusCode);

                if (response.IsSuccessful)
                {
                    var data = JsonConvert.DeserializeObject<dynamic>(response.Content ?? string.Empty);
                    if (data?.id != null)
                    {
                        return new UserInfo
                        {
                            UserId = (long)data.id,
                            Username = (string)(data.name ?? string.Empty),
                            DisplayName = (string)(data.displayName ?? string.Empty)
                        };
                    }
                    else
                    {
                        _logger.LogWarning("GetUserInfo: response missing id field. Content: {Content}", response.Content ?? string.Empty);
                    }
                }
                else
                {
                    _logger.LogWarning("GetUserInfo: unsuccessful response. Status: {StatusCode}, Content: {Content}", response.StatusCode, response.Content ?? string.Empty);
                    // Fallback: mobile userinfo endpoint on www.roblox.com
                    var alt = new RestRequest("/mobileapi/userinfo", Method.Get);
                    alt.AddHeader("Cookie", $".ROBLOSECURITY={cookie}");
                    alt.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    alt.AddHeader("Referer", "https://www.roblox.com/");
                    alt.AddHeader("Accept", "application/json, text/plain, */*");
                    var altResp = await _client.ExecuteAsync(alt);
                    if (altResp.IsSuccessful && !string.IsNullOrEmpty(altResp.Content) && altResp.Content.TrimStart().StartsWith("{"))
                    {
                        try
                        {
                            var t = JsonConvert.DeserializeObject<dynamic>(altResp.Content);
                            if (t != null && t.UserID != null)
                            {
                                return new UserInfo
                                {
                                    UserId = (long)t.UserID,
                                    Username = (string)(t.UserName ?? ""),
                                    DisplayName = (string)(t.DisplayName ?? (t.UserName ?? ""))
                                };
                            }
                        }
                        catch (Exception ex2)
                        {
                            _logger.LogWarning(ex2, "Failed to parse mobile userinfo content");
                        }
                    }
                    // Fallback 2: legacy settings json (authenticated context)
                    var alt2 = new RestRequest("/my/settings/json", Method.Get);
                    alt2.AddHeader("Cookie", $".ROBLOSECURITY={cookie}");
                    alt2.AddHeader("Accept", "application/json, text/plain, */*");
                    var altResp2 = await _client.ExecuteAsync(alt2);
                    if (altResp2.IsSuccessful && !string.IsNullOrEmpty(altResp2.Content) && altResp2.Content.TrimStart().StartsWith("{"))
                    {
                        try
                        {
                            var t2 = JsonConvert.DeserializeObject<dynamic>(altResp2.Content);
                            if (t2 != null && t2.Name != null)
                            {
                                return new UserInfo
                                {
                                    UserId = (long)(t2.UserId ?? 0L),
                                    Username = (string)(t2.Name ?? ""),
                                    DisplayName = (string)(t2.DisplayName ?? (t2.Name ?? ""))
                                };
                            }
                        }
                        catch (Exception ex3)
                        {
                            _logger.LogWarning(ex3, "Failed to parse settings json content");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user info");
            }

            return null;
        }

        public async Task LogoutAsync(string cookie)
        {
            try
            {
                var request = new RestRequest("/v2/logout", Method.Post);
                request.AddHeader("Cookie", $".ROBLOSECURITY={cookie}");
                request.AddHeader("X-CSRF-TOKEN", await GetCsrfTokenAsync(cookie));

                await _authClient.ExecuteAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to logout");
            }
        }

        public async Task<bool> LaunchRobloxAsync(Account account, long placeId, string? jobId = null, string? launchData = null)
        {
            try
            {
                // Check if multi-Roblox is enabled and handle mutex
                var settingsService = App.GetService<ISettingsService>();
                var multiRobloxEnabled = await settingsService.GetMultiRobloxEnabledAsync();
                
                // Try to update mutex but don't block launch if it fails (user might already have Multi-Roblox handled)
                UpdateMultiRoblox(multiRobloxEnabled);

                var authTicket = await GetAuthenticationTicketAsync(account.SecurityToken);
                if (string.IsNullOrEmpty(authTicket))
                {
                    _logger.LogWarning("LaunchRoblox: failed to get auth ticket for account {Username}", account.Username);
                    return false;
                }

                if (string.IsNullOrEmpty(account.BrowserTrackerId))
                {
                    lock (_sharedRandom)
                    {
                        account.BrowserTrackerId = _sharedRandom.Next(100000, 175000).ToString() + _sharedRandom.Next(100000, 900000).ToString();
                    }
                }

                var launcherUrl = jobId != null
                    ? $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGameJob&placeId={placeId}&gameId={jobId}&isPlayTogetherGame=false&browserTrackerId={account.BrowserTrackerId}"
                    : $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&placeId={placeId}&isPlayTogetherGame=false&browserTrackerId={account.BrowserTrackerId}";

                // Handle Private Servers if jobId looks like a link or access code
                if (!string.IsNullOrEmpty(jobId) && (jobId.Contains("privateServerLinkCode") || jobId.Length > 30))
                {
                    var accessCode = jobId;
                    if (jobId.Contains("privateServerLinkCode="))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(jobId, "privateServerLinkCode=([^&]+)");
                        if (match.Success) accessCode = match.Groups[1].Value;
                    }
                    launcherUrl = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&placeId={placeId}&accessCode={accessCode}&browserTrackerId={account.BrowserTrackerId}";
                }

                if (!string.IsNullOrEmpty(launchData))
                {
                    string rawLaunchData = launchData.Trim();
                    
                    // If it doesn't look like JSON, assume it's a private server code (ERLC style)
                    if (!rawLaunchData.StartsWith("{"))
                    {
                        rawLaunchData = $"{{\"psCode\":\"{rawLaunchData}\"}}";
                    }

                    try
                    {
                        // Ensure it's compact JSON as expected by Roblox
                        var parsedData = JToken.Parse(rawLaunchData);
                        rawLaunchData = parsedData.ToString(Formatting.None);
                        
                        // Log the launch data for debugging
                        _logger.LogInformation("Using launch data for account {Username}: {LaunchData}", account.Username, rawLaunchData);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse launchData as JSON for account {Username}. Using raw value.", account.Username);
                    }

                    launcherUrl += $"&launchData={Uri.EscapeDataString(rawLaunchData)}";
                }

                var robloxPath = FindRobloxPath();
                var launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (robloxPath != null)
                {
                    // Direct EXE Launching - Using RAM style arguments
                    _logger.LogInformation("Launching Roblox directly via EXE: {RobloxPath}", robloxPath);

                    // Arguments used by RAM for direct launch
                    var args = $"--app -t {authTicket} -j \"{launcherUrl}\" -b {account.BrowserTrackerId} --launchtime {launchTime} --robloxLocale en_us --gameLocale en_us --launchExp InApp";
                    
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = robloxPath,
                        Arguments = args,
                        UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(robloxPath)
                    };

                    System.Diagnostics.Process.Start(startInfo);
                }
                else
                {
                    // Fallback to Deep Link if EXE not found
                    var deepLink = $"roblox-player:1+launchmode:play+gameinfo:{authTicket}+launchtime:{launchTime}+placelauncherurl:{Uri.EscapeDataString(launcherUrl)}+browsertrackerid:{account.BrowserTrackerId}+robloxLocale:en_us+gameLocale:en_us+launchExp:InApp";

                    _logger.LogInformation("Roblox EXE not found, falling back to deep link: {DeepLink}", deepLink);

                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd",
                        Arguments = $"/c start \"\" \"{deepLink}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };

                    System.Diagnostics.Process.Start(startInfo);
                }

                account.LastUsed = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch Roblox for account {Username}", account.Username);
                return false;
            }
        }

        public async Task<List<Server>> GetServersAsync(long placeId, int limit = 100)
        {
            try
            {
                var request = new RestRequest($"/v1/games/{placeId}/servers/Public", Method.Get);
                request.AddParameter("limit", limit);

                var response = await _gamesClient.ExecuteAsync(request);

                if (response.IsSuccessful)
                {
                    var data = JsonConvert.DeserializeObject<dynamic>(response.Content ?? string.Empty);
                    var servers = new List<Server>();

                    if (data?.data != null)
                    {
                        foreach (var serverData in data.data)
                        {
                            servers.Add(new Server
                            {
                                Id = (string)serverData.id,
                                MaxPlayers = (int)(serverData.maxPlayers ?? 0),
                                Playing = (int)(serverData.playing ?? 0),
                                PlayerCount = (int)(serverData.playing ?? 0),
                                Ping = (int)(serverData.ping ?? 0),
                                Fps = (float)(serverData.fps ?? 0),
                                Region = (string?)serverData.region,
                                RegionCode = (string?)serverData.regionCode,
                                Capacity = (int)(serverData.capacity ?? 0),
                                CurrentPlayers = JsonConvert.DeserializeObject<Player[]>(serverData.currentPlayers?.ToString() ?? "[]")
                            });
                        }
                    }

                    return servers;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get servers for place {PlaceId}", placeId);
            }

            return new List<Server>();
        }

        public async Task<Game?> GetGameInfoAsync(long placeId)
        {
            try
            {
                // Try primary endpoint (multiget) - optionally with a cookie if available
                var request = new RestRequest("/v1/games/multiget-place-details", Method.Get);
                request.AddParameter("placeIds", placeId);

                // Use a valid cookie if available, as some games require authentication to see details
                var accountService = App.GetService<IAccountService>();
                var accounts = await accountService.GetAccountsAsync();
                var validAccount = accounts.FirstOrDefault(a => a.IsValid);
                
                if (validAccount != null)
                {
                    request.AddHeader("Cookie", $".ROBLOSECURITY={validAccount.SecurityToken}");
                }

                var response = await _gamesClient.ExecuteAsync(request);

                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    JToken token = JToken.Parse(response.Content);
                    JArray array = token is JArray jArray ? jArray : (JArray?)token["data"] ?? new JArray();
                    var place = array.FirstOrDefault();
                    
                    if (place != null)
                    {
                        return new Game
                        {
                            Id = (long)(place["placeId"] ?? 0L),
                            Name = (string?)(place["name"] ?? ""),
                            Description = (string?)(place["description"] ?? "")
                        };
                    }
                }

                var altRequest = new RestRequest($"/places/api-get-details", Method.Get);
                altRequest.AddParameter("assetId", placeId);
                var altResponse = await _client.ExecuteAsync(altRequest);
                if (altResponse.IsSuccessful && !string.IsNullOrEmpty(altResponse.Content))
                {
                    var token = JToken.Parse(altResponse.Content);
                    var name = (string?)(token["Name"] ?? token["name"]);
                    var description = (string?)(token["Description"] ?? token["description"]);
                    var id = (long)(token["AssetId"] ?? token["placeId"] ?? placeId);
                    if (!string.IsNullOrEmpty(name))
                    {
                        return new Game
                        {
                            Id = id,
                            Name = name,
                            Description = description ?? ""
                        };
                    }
                }

                // Fallback 2: resolve universe and query by universe id
                // GET https://apis.roblox.com/universes/v1/places/{placeId}/universe  -> { universeId }
                // GET https://games.roblox.com/v1/games?universeIds={universeId}     -> data[0].name/description
                try
                {
                    var universesClient = new RestClient("https://apis.roblox.com");
                    var uniReq = new RestRequest($"/universes/v1/places/{placeId}/universe", Method.Get);
                    var uniResp = await universesClient.ExecuteAsync(uniReq);
                    if (uniResp.IsSuccessful && !string.IsNullOrEmpty(uniResp.Content))
                    {
                        var uniToken = JToken.Parse(uniResp.Content);
                        var universeId = (long?)(uniToken["universeId"] ?? uniToken["UniverseId"]);
                        if (universeId.HasValue && universeId.Value > 0)
                        {
                            var byUniReq = new RestRequest("/v1/games", Method.Get);
                            byUniReq.AddParameter("universeIds", universeId.Value);
                            var byUniResp = await _gamesClient.ExecuteAsync(byUniReq);
                            if (byUniResp.IsSuccessful && !string.IsNullOrEmpty(byUniResp.Content))
                            {
                                var byUniTok = JToken.Parse(byUniResp.Content);
                                var dataArr = byUniTok["data"] as JArray ?? new JArray();
                                var first = dataArr.FirstOrDefault();
                                if (first != null)
                                {
                                    var name2 = (string?)(first["name"] ?? "");
                                    var desc2 = (string?)(first["description"] ?? "");
                                    if (!string.IsNullOrEmpty(name2))
                                    {
                                        return new Game
                                        {
                                            Id = placeId,
                                            Name = name2,
                                            Description = desc2 ?? ""
                                        };
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex2)
                {
                    _logger.LogWarning(ex2, "UniverseId fallback failed for place {PlaceId}", placeId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get game info for place {PlaceId}", placeId);
            }

            return null;
        }

        public async Task<UserPresence?> GetUserPresenceAsync(long userId)
        {
            try
            {
                var presenceClient = new RestClient("https://presence.roblox.com");

                // Presence API requires POST with body { userIds: [...] } and often
                // returns richer data when a valid cookie is supplied.
                var request = new RestRequest("/v1/presence/users", Method.Post);
                request.AddHeader("Content-Type", "application/json");
                request.AddStringBody(JsonConvert.SerializeObject(new { userIds = new long[] { userId } }), DataFormat.Json);

                try
                {
                    var accountService = App.GetService<IAccountService>();
                    var accounts = await accountService.GetAccountsAsync();
                    var validCookie = accounts.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.SecurityToken))?.SecurityToken;
                    if (!string.IsNullOrEmpty(validCookie))
                    {
                        request.AddHeader("Cookie", $".ROBLOSECURITY={validCookie}");
                    }
                }
                catch (Exception ex2)
                {
                    _logger.LogDebug(ex2, "GetUserPresenceAsync: failed to attach cookie header");
                }

                var response = await presenceClient.ExecuteAsync(request);
                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    var token = JToken.Parse(response.Content);
                    var arr = token["userPresences"] as JArray;
                    var first = arr?.FirstOrDefault();
                    if (first != null)
                    {
                        return first.ToObject<UserPresence>();
                    }
                }
                else
                {
                    _logger.LogWarning("GetUserPresenceAsync: unsuccessful response. Status: {StatusCode}, Content: {Content}", response.StatusCode, response.Content ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user presence for user {UserId}", userId);
            }

            return null;
        }

        public async Task<Dictionary<long, UserPresence>> GetUsersPresenceAsync(IEnumerable<long> userIds)
        {
            var result = new Dictionary<long, UserPresence>();
            try
            {
                var presenceClient = new RestClient("https://presence.roblox.com");
                var request = new RestRequest("/v1/presence/users", Method.Post);
                request.AddHeader("Content-Type", "application/json");
                request.AddStringBody(JsonConvert.SerializeObject(new { userIds = userIds.ToArray() }), DataFormat.Json);

                try
                {
                    var accountService = App.GetService<IAccountService>();
                    var accounts = await accountService.GetAccountsAsync();
                    var validCookie = accounts.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.SecurityToken))?.SecurityToken;
                    if (!string.IsNullOrEmpty(validCookie))
                    {
                        request.AddHeader("Cookie", $".ROBLOSECURITY={validCookie}");
                    }
                }
                catch (Exception ex2)
                {
                    _logger.LogDebug(ex2, "GetUsersPresenceAsync: failed to attach cookie header");
                }

                var response = await presenceClient.ExecuteAsync(request);
                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    var token = JToken.Parse(response.Content);
                    var arr = token["userPresences"] as JArray ?? new JArray();
                    foreach (var item in arr)
                    {
                        var presence = item.ToObject<UserPresence>();
                        var idToken = item["userId"];
                        if (presence != null && idToken != null && long.TryParse(idToken.ToString(), out var uid))
                        {
                            result[uid] = presence;
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("GetUsersPresenceAsync: unsuccessful response. Status: {StatusCode}, Content: {Content}", response.StatusCode, response.Content ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get presences");
            }
            return result;
        }

        private string? FindRobloxPath()
        {
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var robloxVersionsPath = Path.Combine(localAppData, "Roblox", "Versions");

                if (!Directory.Exists(robloxVersionsPath)) return null;

                // Roblox installs versions in folders like version-xxxxxxxxxxxxxx
                // We need to find the one that contains RobloxPlayerBeta.exe
                var versionFolders = Directory.GetDirectories(robloxVersionsPath, "version-*");
                
                // Sort by last write time to get the most recent version
                var latestVersion = versionFolders
                    .Select(d => new DirectoryInfo(d))
                    .OrderByDescending(d => d.LastWriteTime)
                    .FirstOrDefault();

                if (latestVersion != null)
                {
                    var exePath = Path.Combine(latestVersion.FullName, "RobloxPlayerBeta.exe");
                    if (File.Exists(exePath))
                    {
                        return exePath;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to locate Roblox installation path");
            }

            return null;
        }

        public async Task<bool> JoinGameAsync(Account account, long placeId, string? jobId = null, string? launchData = null)
        {
            return await LaunchRobloxAsync(account, placeId, jobId, launchData);
        }

        public async Task<bool> JoinServerAsync(Account account, long placeId, string serverId)
        {
            return await LaunchRobloxAsync(account, placeId, serverId);
        }

        public async Task<string?> GetAuthenticationTicketAsync(string cookie)
        {
            try
            {
                var request = new RestRequest("/v1/authentication-ticket/", Method.Post);
                request.AddHeader("Cookie", $".ROBLOSECURITY={cookie}");
                request.AddHeader("X-CSRF-TOKEN", await GetCsrfTokenAsync(cookie));
                // Brookhaven Referer is standard in RAM to ensure game join success
                request.AddHeader("Referer", "https://www.roblox.com/games/4924922222/Brookhaven-RP");
                request.AddHeader("Content-Type", "application/json");
                request.AddStringBody("{}", DataFormat.Json);

                var response = await _authClient.ExecuteAsync(request);

                if (response.IsSuccessful)
                {
                    // The ticket is returned in the RBX-Authentication-Ticket response header
                    var ticketHeader = response.Headers?.FirstOrDefault(h =>
                        string.Equals(h.Name, "RBX-Authentication-Ticket", StringComparison.OrdinalIgnoreCase) || 
                        string.Equals(h.Name, "rbx-authentication-ticket", StringComparison.OrdinalIgnoreCase));

                    if (ticketHeader?.Value != null)
                    {
                        return ticketHeader.Value.ToString();
                    }

                    _logger.LogWarning("GetAuthenticationTicket: RBX-Authentication-Ticket header not found in response.");
                }
                else
                {
                    _logger.LogWarning("GetAuthenticationTicket: unsuccessful response. Status: {StatusCode}, Content: {Content}", response.StatusCode, response.Content ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get authentication ticket");
            }

            return null;
        }

        public async Task<bool> ValidateCookieAsync(string cookie)
        {
            var userInfo = await GetUserInfoAsync(cookie);
            return userInfo != null;
        }

        public async Task<bool> TestCsrfTokenAsync(string cookie)
        {
            try
            {
                var csrfToken = await GetCsrfTokenAsync(cookie);
                return !string.IsNullOrEmpty(csrfToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "CSRF token test failed");
                return false;
            }
        }

        public async Task RefreshCookieAsync(Account account)
        {
            var isValid = await ValidateCookieAsync(account.SecurityToken);
            account.IsValid = isValid;
        }

        private async Task<string> GetCsrfTokenAsync(string cookie)
        {
            try
            {
                var request = new RestRequest("/v2/login", Method.Post);

                if (!string.IsNullOrEmpty(cookie))
                {
                    request.AddHeader("Cookie", $".ROBLOSECURITY={cookie}");
                }

                request.AddHeader("Content-Type", "application/json");

                var response = await _authClient.ExecuteAsync(request);

                var headers = response.Headers;
                if (headers == null)
                {
                    return string.Empty;
                }

                var tokenHeader = headers.FirstOrDefault(h =>
                    string.Equals(h.Name, "x-csrf-token", StringComparison.OrdinalIgnoreCase));

                return tokenHeader?.Value?.ToString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get CSRF token");
            }

            return string.Empty;
        }

        private string ExtractCookieFromResponse(RestResponse response)
        {
            var setCookieHeader = response.Headers?
                .FirstOrDefault(h => string.Equals(h.Name, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                ?.Value?.ToString() ?? string.Empty;

            if (!string.IsNullOrEmpty(setCookieHeader))
            {
                var match = Regex.Match(setCookieHeader, @"\.ROBLOSECURITY=([^;]+)");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return string.Empty;
        }

    }
}
