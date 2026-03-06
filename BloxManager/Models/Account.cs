using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BloxManager.Models
{
    public enum AccountStatus
    {
        Unknown,
        Online,
        InGame,
        Offline,
        Expired
    }

    public partial class Account : AccountListItem
    {
        private string _alias = string.Empty;
        private string _description = string.Empty;
        private string _password = string.Empty;
        private bool _isSelected = false;
        private string _avatarUrl = string.Empty;
        private UserPresence? _presence;


        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        private string _username = string.Empty;
        [JsonProperty("username")]
        public string Username
        {
            get => _username;
            set { if (_username != value) { _username = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
        }

        [JsonProperty("userId")]
        public long UserId { get; set; }

        [JsonProperty("securityToken")]
        public string SecurityToken { get; set; } = string.Empty;

        [JsonProperty("alias")]
        public string Alias
        {
            get => _alias;
            set
            {
                if (value != null && value.Length <= 50)
                    _alias = value;
            }
        }

        [JsonProperty("description")]
        public string Description
        {
            get => _description;
            set
            {
                if (value != null && value.Length <= 5000 && _description != value)
                {
                    _description = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonProperty("password")]
        public string Password
        {
            get => _password;
            set
            {
                if (value != null && value.Length <= 5000 && _password != value)
                {
                    _password = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _group = "Default";
        [JsonProperty("group")]
        public string Group
        {
            get => _group;
            set 
            { 
                var val = value ?? "Default";
                if (_group != val) 
                { 
                    _group = val; 
                    OnPropertyChanged(); 
                } 
            }
        }

        [JsonProperty("isValid")]
        public bool IsValid { get; set; }

        private AccountStatus _status = AccountStatus.Unknown;

        [JsonProperty("status")]
        public AccountStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonProperty("lastChecked")]
        public DateTime LastChecked { get; set; } = DateTime.Now;

        [JsonProperty("lastUsed")]
        public DateTime LastUsed { get; set; } = DateTime.Now;

        [JsonProperty("lastAttemptedRefresh")]
        public DateTime LastAttemptedRefresh { get; set; } = DateTime.MinValue;

        [JsonProperty("fields")]
        public Dictionary<string, string> Fields { get; set; } = new();

        [JsonProperty("browserTrackerId")]
        public string BrowserTrackerId { get; set; } = string.Empty;

        [JsonProperty("isFavorite")]
        public bool IsFavorite { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonProperty("sortOrder")]
        public int SortOrder { get; set; }

        [JsonProperty("avatarUrl")]
        public string AvatarUrl
        {
            get => _avatarUrl;
            set
            {
                if (_avatarUrl != value)
                {
                    _avatarUrl = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }


        // Runtime properties (not serialized)
        [JsonIgnore]
        public DateTime PinUnlocked { get; set; } = DateTime.MinValue;

        [JsonIgnore]
        public DateTime TokenSet { get; set; } = DateTime.MinValue;

        [JsonIgnore]
        public DateTime LastAppLaunch { get; set; } = DateTime.MinValue;

        [JsonIgnore]
        public string? CsrfToken { get; set; }

        [JsonIgnore]
        public UserPresence? Presence
        {
            get => _presence;
            set
            {
                if (!ReferenceEquals(_presence, value))
                {
                    _presence = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsOnline));
                }
            }
        }

        [JsonIgnore]
        public bool IsOnline => Presence?.Type == "online" || Presence?.Type == "in-game";

        [JsonIgnore]
        public string DisplayName => string.IsNullOrEmpty(Alias) ? Username : Alias;

        [JsonIgnore]
        public DateTime? InGameSince { get; set; }

        [JsonIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isRunning;
        [JsonIgnore]
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    OnPropertyChanged();
                }
            }
        }

        public int CompareTo(Account? other)
        {
            if (other == null) return 1;
            return Group.CompareTo(other.Group);
        }

        public Account() { }

        public Account(string username, string password)
        {
            Username = username;
            Password = password;
            Alias = username;
        }

        public Account(string securityToken)
        {
            SecurityToken = securityToken;
        }
    }

    public class UserPresence
    {
        [JsonProperty("userPresenceType")]
        public int UserPresenceType { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("placeId")]
        public long? PlaceId { get; set; }

        [JsonProperty("rootPlaceId")]
        public long? RootPlaceId { get; set; }

        [JsonProperty("gameId")]
        public string? GameId { get; set; }

        [JsonProperty("lastLocation")]
        public string? LastLocation { get; set; }

        [JsonProperty("lastOnline")]
        public string? LastOnline { get; set; }
    }
}
