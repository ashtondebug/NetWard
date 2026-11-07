using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace WebBlocker.Shared.Tests
{
    /// <summary>
    /// Contract tests for the engine bridge commands the LAN link depends on. They run the real
    /// NetWard.EngineBridge.exe (built by the solution) against a temporary database, so a change that
    /// drops policy fields or breaks the event payload fails here instead of silently corrupting an
    /// endpoint during synchronization.
    /// </summary>
    internal static class BridgeContractTests
    {
        public static async Task PolicyRoundTripKeepsTimedAndCategoryRulesAsync()
        {
            var bridge = LocateBridge();
            using (var database = new TestDatabase())
            {
                Directory.CreateDirectory(database.DirectoryPath);
                var policyPath = Path.Combine(database.DirectoryPath, "policy.json");
                File.WriteAllText(policyPath, @"{
  ""Mode"": ""AllowList"",
  ""Rules"": [ { ""Domain"": ""allowed.example.test"", ""IncludeSubdomains"": true, ""Enabled"": true } ],
  ""TimedCategories"": [ { ""Name"": ""Study"", ""LimitMinutes"": 30 } ],
  ""TimedRules"": [ { ""Domain"": ""learning.example.test"", ""IncludeSubdomains"": true, ""Category"": ""Study"", ""Enabled"": true } ],
  ""Categories"": [ { ""Name"": ""adult"", ""SourceName"": ""samples"", ""Enabled"": true } ],
  ""CategoryRules"": [ { ""Domain"": ""adult.example.test"", ""IncludeSubdomains"": true, ""Category"": ""adult"", ""Enabled"": true } ]
}", new UTF8Encoding(false));

                var save = await RunAsync(bridge, "save-policy", "--database", database.Path, "--input", policyPath);
                AssertTrue(save.Contains("\"version\":1"), "save-policy must return the new version: " + save);

                var json = await RunAsync(bridge, "policy-json", "--database", database.Path);
                AssertTrue(json.Contains("\"timedRules\""), "policy-json must expose timed rules: " + json);
                AssertTrue(json.Contains("\"categoryRules\""), "policy-json must expose category rules: " + json);
                AssertTrue(json.Contains("learning.example.test"), "the timed rule must survive the round trip");
                AssertTrue(json.Contains("adult.example.test"), "the category rule must survive the round trip");
                AssertTrue(json.Contains("\"category\":\"Study\""), "the timed rule category must be preserved");
            }
        }

        public static async Task RemoteEventImportIsIdempotentAndPerDeviceAsync()
        {
            var bridge = LocateBridge();
            using (var database = new TestDatabase())
            {
                Directory.CreateDirectory(database.DirectoryPath);
                var payload = @"{
  ""events"": [
    { ""id"": ""11111111-1111-1111-1111-111111111111"", ""occurredAtUtc"": ""2026-11-06T08:00:00.0000000Z"", ""domain"": ""blocked.example.test"", ""method"": ""GET"", ""result"": 1, ""requestedUrl"": ""http://blocked.example.test/a"", ""port"": 80 },
    { ""id"": ""22222222-2222-2222-2222-222222222222"", ""occurredAtUtc"": ""2026-11-06T08:05:00.0000000Z"", ""domain"": ""allowed.example.test"", ""method"": ""CONNECT"", ""result"": 0, ""requestedUrl"": null, ""port"": 443 }
  ]
}";
                var eventsPath = Path.Combine(database.DirectoryPath, "events.json");
                File.WriteAllText(eventsPath, payload, new UTF8Encoding(false));

                var first = await RunAsync(bridge, "import-remote-events", "--database", database.Path,
                    "--device", "device-classroom", "--input", eventsPath);
                AssertTrue(first.Contains("\"imported\":2"), "the first import must store both events: " + first);

                var second = await RunAsync(bridge, "import-remote-events", "--database", database.Path,
                    "--device", "device-classroom", "--input", eventsPath);
                AssertTrue(second.Contains("\"imported\":0"), "a repeated import must be ignored: " + second);

                var listed = await RunAsync(bridge, "remote-events-json", "--database", database.Path,
                    "--device", "device-classroom", "--limit", "10");
                AssertTrue(listed.Contains("blocked.example.test"), "stored events must be queryable: " + listed);
                AssertTrue(listed.Contains("22222222-2222-2222-2222-222222222222"), "event ids must round-trip");

                var other = await RunAsync(bridge, "remote-events-json", "--database", database.Path,
                    "--device", "device-office", "--limit", "10");
                AssertTrue(!other.Contains("blocked.example.test"), "another device must not see these events");
            }
        }

        private static string LocateBridge()
        {
            var overridePath = Environment.GetEnvironmentVariable("NETWARD_BRIDGE");
            if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
            {
                return overridePath;
            }

            // ...\tests\WebBlocker.Shared.Tests\bin\Release\net48\ -> repository root
            var candidate = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "NetWard.EngineBridge", "bin", "Release", "net48", "NetWard.EngineBridge.exe"));
            if (!File.Exists(candidate))
            {
                throw new FileNotFoundException(
                    "The engine bridge was not built. Run 'dotnet build NetWard.sln -c Release' first, or set NETWARD_BRIDGE.",
                    candidate);
            }

            return candidate;
        }

        private static Task<string> RunAsync(string executable, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = QuoteArguments(arguments),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = Process.Start(startInfo))
            {
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit(30000);
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        "Bridge command failed (" + process.ExitCode + "): " + (error.Length > 0 ? error : output));
                }

                return Task.FromResult(output.Trim());
            }
        }

        private static string QuoteArguments(string[] arguments)
        {
            var builder = new StringBuilder();
            foreach (var argument in arguments)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append('"').Append(argument.Replace("\"", "\\\"")).Append('"');
            }

            return builder.ToString();
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
