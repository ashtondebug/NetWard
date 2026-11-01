using System;
using System.Diagnostics;
using System.IO;

namespace NetWard.Modern;

public sealed class ManagedBrowser
{
    public static string Detect()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe")
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static void Launch(string executablePath, int proxyPort)
    {
        if (executablePath == null)
        {
            throw new InvalidOperationException("未找到 Edge 或 Chrome，无法启动受管浏览。");
        }

        var profile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebBlocker",
            "modern-managed",
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(profile);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--proxy-server=http://127.0.0.1:" + proxyPort);
        startInfo.ArgumentList.Add("--user-data-dir=" + profile);
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--no-default-browser-check");
        startInfo.ArgumentList.Add("--disable-extensions");
        startInfo.ArgumentList.Add("--app=https://www.bing.com/");
        Process.Start(startInfo);
    }
}