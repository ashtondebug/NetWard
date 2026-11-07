using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NetWard.Modern.Administrator;

public sealed class AdminEngineClient
{
    private readonly string _bridgePath;
    private readonly string _databasePath;

    public AdminEngineClient(string databasePath)
    {
        _databasePath = databasePath;
        _bridgePath = Path.Combine(AppContext.BaseDirectory, "engine", "NetWard.EngineBridge.exe");
    }

    public string DatabasePath => _databasePath;

    public async Task<AdminPolicy> GetPolicyAsync()
    {
        var policy = new AdminPolicy();
        var status = await RunAsync("status-json", "--database", _databasePath).ConfigureAwait(false);
        using (var document = JsonDocument.Parse(status))
        {
            var root = document.RootElement;
            policy.HasPolicy = root.GetProperty("hasPolicy").GetBoolean();
            policy.Version = root.GetProperty("version").GetInt64();
            policy.Mode = root.GetProperty("mode").GetString() == "BlockList" ? "BlockList" : "AllowList";
            policy.Events = root.GetProperty("events").GetInt64();
        }

        if (!policy.HasPolicy)
        {
            return policy;
        }

        var json = await RunAsync("policy-json", "--database", _databasePath).ConfigureAwait(false);
        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            foreach (var item in root.GetProperty("rules").EnumerateArray())
            {
                policy.Rules.Add(new AdminRule
                {
                    Domain = item.GetProperty("domain").GetString(),
                    IncludeSubdomains = item.GetProperty("includeSubdomains").GetBoolean(),
                    Enabled = item.GetProperty("enabled").GetBoolean()
                });
            }

            foreach (var item in root.GetProperty("timedCategories").EnumerateArray())
            {
                policy.TimedCategories.Add(new AdminTimedCategory
                {
                    Name = item.GetProperty("name").GetString(),
                    LimitMinutes = item.GetProperty("limitMinutes").GetInt32()
                });
            }

            // Timed-access rules and blocked-content rules must be loaded, otherwise the next save would
            // rewrite the policy without them and silently delete rules that other screens created.
            if (root.TryGetProperty("timedRules", out var timedRules))
            {
                foreach (var item in timedRules.EnumerateArray())
                {
                    policy.TimedRules.Add(new AdminTimedRule
                    {
                        Domain = item.GetProperty("domain").GetString(),
                        IncludeSubdomains = item.GetProperty("includeSubdomains").GetBoolean(),
                        Category = item.GetProperty("category").GetString(),
                        Enabled = item.GetProperty("enabled").GetBoolean()
                    });
                }
            }

            if (root.TryGetProperty("categoryRules", out var categoryRules))
            {
                foreach (var item in categoryRules.EnumerateArray())
                {
                    policy.CategoryRules.Add(new AdminCategoryRule
                    {
                        Domain = item.GetProperty("domain").GetString(),
                        IncludeSubdomains = item.GetProperty("includeSubdomains").GetBoolean(),
                        Category = item.GetProperty("category").GetString(),
                        Enabled = item.GetProperty("enabled").GetBoolean()
                    });
                }
            }

            foreach (var item in root.GetProperty("categories").EnumerateArray())
            {
                policy.Categories.Add(new AdminCategory
                {
                    Name = item.GetProperty("name").GetString(),
                    SourceName = item.GetProperty("source").GetString(),
                    Enabled = item.GetProperty("enabled").GetBoolean(),
                    RuleCount = item.GetProperty("rules").GetInt32()
                });
            }
        }

        return policy;
    }

    public async Task<long> SavePolicyAsync(AdminPolicy policy)
    {
        var temp = Path.Combine(Path.GetTempPath(), "netward-policy-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(temp, BuildPolicyJson(policy), Encoding.UTF8);
        try
        {
            var result = await RunAsync("save-policy", "--database", _databasePath, "--input", temp).ConfigureAwait(false);
            using (var document = JsonDocument.Parse(result))
            {
                return document.RootElement.GetProperty("version").GetInt64();
            }
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Serializes the complete policy, including timed-access rules and blocked-content rules, into the
    /// document the engine bridge accepts. The same document is used for a local save and for pushing the
    /// policy to a paired endpoint.
    /// </summary>
    public static string BuildPolicyJson(AdminPolicy policy)
    {
        var payload = new
        {
            Mode = policy.Mode,
            Rules = new List<object>(),
            TimedCategories = new List<object>(),
            TimedRules = new List<object>(),
            Categories = new List<object>(),
            CategoryRules = new List<object>()
        };
        foreach (var rule in policy.Rules)
        {
            payload.Rules.Add(new { Domain = rule.Domain, IncludeSubdomains = rule.IncludeSubdomains, Enabled = rule.Enabled });
        }

        foreach (var category in policy.TimedCategories)
        {
            payload.TimedCategories.Add(new { Name = category.Name, LimitMinutes = category.LimitMinutes });
        }

        foreach (var rule in policy.TimedRules)
        {
            payload.TimedRules.Add(new { Domain = rule.Domain, IncludeSubdomains = rule.IncludeSubdomains, Category = rule.Category, Enabled = rule.Enabled });
        }

        foreach (var category in policy.Categories)
        {
            payload.Categories.Add(new { Name = category.Name, SourceName = category.SourceName, Enabled = category.Enabled });
        }

        foreach (var rule in policy.CategoryRules)
        {
            payload.CategoryRules.Add(new { Domain = rule.Domain, IncludeSubdomains = rule.IncludeSubdomains, Category = rule.Category, Enabled = rule.Enabled });
        }

        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Stores events pulled from a paired endpoint; the import is idempotent per event id.</summary>
    public async Task<long> ImportRemoteEventsAsync(string deviceId, string eventsJson)
    {
        var temp = Path.Combine(Path.GetTempPath(), "netward-events-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(temp, eventsJson, Encoding.UTF8);
        try
        {
            var result = await RunAsync("import-remote-events", "--database", _databasePath,
                "--device", deviceId, "--input", temp).ConfigureAwait(false);
            using (var document = JsonDocument.Parse(result))
            {
                return document.RootElement.GetProperty("imported").GetInt64();
            }
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
            }
        }
    }

    public async Task<List<AdminEventRow>> GetRemoteEventsAsync(string deviceId, int limit)
    {
        var json = await RunAsync("remote-events-json", "--database", _databasePath,
            "--device", deviceId, "--limit", limit.ToString()).ConfigureAwait(false);
        return ParseEventRows(json);
    }

    public async Task<List<AdminEventRow>> GetEventsAsync(bool onlyBlocked, string search)
    {
        var arguments = new List<string> { "events-json", "--database", _databasePath, "--limit", "200" };
        if (onlyBlocked)
        {
            arguments.Add("--only-blocked");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            arguments.Add("--search");
            arguments.Add(search);
        }

        var json = await RunAsync(arguments.ToArray()).ConfigureAwait(false);
        return ParseEventRows(json);
    }

    private static List<AdminEventRow> ParseEventRows(string json)
    {
        var rows = new List<AdminEventRow>();
        using (var document = JsonDocument.Parse(json))
        {
            foreach (var item in document.RootElement.GetProperty("events").EnumerateArray())
            {
                var result = item.GetProperty("result").GetInt32();
                var brush = result switch
                {
                    4 => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 196, 43, 28)),
                    1 or 2 or 3 => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 157, 93, 0)),
                    _ => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 15, 123, 15))
                };
                var occurred = DateTimeOffset.Parse(
                    item.GetProperty("occurredAtUtc").GetString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind);
                rows.Add(new AdminEventRow
                {
                    Time = occurred.ToLocalTime().ToString("MM-dd HH:mm:ss"),
                    Domain = item.GetProperty("domain").GetString(),
                    Method = item.GetProperty("method").GetString(),
                    ResultText = item.GetProperty("resultText").GetString(),
                    ResultBrush = brush
                });
            }
        }

        return rows;
    }

    public async Task<List<AdminHistoryRow>> GetHistoryAsync()
    {
        var json = await RunAsync("history-json", "--database", _databasePath).ConfigureAwait(false);
        var rows = new List<AdminHistoryRow>();
        using (var document = JsonDocument.Parse(json))
        {
            foreach (var item in document.RootElement.GetProperty("history").EnumerateArray())
            {
                var updated = DateTimeOffset.Parse(
                    item.GetProperty("updatedAtUtc").GetString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind);
                rows.Add(new AdminHistoryRow
                {
                    Version = item.GetProperty("version").GetInt64(),
                    Mode = item.GetProperty("mode").GetString(),
                    RuleCount = item.GetProperty("ruleCount").GetInt32(),
                    UpdatedAt = updated.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                });
            }
        }

        return rows;
    }

    public async Task<long> RestoreAsync(long version)
    {
        var result = await RunAsync("restore-version", "--database", _databasePath, "--version", version.ToString()).ConfigureAwait(false);
        using (var document = JsonDocument.Parse(result))
        {
            return document.RootElement.GetProperty("version").GetInt64();
        }
    }

    public async Task<CategoryParseResult> ParseCategoriesAsync(string filePath)
    {
        var json = await RunAsync("parse-categories", "--input", filePath).ConfigureAwait(false);
        var parsed = new CategoryParseResult();
        using (var document = JsonDocument.Parse(json))
        {
            foreach (var item in document.RootElement.GetProperty("Categories").EnumerateArray())
            {
                parsed.Categories.Add(new AdminCategory
                {
                    Name = item.GetProperty("Name").GetString(),
                    SourceName = item.GetProperty("SourceName").GetString(),
                    Enabled = item.GetProperty("Enabled").GetBoolean()
                });
            }

            foreach (var item in document.RootElement.GetProperty("Rules").EnumerateArray())
            {
                parsed.Rules.Add(new AdminCategoryRule
                {
                    Domain = item.GetProperty("Domain").GetString(),
                    IncludeSubdomains = item.GetProperty("IncludeSubdomains").GetBoolean(),
                    Category = item.GetProperty("Category").GetString(),
                    Enabled = item.GetProperty("Enabled").GetBoolean()
                });
            }

            foreach (var item in document.RootElement.GetProperty("Errors").EnumerateArray())
            {
                parsed.Errors.Add(item.GetString());
            }

            parsed.Duplicates = document.RootElement.GetProperty("Duplicates").GetInt32();
        }

        return parsed;
    }

    private async Task<string> RunAsync(params string[] arguments)
    {
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