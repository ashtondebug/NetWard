using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace NetWard.Modern;

/// <summary>
/// Applies the local NetWard proxy as the Windows (WinINET) system proxy while management is
/// enabled, and restores the previous settings when management stops or the app exits.
/// </summary>
public sealed class SystemProxyManager : IDisposable
{
    private const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const int InternetOptionRefresh = 37;
    private const int InternetOptionSettingsChanged = 39;

    private readonly string _backupPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WebBlocker",
        "system-proxy.backup.json");

    private bool _applied;

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    public void RecoverIfNeeded()
    {
        if (File.Exists(_backupPath))
        {
            Restore();
        }
    }

    public void Enable(int port)
    {
        if (_applied)
        {
            return;
        }

        var backup = ReadCurrent();
        Directory.CreateDirectory(Path.GetDirectoryName(_backupPath));
        File.WriteAllText(
            _backupPath,
            JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));

        using var key = Registry.CurrentUser.OpenSubKey(SettingsKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(SettingsKey);
        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", "127.0.0.1:" + port, RegistryValueKind.String);
        key.SetValue("ProxyOverride", AddLocalBypass(backup.ProxyOverride), RegistryValueKind.String);
        key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
        Refresh();
        _applied = true;
    }

    public void Disable()
    {
        if (File.Exists(_backupPath))
        {
            Restore();
        }
        else if (_applied)
        {
            _applied = false;
            Refresh();
        }
    }

    public void Dispose()
    {
        Disable();
    }

    private void Restore()
    {
        ProxyBackup backup;
        try
        {
            backup = JsonSerializer.Deserialize<ProxyBackup>(File.ReadAllText(_backupPath));
        }
        catch (Exception exception) when (exception is IOException || exception is JsonException)
        {
            backup = null;
        }

        if (backup != null)
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(SettingsKey);
            key.SetValue("ProxyEnable", backup.ProxyEnable, RegistryValueKind.DWord);
            SetOrDelete(key, "ProxyServer", backup.ProxyServer);
            SetOrDelete(key, "ProxyOverride", backup.ProxyOverride);
            SetOrDelete(key, "AutoConfigURL", backup.AutoConfigURL);
        }

        try
        {
            File.Delete(_backupPath);
        }
        catch (IOException)
        {
        }

        _applied = false;
        Refresh();
    }

    private static ProxyBackup ReadCurrent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
        return new ProxyBackup
        {
            ProxyEnable = key?.GetValue("ProxyEnable") is int value ? value : 0,
            ProxyServer = key?.GetValue("ProxyServer") as string,
            ProxyOverride = key?.GetValue("ProxyOverride") as string,
            AutoConfigURL = key?.GetValue("AutoConfigURL") as string
        };
    }

    private static void SetOrDelete(RegistryKey key, string name, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(name, value, RegistryValueKind.String);
        }
    }

    private static string AddLocalBypass(string existing)
    {
        var parts = (existing ?? string.Empty)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToList();
        if (!parts.Any(part => string.Equals(part, "<local>", StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add("<local>");
        }

        return string.Join(";", parts);
    }

    private static void Refresh()
    {
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }

    private sealed class ProxyBackup
    {
        public int ProxyEnable { get; set; }

        public string ProxyServer { get; set; }

        public string ProxyOverride { get; set; }

        public string AutoConfigURL { get; set; }
    }
}