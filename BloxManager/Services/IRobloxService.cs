using BloxManager.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BloxManager.Services
{
    public interface IRobloxService
    {
        Task<LoginResult?> LoginAsync(string username, string password);
        Task<UserInfo?> GetUserInfoAsync(string cookie);
        Task LogoutAsync(string cookie);
        Task<bool> LaunchRobloxAsync(Account account, long placeId, string? jobId = null, string? launchData = null);
        Task<List<Server>> GetServersAsync(long placeId, int limit = 100);
        Task<Game?> GetGameInfoAsync(long placeId);
        Task<UserPresence?> GetUserPresenceAsync(long userId);
        Task<Dictionary<long, UserPresence>> GetUsersPresenceAsync(IEnumerable<long> userIds);
        Task<bool> JoinGameAsync(Account account, long placeId, string? jobId = null, string? launchData = null);
        Task<bool> JoinServerAsync(Account account, long placeId, string serverId);
        Task<string?> GetAuthenticationTicketAsync(string cookie);
        Task<bool> ValidateCookieAsync(string cookie);
        Task<bool> TestCsrfTokenAsync(string cookie);
        Task RefreshCookieAsync(Account account);
        Task<bool> IsGamePassOwnedAsync(long userId, long gamePassId);
        Task<string?> GetUserAvatarUrlAsync(long userId);
        bool UpdateMultiRoblox(bool enabled);
    }

    public class LoginResult
    {
        public string SecurityToken { get; set; } = string.Empty;
        public long UserId { get; set; }
        public string Username { get; set; } = string.Empty;
    }

    public class UserInfo
    {
        public long UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }
}
