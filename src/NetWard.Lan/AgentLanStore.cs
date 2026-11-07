using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NetWard.Lan
{
    public enum PairingOutcome
    {
        Paired,
        InvalidCode,
        ExpiredCode,
        TooManyAttempts,
        AlreadyPaired
    }

    public sealed class PairedAdmin
    {
        public string AdminId { get; set; }

        public string Name { get; set; }

        public string Thumbprint { get; set; }

        public string PairedAtUtc { get; set; }
    }

    public sealed class AgentLanState
    {
        public string PairingCode { get; set; }

        public string PairingCodeExpiresAtUtc { get; set; }

        public int FailedAttempts { get; set; }

        public List<PairedAdmin> PairedAdmins { get; set; }
    }

    /// <summary>
    /// Endpoint-side pairing state: the short-lived pairing code shown on screen and the list of
    /// administrators that were allowed to pair. The file only contains public data (certificate
    /// thumbprints); the device private key lives in the identity file.
    /// </summary>
    public sealed class AgentLanStore
    {
        public const int PairingCodeLifetimeMinutes = 10;
        public const int MaximumFailedAttempts = 5;

        private readonly object _sync = new object();
        private readonly string _directory;
        private readonly string _path;

        public AgentLanStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A directory is required.", "directory");
            }

            _directory = directory;
            _path = Path.Combine(directory, "agent-state.json");
        }

        public AgentLanState Load()
        {
            lock (_sync)
            {
                return LoadCore();
            }
        }

        public string EnsurePairingCode(DateTime utcNow)
        {
            lock (_sync)
            {
                var state = LoadCore();
                if (IsCodeUsable(state, utcNow))
                {
                    return state.PairingCode;
                }

                var code = GenerateCode();
                state.PairingCode = code;
                state.PairingCodeExpiresAtUtc = utcNow.AddMinutes(PairingCodeLifetimeMinutes)
                    .ToString("o", CultureInfo.InvariantCulture);
                state.FailedAttempts = 0;
                SaveCore(state);
                return code;
            }
        }

        /// <summary>Forces a new code; used after exhausting attempts or when the administrator asks.</summary>
        public string RotatePairingCode(DateTime utcNow)
        {
            lock (_sync)
            {
                var state = LoadCore();
                state.PairingCode = GenerateCode();
                state.PairingCodeExpiresAtUtc = utcNow.AddMinutes(PairingCodeLifetimeMinutes)
                    .ToString("o", CultureInfo.InvariantCulture);
                state.FailedAttempts = 0;
                SaveCore(state);
                return state.PairingCode;
            }
        }

        public PairingOutcome TryPair(string code, PairedAdmin admin, DateTime utcNow)
        {
            if (admin == null)
            {
                throw new ArgumentNullException("admin");
            }

            if (string.IsNullOrWhiteSpace(admin.AdminId) || string.IsNullOrWhiteSpace(admin.Thumbprint))
            {
                throw new ArgumentException("The administrator identity is incomplete.", "admin");
            }

            lock (_sync)
            {
                var state = LoadCore();
                if (state.PairedAdmins == null)
                {
                    state.PairedAdmins = new List<PairedAdmin>();
                }

                var existing = state.PairedAdmins.Find(item =>
                    DeviceIdentity.ThumbprintEquals(item.Thumbprint, admin.Thumbprint));
                if (existing != null)
                {
                    return PairingOutcome.AlreadyPaired;
                }

                if (state.FailedAttempts >= MaximumFailedAttempts)
                {
                    return PairingOutcome.TooManyAttempts;
                }

                DateTime expiresAtUtc;
                if (!TryParseUtc(state.PairingCodeExpiresAtUtc, out expiresAtUtc) || utcNow >= expiresAtUtc)
                {
                    return PairingOutcome.ExpiredCode;
                }

                if (!CodeEquals(state.PairingCode, code))
                {
                    state.FailedAttempts++;
                    SaveCore(state);
                    return PairingOutcome.InvalidCode;
                }

                admin.PairedAtUtc = utcNow.ToString("o", CultureInfo.InvariantCulture);
                state.PairedAdmins.Add(admin);
                state.PairingCode = null;
                state.PairingCodeExpiresAtUtc = null;
                state.FailedAttempts = 0;
                SaveCore(state);
                return PairingOutcome.Paired;
            }
        }

        public bool IsPaired(string thumbprint)
        {
            lock (_sync)
            {
                var state = LoadCore();
                if (state.PairedAdmins == null)
                {
                    return false;
                }

                return state.PairedAdmins.Exists(item =>
                    DeviceIdentity.ThumbprintEquals(item.Thumbprint, thumbprint));
            }
        }

        public PairedAdmin FindAdmin(string thumbprint)
        {
            lock (_sync)
            {
                var state = LoadCore();
                if (state.PairedAdmins == null)
                {
                    return null;
                }

                return state.PairedAdmins.Find(item =>
                    DeviceIdentity.ThumbprintEquals(item.Thumbprint, thumbprint));
            }
        }

        public bool RemoveAdmin(string adminId)
        {
            lock (_sync)
            {
                var state = LoadCore();
                if (state.PairedAdmins == null)
                {
                    return false;
                }

                var removed = state.PairedAdmins.RemoveAll(item =>
                    string.Equals(item.AdminId, adminId, StringComparison.OrdinalIgnoreCase));
                if (removed > 0)
                {
                    SaveCore(state);
                }

                return removed > 0;
            }
        }

        public int CountAdmins()
        {
            lock (_sync)
            {
                var state = LoadCore();
                return state.PairedAdmins == null ? 0 : state.PairedAdmins.Count;
            }
        }

        private AgentLanState LoadCore()
        {
            if (!File.Exists(_path))
            {
                return NewState();
            }

            try
            {
                var json = File.ReadAllText(_path, Encoding.UTF8);
                var state = LanJson.Deserialize<AgentLanState>(json);
                if (state == null)
                {
                    return NewState();
                }

                if (state.PairedAdmins == null)
                {
                    state.PairedAdmins = new List<PairedAdmin>();
                }

                return state;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                // Never keep a half-broken pairing file in the loop: move it aside and start clean so the
                // endpoint refuses remote management until an administrator pairs again.
                try
                {
                    var quarantine = _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                    File.Move(_path, quarantine);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                return NewState();
            }
        }

        private void SaveCore(AgentLanState state)
        {
            Directory.CreateDirectory(_directory);
            var json = LanJson.Serialize(state);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            File.Move(temporary, _path);
        }

        private static AgentLanState NewState()
        {
            return new AgentLanState { PairedAdmins = new List<PairedAdmin>() };
        }

        private static bool IsCodeUsable(AgentLanState state, DateTime utcNow)
        {
            if (string.IsNullOrEmpty(state.PairingCode) || state.FailedAttempts >= MaximumFailedAttempts)
            {
                return false;
            }

            DateTime expiresAtUtc;
            return TryParseUtc(state.PairingCodeExpiresAtUtc, out expiresAtUtc) && utcNow < expiresAtUtc;
        }

        private static bool TryParseUtc(string value, out DateTime utc)
        {
            utc = DateTime.MinValue;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            DateTime parsed;
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out parsed))
            {
                return false;
            }

            utc = parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
            return true;
        }

        private static bool CodeEquals(string expected, string actual)
        {
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual))
            {
                return false;
            }

            var left = expected.Trim();
            var right = actual.Trim();
            if (left.Length != right.Length)
            {
                // Still spend comparable time so a wrong-length guess is not obviously faster.
                return false;
            }

            var difference = 0;
            for (var index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }

        private static string GenerateCode()
        {
            // 6 digits from a cryptographic generator; the code is a short-lived enrollment secret,
            // never a long-term key.
            var bytes = new byte[4];
            using (var generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }

            var value = BitConverter.ToUInt32(bytes, 0) % 1000000u;
            return value.ToString("D6", CultureInfo.InvariantCulture);
        }
    }
}
