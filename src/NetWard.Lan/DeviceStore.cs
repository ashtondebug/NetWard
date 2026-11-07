using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NetWard.Lan
{
    /// <summary>An endpoint that this administrator paired with. Contains no secret material.</summary>
    public sealed class PairedDevice
    {
        public string DeviceId { get; set; }

        public string Name { get; set; }

        public string Address { get; set; }

        public int Port { get; set; }

        /// <summary>Pinned certificate thumbprint captured during pairing; every later session must match.</summary>
        public string Thumbprint { get; set; }

        /// <summary>Short fingerprint shown to the operator so both screens can be compared.</summary>
        public string Fingerprint { get; set; }

        public string PairedAtUtc { get; set; }

        public string LastSeenUtc { get; set; }

        public long LastPolicyVersion { get; set; }

        /// <summary>Newest endpoint event timestamp already imported into the administrator database.</summary>
        public string LastEventUtc { get; set; }
    }

    /// <summary>
    /// Administrator-side list of paired endpoints, stored as JSON in the local application data
    /// directory. Atomic replace keeps a crash from truncating the file.
    /// </summary>
    public sealed class DeviceStore
    {
        private readonly object _sync = new object();
        private readonly string _directory;
        private readonly string _path;

        public DeviceStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A directory is required.", "directory");
            }

            _directory = directory;
            _path = Path.Combine(directory, "devices.json");
        }

        public string FilePath
        {
            get { return _path; }
        }

        public IList<PairedDevice> Load()
        {
            lock (_sync)
            {
                if (!File.Exists(_path))
                {
                    return new List<PairedDevice>();
                }

                try
                {
                    var json = File.ReadAllText(_path, Encoding.UTF8);
                    var document = LanJson.Deserialize<DeviceDocument>(json);
                    return document == null || document.Devices == null
                        ? new List<PairedDevice>()
                        : document.Devices;
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    throw new LanIdentityException("设备列表无法读取：" + _path, exception);
                }
            }
        }

        public PairedDevice Find(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return null;
            }

            var devices = Load();
            foreach (var device in devices)
            {
                if (string.Equals(device.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
            }

            return null;
        }

        public void AddOrReplace(PairedDevice device)
        {
            if (device == null)
            {
                throw new ArgumentNullException("device");
            }

            if (string.IsNullOrWhiteSpace(device.DeviceId) || string.IsNullOrWhiteSpace(device.Thumbprint))
            {
                throw new ArgumentException("The device record is incomplete.", "device");
            }

            lock (_sync)
            {
                var devices = new List<PairedDevice>(Load());
                devices.RemoveAll(item => string.Equals(item.DeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase));
                devices.Add(device);
                Save(devices);
            }
        }

        public bool Remove(string deviceId)
        {
            lock (_sync)
            {
                var devices = new List<PairedDevice>(Load());
                var removed = devices.RemoveAll(item =>
                    string.Equals(item.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
                if (removed > 0)
                {
                    Save(devices);
                }

                return removed > 0;
            }
        }

        private void Save(IList<PairedDevice> devices)
        {
            Directory.CreateDirectory(_directory);
            var json = LanJson.Serialize(new DeviceDocument { Devices = new List<PairedDevice>(devices) });
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            File.Move(temporary, _path);
        }

        public static string FormatUtc(DateTime value)
        {
            return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        public static DateTime ParseUtc(string value, DateTime fallback)
        {
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }

            DateTime parsed;
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out parsed))
            {
                return fallback;
            }

            return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
        }
    }

    internal sealed class DeviceDocument
    {
        public List<PairedDevice> Devices { get; set; }
    }
}
