using System.Security.Cryptography;
using System.Text.Json;

namespace Server.Headless
{
    public sealed class WebAdminSettings
    {
        public const string DefaultUsername = "admin";
        public const string DefaultPassword = "admin123";

        private static readonly object SyncRoot = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public string Username { get; set; } = DefaultUsername;
        public string Salt { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string BindAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 8080;
        public bool AutoStartServer { get; set; } = true;
        public bool MustChangePassword { get; set; } = true;

        public static string DefaultPath => Path.Combine("Configs", "WebAdmin.json");

        public static WebAdminSettings LoadOrCreate(string path = null)
        {
            path ??= DefaultPath;
            lock (SyncRoot)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var loaded = JsonSerializer.Deserialize<WebAdminSettings>(File.ReadAllText(path));
                        if (loaded != null && !string.IsNullOrEmpty(loaded.PasswordHash) && !string.IsNullOrEmpty(loaded.Salt))
                            return loaded;
                    }
                    catch
                    {
                    }
                }

                var settings = new WebAdminSettings();
                settings.SetPassword(DefaultPassword, true);
                settings.Save(path);
                return settings;
            }
        }

        public void Save(string path = null)
        {
            path ??= DefaultPath;
            lock (SyncRoot)
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
            }
        }

        public bool Verify(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return false;
            if (!string.Equals(Username, username, StringComparison.Ordinal)) return false;
            if (string.IsNullOrEmpty(Salt) || string.IsNullOrEmpty(PasswordHash)) return false;

            var salt = Convert.FromBase64String(Salt);
            var expected = Convert.FromBase64String(PasswordHash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 120000, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        public void SetPassword(string password, bool requireChange)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 120000, HashAlgorithmName.SHA256, 32);
            Salt = Convert.ToBase64String(salt);
            PasswordHash = Convert.ToBase64String(hash);
            MustChangePassword = requireChange;
        }
    }

    public static class AdminRuntime
    {
        private static readonly object SyncRoot = new();
        private static DateTime? _startedAt;

        public static DateTime? StartedAt
        {
            get { lock (SyncRoot) return _startedAt; }
            set { lock (SyncRoot) _startedAt = value; }
        }

        public static long UptimeSeconds
        {
            get
            {
                var started = StartedAt;
                return started == null ? 0 : Math.Max(0, (long)(DateTime.UtcNow - started.Value).TotalSeconds);
            }
        }
    }

    public static class LoginGuard
    {
        private sealed class Attempt
        {
            public int Count;
            public DateTime LockedUntil;
        }

        private static readonly Dictionary<string, Attempt> Attempts = new();
        private static readonly object SyncRoot = new();

        public static bool IsBlocked(string key, out int seconds)
        {
            lock (SyncRoot)
            {
                if (Attempts.TryGetValue(key, out var attempt) && attempt.LockedUntil > DateTime.UtcNow)
                {
                    seconds = (int)Math.Ceiling((attempt.LockedUntil - DateTime.UtcNow).TotalSeconds);
                    return true;
                }

                seconds = 0;
                return false;
            }
        }

        public static void Fail(string key)
        {
            lock (SyncRoot)
            {
                if (!Attempts.TryGetValue(key, out var attempt))
                {
                    attempt = new Attempt();
                    Attempts[key] = attempt;
                }

                attempt.Count++;
                if (attempt.Count >= 5)
                {
                    attempt.Count = 0;
                    attempt.LockedUntil = DateTime.UtcNow.AddMinutes(5);
                }
            }
        }

        public static void Reset(string key)
        {
            lock (SyncRoot) Attempts.Remove(key);
        }
    }
}
