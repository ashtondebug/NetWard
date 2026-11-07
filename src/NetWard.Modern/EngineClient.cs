using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace NetWard.Modern;

/// <summary>
/// Uses the net48 engine bridge as a child process so the Modern front-end can read the same policy and
/// audit database without re-targeting the SQLite stack or introducing a second database format.
/// </summary>
public sealed class EngineClient
{
    private readonly string _bridgePath;
    private readonly string _databasePath;

    public EngineClient(string databasePath)
    {
        _databasePath = databasePath;
        _bridgePath = Path.Combine(AppContext.BaseDirectory, "engine", "NetWard.EngineBridge.exe");
    }

    public string DatabasePath => _databasePath;

    public string BridgePath => _bridgePath;

    public bool IsAvailable => File.Exists(_bridgePath);

    /// <summary>Runs an arbitrary bridge command and returns its standard output.</summary>
    public Task<string> ExecuteAsync(params string[] arguments)
    {
        return RunAsync(arguments);
    }

    public async Task<PolicySummary> GetPolicyAsync()
    {
        var summary = new PolicySummary();
        var statusJson = await RunAsync("status-json", "--database", _databasePath).ConfigureAwait(false);
        using (var status = JsonDocument.Parse(statusJson))
        {
            var root = status.RootElement;
            summary.HasPolicy = root.GetProperty("hasPolicy").GetBoolean();
            summary.Version = root.GetProperty("version").GetInt64();
            summary.Mode = root.GetProperty("mode").GetString();
            summary.Rules = root.GetProperty("rules").GetInt32();
            summary.Categories = root.GetProperty("categories").GetInt32();
            summary.CategoryRules = root.GetProperty("categoryRules").GetInt32();
            summary.Events = root.GetProperty("events").GetInt64();
        }

        if (!summary.HasPolicy)
        {
            return summary;
        }

        var policyJson = await RunAsync("policy-json", "--database", _databasePath).ConfigureAwait(false);
        using (var policy = JsonDocument.Parse(policyJson))
        {
            var root = policy.RootElement;
            foreach (var rule in root.GetProperty("rules").EnumerateArray())
            {
                summary.RuleRows.Add(new RuleRow
                {
                    Domain = rule.GetProperty("domain").GetString(),
                    Scope = rule.GetProperty("includeSubdomains").GetBoolean() ? "含子域" : "精确域名",
                    State = rule.GetProperty("enabled").GetBoolean() ? "已启用" : "已停用",
                    StateBrush = rule.GetProperty("enabled").GetBoolean()
                        ? new SolidColorBrush(ColorHelper.FromArgb(255, 8, 122, 85))
                        : new SolidColorBrush(ColorHelper.FromArgb(255, 147, 91, 0))
                });
            }

            foreach (var category in root.GetProperty("categories").EnumerateArray())
            {
                var enabled = category.GetProperty("enabled").GetBoolean();
                summary.CategoryRows.Add(new CategoryRow
                {
                    Name = category.GetProperty("name").GetString(),
                    RuleCount = category.GetProperty("rules").GetInt32().ToString(),
                    State = enabled ? "防护已启用" : "防护未启用",
                    Source = category.GetProperty("source").GetString(),
                    StateBrush = enabled
                        ? new SolidColorBrush(ColorHelper.FromArgb(255, 8, 122, 85))
                        : new SolidColorBrush(ColorHelper.FromArgb(255, 147, 91, 0))
                });
            }
        }

        return summary;
    }

    public async Task<List<EventRow>> GetEventsAsync(int limit)
    {
        var rows = new List<EventRow>();
        var json = await RunAsync("events-json", "--database", _databasePath, "--limit", limit.ToString()).ConfigureAwait(false);
        using (var document = JsonDocument.Parse(json))
        {
            foreach (var item in document.RootElement.GetProperty("events").EnumerateArray())
            {
                var result = item.GetProperty("result").GetInt32();
                var resultText = item.GetProperty("resultText").GetString();
                var brush = result switch
                {
                    4 => new SolidColorBrush(ColorHelper.FromArgb(255, 190, 35, 45)),
                    2 => new SolidColorBrush(ColorHelper.FromArgb(255, 147, 91, 0)),
                    3 => new SolidColorBrush(ColorHelper.FromArgb(255, 147, 91, 0)),
                    0 => new SolidColorBrush(ColorHelper.FromArgb(255, 8, 122, 85)),
                    _ => new SolidColorBrush(ColorHelper.FromArgb(255, 88, 103, 125))
                };
                var occurred = DateTimeOffset.Parse(
                    item.GetProperty("occurredAtUtc").GetString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind);
                rows.Add(new EventRow
                {
                    Time = occurred.ToLocalTime().ToString("MM-dd HH:mm:ss"),
                    Domain = item.GetProperty("domain").GetString(),
                    Method = item.GetProperty("method").GetString(),
                    ResultText = resultText,
                    ResultBrush = brush,
                    IsBlocked = result != 0
                });
            }
        }

        return rows;
    }

    private async Task<string> RunAsync(params string[] arguments)
    {
        if (!IsAvailable)
        {
            throw new FileNotFoundException("未找到引擎组件：" + _bridgePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _bridgePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using (var process = Process.Start(startInfo))
        {
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                try
                {
                    process.Kill();
                }
                catch (Exception)
                {
                }

                throw new TimeoutException("引擎组件未在 15 秒内返回。");
            }

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);
            }

            return output;
        }
    }
}