using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace WebBlocker.Agent
{
    internal enum ManagedBrowserKind
    {
        Chromium = 0,
        InternetExplorer = 1
    }

    internal sealed class ManagedBrowserTarget
    {
        public ManagedBrowserTarget(string displayName, string executablePath, ManagedBrowserKind kind)
        {
            DisplayName = displayName;
            ExecutablePath = executablePath;
            Kind = kind;
        }

        public string DisplayName { get; private set; }

        public string ExecutablePath { get; private set; }

        public ManagedBrowserKind Kind { get; private set; }
    }

    /// <summary>
    /// Launches a visible browser that is forced through the local proxy for the duration of one session.
    /// Chromium-based browsers accept --proxy-server together with a dedicated --user-data-dir, so the
    /// user's normal profile is untouched and nothing outside the session changes.
    /// The Internet Explorer fallback has no equivalent switch, so it uses the per-user WinINET proxy
    /// values, which are captured before the session and restored when it ends.
    /// </summary>
    internal static class ManagedBrowserLauncher
    {
        private const string InternetSettingsKey =
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        public static ManagedBrowserTarget DetectPreferred()
        {
            foreach (var candidate in GetChromiumCandidates())
            {
                if (File.Exists(candidate.ExecutablePath))
                {
                    return candidate;
                }
            }

            var iePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Internet Explorer",
                "iexplore.exe");
            if (!File.Exists(iePath))
            {
                iePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Internet Explorer",
                    "iexplore.exe");
            }

            if (File.Exists(iePath))
            {
                return new ManagedBrowserTarget("Internet Explorer", iePath, ManagedBrowserKind.InternetExplorer);
            }

            return null;
        }

        public static Process Launch(ManagedBrowserTarget target, int proxyPort, string startUrl, string profileDirectory)
        {
            if (target == null)
            {
                throw new ArgumentNullException("target");
            }

            if (proxyPort < 1 || proxyPort > 65535)
            {
                throw new ArgumentOutOfRangeException("proxyPort");
            }

            Directory.CreateDirectory(profileDirectory);
            RemoveStaleProfileFiles(profileDirectory);

            var proxyServer = "http://127.0.0.1:" + proxyPort;
            var process = new Process();
            var startInfo = new ProcessStartInfo
            {
                FileName = target.ExecutablePath,
                UseShellExecute = false
            };
            var arguments = new StringBuilder();

            if (target.Kind == ManagedBrowserKind.Chromium)
            {
                arguments.Append("--proxy-server=").Append(proxyServer).Append(' ');
                arguments.Append("--user-data-dir=").Append(Quote(profileDirectory)).Append(' ');
                arguments.Append("--no-first-run --no-default-browser-check --disable-extensions ");
                arguments.Append("--disable-sync --disable-background-networking ");
                arguments.Append("--app=").Append(Quote(startUrl));
            }
            else
            {
                ApplyInternetExplorerProxy(proxyPort);
                arguments.Append("-nohome ").Append(Quote(startUrl));
            }

            startInfo.Arguments = arguments.ToString();
            process.StartInfo = startInfo;
            process.Start();
            return process;
        }

        public static void RestoreInternetExplorerProxy()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, true))
                {
                    if (key != null)
                    {
                        key.DeleteValue("ProxyServer", false);
                        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception)
            {
                // Restoring best-effort settings must never break application shutdown.
            }
        }

        private static void ApplyInternetExplorerProxy(int proxyPort)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, true)
                             ?? Registry.CurrentUser.CreateSubKey(InternetSettingsKey))
            {
                key.SetValue("ProxyServer", "127.0.0.1:" + proxyPort, RegistryValueKind.String);
                key.SetValue("ProxyOverride", "<local>", RegistryValueKind.String);
                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            }
        }

        private static IEnumerable<ManagedBrowserTarget> GetChromiumCandidates()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            yield return new ManagedBrowserTarget("Microsoft Edge",
                Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                ManagedBrowserKind.Chromium);
            yield return new ManagedBrowserTarget("Microsoft Edge",
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
                ManagedBrowserKind.Chromium);
            yield return new ManagedBrowserTarget("Google Chrome",
                Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                ManagedBrowserKind.Chromium);
            yield return new ManagedBrowserTarget("Google Chrome",
                Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                ManagedBrowserKind.Chromium);
            yield return new ManagedBrowserTarget("Google Chrome",
                Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
                ManagedBrowserKind.Chromium);
        }

        private static void RemoveStaleProfileFiles(string profileDirectory)
        {
            // A session that was killed can leave a singleton lock behind. It only guards one profile per
            // process, so removing it is safe and lets the next session start.
            foreach (var name in new[] { "SingletonLock", "SingletonCookie", "SingletonSocket" })
            {
                try
                {
                    var path = Path.Combine(profileDirectory, name);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }
    }
}