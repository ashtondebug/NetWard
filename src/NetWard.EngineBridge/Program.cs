using System.Text.RegularExpressions;
using System.Collections.Generic;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using WebBlocker.Filtering;
using WebBlocker.Persistence;
using WebBlocker.Shared;

namespace WebBlocker.EngineBridge
{
    /// <summary>
    /// Small, dependency-free bridge that lets the WinUI 3 front-end use the net48 engine without
    /// re-targeting the persistence stack or adding a network listener of its own.
    /// Commands: --status-json, --policy-json, --events-json [--limit N], --run [--port N] [--parent-pid N].
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var options = Options.Parse(args);
                if (options.Command == "run")
                {
                    return Run(options);
                }

                var store = new AccessEventStore(options.DatabasePath);
                switch (options.Command)
                {
                    case "status-json":
                        WriteStatus(store);
                        return 0;
                    case "policy-json":
                        WritePolicy(store);
                        return 0;
                    case "events-json":
                        WriteEvents(store, options.Limit, options.OnlyBlocked, options.Search, options.SinceUtc, options.Ascending);
                        return 0;
                    case "import-remote-events":
                        WriteImportRemoteEvents(store, options.DeviceId, options.InputPath);
                        return 0;
                    case "remote-events-json":
                        WriteRemoteEvents(store, options.DeviceId, options.Limit);
                        return 0;
                    case "history-json":
                        WriteHistory(store);
                        return 0;
                    case "restore-version":
                        WriteRestore(store, options.Version);
                        return 0;
                    case "save-policy":
                        WriteSave(store, options.InputPath);
                        return 0;
                    case "parse-categories":
                        WriteParsedCategories(options.InputPath);
                        return 0;
                    default:
                        Console.Error.WriteLine("Unknown command.");
                        return 2;
                }
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 2;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static int Run(Options options)
        {
            var store = new AccessEventStore(options.DatabasePath);
            PolicySnapshot snapshot;
            if (!store.TryLoadPolicy(out snapshot))
            {
                Console.Error.WriteLine("No saved policy exists. Save a policy in the administrator first.");
                return 2;
            }

            HttpsInterception httpsInterceptor = null;
            try
            {
                var certificateDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WebBlocker");
                var candidate = new HttpsInterception(certificateDirectory);
                if (candidate.EnsureTrusted())
                {
                    httpsInterceptor = candidate;
                    File.WriteAllText(
                        Path.Combine(certificateDirectory, "https-interception.log"),
                        "trusted=true");
                }
                else
                {
                    File.WriteAllText(
                        Path.Combine(certificateDirectory, "https-interception.log"),
                        "trusted=false");
                    Console.Error.WriteLine(
                        "HTTPS block pages are unavailable: the local NetWard root certificate is not trusted.");
                }
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                Console.Error.WriteLine("HTTPS block pages are unavailable: " + exception.Message);
            }

            using (var shutdown = new ManualResetEventSlim())
            using (var proxy = new DomainFilteringProxy(
                snapshot.Policy,
                options.Port,
                eventStore: store,
                policyVersion: snapshot.Version,
                httpsInterceptor: httpsInterceptor))
            {
                proxy.ClientError += (sender, eventArgs) =>
                    Console.Error.WriteLine("Proxy error: " + eventArgs.Exception.Message);
                proxy.ValidateBlockPages();
                proxy.Start();

                Console.WriteLine("READY port=" + proxy.Port.ToString(CultureInfo.InvariantCulture));
                Console.Out.Flush();
                Console.WriteLine("Policy version " + snapshot.Version.ToString(CultureInfo.InvariantCulture) +
                    " (" + snapshot.Policy.Mode + ", " + snapshot.Policy.Rules.Count + " rules)");
                Console.Out.Flush();

                var cancelKeyPress = new ConsoleCancelEventHandler((sender, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    shutdown.Set();
                });
                Console.CancelKeyPress += cancelKeyPress;
                try
                {
                    if (options.ParentProcessId.HasValue)
                    {
                        WaitForParentExit(options.ParentProcessId.Value, shutdown);
                    }

                    if (!Console.IsInputRedirected)
                    {
                        WaitForStdinClose(shutdown);
                    }

                    shutdown.Wait();
                    proxy.StopAsync().GetAwaiter().GetResult();
                }
                finally
                {
                    Console.CancelKeyPress -= cancelKeyPress;
                }
            }

            return 0;
        }

        private static void WaitForParentExit(int parentProcessId, ManualResetEventSlim shutdown)
        {
            var watcher = new Thread(() =>
            {
                try
                {
                    var parent = Process.GetProcessById(parentProcessId);
                    parent.WaitForExit();
                }
                catch (ArgumentException)
                {
                    // The parent is already gone.
                }

                shutdown.Set();
            });
            watcher.IsBackground = true;
            watcher.Start();
        }

        private static void WaitForStdinClose(ManualResetEventSlim shutdown)
        {
            var watcher = new Thread(() =>
            {
                try
                {
                    while (Console.In.ReadLine() != null)
                    {
                    }
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }

                shutdown.Set();
            });
            watcher.IsBackground = true;
            watcher.Start();
        }

        private static void WriteStatus(AccessEventStore store)
        {
            var events = store.QueryAccessEvents(false, null, 1, 1);
            PolicySnapshot snapshot;
            var hasPolicy = store.TryLoadPolicy(out snapshot);
            Console.WriteLine(
                "{\"database\":\"" + Json.Escape(store.DatabasePath) + "\"," +
                "\"hasPolicy\":" + (hasPolicy ? "true" : "false") + "," +
                "\"version\":" + (hasPolicy ? snapshot.Version : 0) + "," +
                "\"mode\":\"" + (hasPolicy ? Json.Mode(snapshot.Policy.Mode) : "none") + "\"," +
                "\"rules\":" + (hasPolicy ? snapshot.Policy.Rules.Count : 0) + "," +
                "\"categories\":" + (hasPolicy ? snapshot.Policy.Categories.Count : 0) + "," +
                "\"categoryRules\":" + (hasPolicy ? snapshot.Policy.CategoryRules.Count : 0) + "," +
                "\"events\":" + events.TotalCount + "}");
        }

        private static void WritePolicy(AccessEventStore store)
        {
            PolicySnapshot snapshot;
            if (!store.TryLoadPolicy(out snapshot))
            {
                Console.WriteLine("{\"hasPolicy\":false}");
                return;
            }

            var policy = snapshot.Policy;
            var builder = new StringBuilder();
            builder.Append("{\"hasPolicy\":true,\"version\":").Append(snapshot.Version)
                .Append(",\"mode\":\"").Append(Json.Mode(policy.Mode)).Append("\",\"rules\":[");
            for (var index = 0; index < policy.Rules.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var rule = policy.Rules[index];
                builder.Append("{\"domain\":\"").Append(Json.Escape(rule.Domain))
                    .Append("\",\"includeSubdomains\":").Append(rule.IncludeSubdomains ? "true" : "false")
                    .Append(",\"enabled\":").Append(rule.IsEnabled ? "true" : "false").Append('}');
            }

            builder.Append("],\"categories\":[");
            for (var index = 0; index < policy.Categories.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var category = policy.Categories[index];
                var ruleCount = 0;
                foreach (var rule in policy.CategoryRules)
                {
                    if (string.Equals(rule.Category, category.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        ruleCount++;
                    }
                }

                builder.Append("{\"name\":\"").Append(Json.Escape(category.Name))
                    .Append("\",\"enabled\":").Append(category.IsEnabled ? "true" : "false")
                    .Append(",\"rules\":").Append(ruleCount)
                    .Append(",\"source\":\"").Append(Json.Escape(category.SourceName ?? string.Empty))
                    .Append("\"}");
            }

            builder.Append("],\"timedCategories\":[");
            for (var index = 0; index < policy.TimedAccessCategories.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var category = policy.TimedAccessCategories[index];
                builder.Append("{\"name\":\"").Append(Json.Escape(category.Name))
                    .Append("\",\"limitMinutes\":").Append(category.TimeLimitMinutes).Append('}');
            }

            // Timed-access rules and blocked-content rules are part of the policy. Omitting them made a
            // save round-trip delete every rule of that kind, so both collections are exported in full.
            builder.Append("],\"timedRules\":[");
            for (var index = 0; index < policy.TimedAccessRules.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var rule = policy.TimedAccessRules[index];
                builder.Append("{\"domain\":\"").Append(Json.Escape(rule.Domain))
                    .Append("\",\"includeSubdomains\":").Append(rule.IncludeSubdomains ? "true" : "false")
                    .Append(",\"category\":\"").Append(Json.Escape(rule.Category))
                    .Append("\",\"enabled\":").Append(rule.IsEnabled ? "true" : "false").Append('}');
            }

            builder.Append("],\"categoryRules\":[");
            for (var index = 0; index < policy.CategoryRules.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var rule = policy.CategoryRules[index];
                builder.Append("{\"domain\":\"").Append(Json.Escape(rule.Domain))
                    .Append("\",\"includeSubdomains\":").Append(rule.IncludeSubdomains ? "true" : "false")
                    .Append(",\"category\":\"").Append(Json.Escape(rule.Category))
                    .Append("\",\"enabled\":").Append(rule.IsEnabled ? "true" : "false").Append('}');
            }

            builder.Append("]}");
            Console.WriteLine(builder.ToString());
        }

        private static void WriteEvents(AccessEventStore store, int limit, bool onlyBlocked, string search, DateTime? sinceUtc, bool ascending)
        {
            var page = store.QueryAccessEvents(onlyBlocked, search, 1, limit, sinceUtc, ascending);
            var builder = new StringBuilder();
            builder.Append("{\"total\":").Append(page.TotalCount).Append(",\"events\":[");
            for (var index = 0; index < page.Events.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var accessEvent = page.Events[index];
                // The stable event id is required for idempotent synchronization between devices.
                builder.Append("{\"id\":\"").Append(accessEvent.EventId.ToString("D"))
                    .Append("\",\"occurredAtUtc\":\"")
                    .Append(accessEvent.OccurredAtUtc.ToString("o", CultureInfo.InvariantCulture))
                    .Append("\",\"domain\":\"").Append(Json.Escape(accessEvent.Domain))
                    .Append("\",\"method\":\"").Append(Json.Escape(accessEvent.Method))
                    .Append("\",\"result\":").Append((int)accessEvent.Result)
                    .Append(",\"resultText\":\"").Append(Json.Result(accessEvent.Result))
                    .Append("\",\"requestedUrl\":\"").Append(Json.Escape(accessEvent.RequestedUrl ?? string.Empty))
                    .Append("\",\"port\":")
                    .Append(accessEvent.DestinationPort.HasValue
                        ? accessEvent.DestinationPort.Value.ToString(CultureInfo.InvariantCulture)
                        : "0")
                    .Append('}');
            }

            builder.Append("]}");
            Console.WriteLine(builder.ToString());
        }

        private static void WriteHistory(AccessEventStore store)
        {
            var entries = store.QueryPolicyHistory(1, 100);
            var builder = new StringBuilder();
            builder.Append("{\"history\":[");
            for (var index = 0; index < entries.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var entry = entries[index];
                builder.Append("{\"version\":").Append(entry.Version)
                    .Append(",\"mode\":\"").Append(Json.Mode(entry.Mode))
                    .Append("\",\"updatedAtUtc\":\"")
                    .Append(entry.UpdatedAtUtc.ToString("o", CultureInfo.InvariantCulture))
                    .Append("\",\"ruleCount\":").Append(entry.RuleCount).Append('}');
            }

            builder.Append("]}");
            Console.WriteLine(builder.ToString());
        }

        private static void WriteRestore(AccessEventStore store, long version)
        {
            if (version < 1)
            {
                throw new ArgumentException("A positive policy version is required.");
            }

            var restored = store.RestorePolicy(version);
            Console.WriteLine("{\"version\":" + restored.ToString(CultureInfo.InvariantCulture) + "}");
        }

        private static void WriteSave(AccessEventStore store, string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                throw new ArgumentException("--input <policy.json> is required.");
            }

            var json = File.ReadAllText(inputPath, Encoding.UTF8);
            var serializer = new JavaScriptSerializer();
            var file = serializer.Deserialize<PolicyFile>(json);
            if (file == null)
            {
                throw new ArgumentException("The policy file is empty.");
            }

            AccessMode mode;
            if (string.Equals(file.Mode, "AllowList", StringComparison.OrdinalIgnoreCase))
            {
                mode = AccessMode.AllowList;
            }
            else if (string.Equals(file.Mode, "BlockList", StringComparison.OrdinalIgnoreCase))
            {
                mode = AccessMode.BlockList;
            }
            else
            {
                throw new ArgumentException("Mode must be AllowList or BlockList.");
            }

            var rules = new List<DomainRule>();
            if (file.Rules != null)
            {
                foreach (var rule in file.Rules)
                {
                    rules.Add(new DomainRule(Guid.NewGuid(), rule.Domain, rule.IncludeSubdomains, rule.Enabled));
                }
            }

            var timedCategories = new List<TimedAccessCategory>();
            if (file.TimedCategories != null)
            {
                foreach (var category in file.TimedCategories)
                {
                    timedCategories.Add(new TimedAccessCategory(category.Name, category.LimitMinutes));
                }
            }

            var timedRules = new List<TimedAccessRule>();
            if (file.TimedRules != null)
            {
                foreach (var rule in file.TimedRules)
                {
                    timedRules.Add(new TimedAccessRule(
                        Guid.NewGuid(),
                        rule.Domain,
                        rule.IncludeSubdomains,
                        rule.Category,
                        rule.Enabled));
                }
            }

            var categories = new List<CategoryConfig>();
            if (file.Categories != null)
            {
                foreach (var category in file.Categories)
                {
                    categories.Add(new CategoryConfig(category.Name, category.SourceName, category.Enabled));
                }
            }

            var categoryRules = new List<CategoryRule>();
            if (file.CategoryRules != null)
            {
                foreach (var rule in file.CategoryRules)
                {
                    categoryRules.Add(new CategoryRule(
                        Guid.NewGuid(),
                        rule.Category,
                        rule.Domain,
                        rule.IncludeSubdomains,
                        rule.Enabled));
                }
            }

            var policy = new AccessPolicy(mode, rules, timedCategories, timedRules, categories, categoryRules);
            var version = store.SavePolicy(policy);
            Console.WriteLine("{\"version\":" + version.ToString(CultureInfo.InvariantCulture) + "}");
        }

        private static void WriteParsedCategories(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                throw new ArgumentException("--input <category-list.md> is required.");
            }

            var parsed = CategoryMarkdown.Parse(File.ReadAllText(inputPath, Encoding.UTF8), Path.GetFileName(inputPath));
            var serializer = new JavaScriptSerializer();
            Console.WriteLine(serializer.Serialize(parsed));
        }

        /// <summary>
        /// Imports events pulled from a paired endpoint. The write is idempotent per (device, event id),
        /// so a retried synchronization cannot duplicate audit rows.
        /// </summary>
        private static void WriteImportRemoteEvents(AccessEventStore store, string deviceId, string inputPath)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new ArgumentException("--device <id> is required.");
            }

            if (string.IsNullOrWhiteSpace(inputPath))
            {
                throw new ArgumentException("--input <events.json> is required.");
            }

            var serializer = new JavaScriptSerializer();
            var file = serializer.Deserialize<RemoteEventsFile>(File.ReadAllText(inputPath, Encoding.UTF8));
            if (file == null || file.Events == null)
            {
                throw new ArgumentException("The event file has no events.");
            }

            var events = new List<RemoteAccessEvent>();
            foreach (var item in file.Events)
            {
                if (item == null)
                {
                    continue;
                }

                Guid eventId;
                if (!Guid.TryParse(item.Id, out eventId))
                {
                    throw new ArgumentException("The event file contains an invalid event id.");
                }

                DateTime occurredAtUtc;
                if (!DateTime.TryParse(item.OccurredAtUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out occurredAtUtc))
                {
                    throw new ArgumentException("The event file contains an invalid event timestamp.");
                }

                if (occurredAtUtc.Kind != DateTimeKind.Utc)
                {
                    occurredAtUtc = occurredAtUtc.ToUniversalTime();
                }

                if (!Enum.IsDefined(typeof(ProxyAccessResult), item.Result))
                {
                    throw new ArgumentException("The event file contains an unsupported access result.");
                }

                events.Add(new RemoteAccessEvent(
                    deviceId,
                    eventId,
                    occurredAtUtc,
                    item.Domain,
                    item.Method,
                    (ProxyAccessResult)item.Result,
                    string.IsNullOrEmpty(item.RequestedUrl) ? null : item.RequestedUrl,
                    item.Port > 0 ? (int?)item.Port : null));
            }

            var inserted = store.ImportRemoteEvents(deviceId, events);
            Console.WriteLine("{\"imported\":" + inserted.ToString(CultureInfo.InvariantCulture) + "}");
        }

        private static void WriteRemoteEvents(AccessEventStore store, string deviceId, int limit)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new ArgumentException("--device <id> is required.");
            }

            var events = store.QueryRemoteEvents(deviceId, limit);
            var builder = new StringBuilder();
            builder.Append("{\"events\":[");
            for (var index = 0; index < events.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var item = events[index];
                builder.Append("{\"id\":\"").Append(item.EventId.ToString("D"))
                    .Append("\",\"occurredAtUtc\":\"").Append(item.OccurredAtUtc.ToString("o", CultureInfo.InvariantCulture))
                    .Append("\",\"domain\":\"").Append(Json.Escape(item.Domain))
                    .Append("\",\"method\":\"").Append(Json.Escape(item.Method))
                    .Append("\",\"result\":").Append((int)item.Result)
                    .Append(",\"resultText\":\"").Append(Json.Result(item.Result))
                    .Append("\",\"requestedUrl\":\"").Append(Json.Escape(item.RequestedUrl ?? string.Empty))
                    .Append("\",\"port\":").Append(item.DestinationPort.HasValue
                        ? item.DestinationPort.Value.ToString(CultureInfo.InvariantCulture)
                        : "0")
                    .Append('}');
            }

            builder.Append("]}");
            Console.WriteLine(builder.ToString());
        }

        private sealed class RemoteEventsFile
        {
            public List<RemoteEventFile> Events { get; set; }
        }

        private sealed class RemoteEventFile
        {
            public string Id { get; set; }

            public string OccurredAtUtc { get; set; }

            public string Domain { get; set; }

            public string Method { get; set; }

            public int Result { get; set; }

            public string RequestedUrl { get; set; }

            public int Port { get; set; }
        }
        private sealed class Options
        {
            public string Command { get; private set; }

            public string DatabasePath { get; private set; }

            public int Port { get; private set; }

            public int Limit { get; private set; }

            public int? ParentProcessId { get; private set; }

            public string InputPath { get; private set; }

            public long Version { get; private set; }

            public bool OnlyBlocked { get; private set; }

            public string Search { get; private set; }

            public string DeviceId { get; private set; }

            public DateTime? SinceUtc { get; private set; }

            public bool Ascending { get; private set; }

            public static Options Parse(string[] args)
            {
                var options = new Options
                {
                    Command = args.Length > 0 ? args[0] : string.Empty,
                    DatabasePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "WebBlocker",
                        "proxy.db"),
                    Port = 0,
                    Limit = 100,
                    Version = 0,
                    Search = null
                };

                for (var index = 1; index < args.Length; index++)
                {
                    var argument = args[index];
                    if (string.Equals(argument, "--database", StringComparison.OrdinalIgnoreCase))
                    {
                        options.DatabasePath = Path.GetFullPath(RequireValue(args, ref index, argument));
                    }
                    else if (string.Equals(argument, "--port", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsedPort;
                        if (!int.TryParse(RequireValue(args, ref index, argument), NumberStyles.None,
                                CultureInfo.InvariantCulture, out parsedPort) || parsedPort < 0 || parsedPort > 65535)
                        {
                            throw new ArgumentException("Port must be between 0 and 65535.");
                        }

                        options.Port = parsedPort;
                    }
                    else if (string.Equals(argument, "--limit", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsedLimit;
                        if (!int.TryParse(RequireValue(args, ref index, argument), NumberStyles.None,
                                CultureInfo.InvariantCulture, out parsedLimit) || parsedLimit < 1 || parsedLimit > 500)
                        {
                            throw new ArgumentException("A limit between 1 and 500 is required.");
                        }

                        options.Limit = parsedLimit;
                    }
                    else if (string.Equals(argument, "--input", StringComparison.OrdinalIgnoreCase))
                    {
                        options.InputPath = Path.GetFullPath(RequireValue(args, ref index, argument));
                    }
                    else if (string.Equals(argument, "--version", StringComparison.OrdinalIgnoreCase))
                    {
                        long parsedVersion;
                        if (!long.TryParse(RequireValue(args, ref index, argument), NumberStyles.None,
                                CultureInfo.InvariantCulture, out parsedVersion) || parsedVersion < 1)
                        {
                            throw new ArgumentException("A positive policy version is required.");
                        }

                        options.Version = parsedVersion;
                    }
                    else if (string.Equals(argument, "--only-blocked", StringComparison.OrdinalIgnoreCase))
                    {
                        options.OnlyBlocked = true;
                    }
                    else if (string.Equals(argument, "--ascending", StringComparison.OrdinalIgnoreCase))
                    {
                        options.Ascending = true;
                    }
                    else if (string.Equals(argument, "--search", StringComparison.OrdinalIgnoreCase))
                    {
                        options.Search = RequireValue(args, ref index, argument);
                    }
                    else if (string.Equals(argument, "--device", StringComparison.OrdinalIgnoreCase))                    {
                        var deviceId = RequireValue(args, ref index, argument).Trim();
                        if (deviceId.Length == 0 || deviceId.Length > 64)
                        {
                            throw new ArgumentException("A device id of 1 to 64 characters is required.");
                        }

                        options.DeviceId = deviceId;
                    }
                    else if (string.Equals(argument, "--since", StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime since;
                        if (!DateTime.TryParse(RequireValue(args, ref index, argument),
                                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out since))
                        {
                            throw new ArgumentException("--since must be an ISO-8601 UTC timestamp.");
                        }

                        options.SinceUtc = since.Kind == DateTimeKind.Utc ? since : since.ToUniversalTime();
                    }
                    else if (string.Equals(argument, "--parent-pid", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsedParent;
                        if (!int.TryParse(RequireValue(args, ref index, argument), NumberStyles.None,
                                CultureInfo.InvariantCulture, out parsedParent) || parsedParent < 1)
                        {
                            throw new ArgumentException("A parent process ID must be a positive integer.");
                        }

                        options.ParentProcessId = parsedParent;
                    }
                    else
                    {
                        throw new ArgumentException("Unknown option: " + argument);
                    }
                }

                return options;
            }

            private static string RequireValue(string[] args, ref int index, string option)
            {
                index++;
                if (index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("A value is required after " + option + ".");
                }

                return args[index];
            }
        }

        private sealed class PolicyFile
        {
            public string Mode { get; set; }

            public List<RuleFile> Rules { get; set; }

            public List<TimedCategoryFile> TimedCategories { get; set; }

            public List<TimedRuleFile> TimedRules { get; set; }

            public List<CategoryFile> Categories { get; set; }

            public List<CategoryRuleFile> CategoryRules { get; set; }
        }

        private sealed class RuleFile
        {
            public string Domain { get; set; }

            public bool IncludeSubdomains { get; set; }

            public bool Enabled { get; set; }
        }

        private sealed class TimedCategoryFile
        {
            public string Name { get; set; }

            public int LimitMinutes { get; set; }
        }

        private sealed class TimedRuleFile
        {
            public string Domain { get; set; }

            public bool IncludeSubdomains { get; set; }

            public string Category { get; set; }

            public bool Enabled { get; set; }
        }

        private sealed class CategoryFile
        {
            public string Name { get; set; }

            public string SourceName { get; set; }

            public bool Enabled { get; set; }
        }

        private sealed class CategoryRuleFile
        {
            public string Domain { get; set; }

            public bool IncludeSubdomains { get; set; }

            public string Category { get; set; }

            public bool Enabled { get; set; }
        }

        private sealed class CategoryMarkdown
        {
            private static readonly Regex Heading = new Regex(
                @"^\s{0,3}#{2,6}\s+(?<name>.+?)\s*$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

            private static readonly Regex Title = new Regex(
                @"^\s{0,3}#\s+\S",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

            private static readonly Regex Entry = new Regex(
                @"^\s*(?:[-*+]|\d+[.)])\s+(?<value>.+?)\s*$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

            public static CategoryParseResult Parse(string text, string sourceName)
            {
                var result = new CategoryParseResult();
                var categoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var ruleKeys = new HashSet<string>(StringComparer.Ordinal);
                string current = null;
                var lineNumber = 0;

                foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
                {
                    lineNumber++;
                    var line = raw.TrimEnd();
                    if (Title.IsMatch(line))
                    {
                        current = null;
                        continue;
                    }

                    var heading = Heading.Match(line);
                    if (heading.Success)
                    {
                        var name = heading.Groups["name"].Value.Trim();
                        if (name.Length == 0 || !categoryNames.Add(name))
                        {
                            result.Errors.Add("第 " + lineNumber + " 行：类别名称为空或重复。");
                            current = null;
                            continue;
                        }

                        result.Categories.Add(new CategoryFile { Name = name, SourceName = sourceName, Enabled = false });
                        current = name;
                        continue;
                    }

                    var entry = Entry.Match(line);
                    if (!entry.Success)
                    {
                        continue;
                    }

                    if (current == null)
                    {
                        result.Errors.Add("第 " + lineNumber + " 行：域名前缺少类别标题。");
                        continue;
                    }

                    var value = entry.Groups["value"].Value.Trim();
                    if (value.Length == 0 || value.StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var includeSubdomains = false;
                    if (value.EndsWith(" include:subdomains", StringComparison.OrdinalIgnoreCase))
                    {
                        includeSubdomains = true;
                        value = value.Substring(0, value.Length - " include:subdomains".Length).Trim();
                    }

                    if (value.StartsWith("*.", StringComparison.Ordinal))
                    {
                        includeSubdomains = true;
                        value = value.Substring(2);
                    }

                    string normalized;
                    try
                    {
                        normalized = DomainName.Normalize(value);
                    }
                    catch (ArgumentException)
                    {
                        result.Errors.Add("第 " + lineNumber + " 行：无法识别的域名「" + value + "」。");
                        continue;
                    }

                    var key = current + "|" + normalized + "|" + includeSubdomains;
                    if (!ruleKeys.Add(key))
                    {
                        result.Duplicates++;
                        continue;
                    }

                    result.Rules.Add(new CategoryRuleFile
                    {
                        Domain = normalized,
                        IncludeSubdomains = includeSubdomains,
                        Category = current,
                        Enabled = true
                    });
                }

                if (result.Categories.Count == 0)
                {
                    result.Errors.Add("文件中没有找到任何「## 类别名称」标题。");
                }

                return result;
            }
        }

        private sealed class CategoryParseResult
        {
            public List<CategoryFile> Categories { get; } = new List<CategoryFile>();

            public List<CategoryRuleFile> Rules { get; } = new List<CategoryRuleFile>();

            public List<string> Errors { get; } = new List<string>();

            public int Duplicates { get; set; }
        }
        private static class Json
        {
            public static string Escape(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return string.Empty;
                }

                var builder = new StringBuilder(value.Length + 8);
                foreach (var character in value)
                {
                    switch (character)
                    {
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '\b':
                            builder.Append("\\b");
                            break;
                        case '\f':
                            builder.Append("\\f");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            if (character < 32 || character == '\u2028' || character == '\u2029')
                            {
                                builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                builder.Append(character);
                            }

                            break;
                    }
                }

                return builder.ToString();
            }

            public static string Mode(AccessMode mode)
            {
                return mode == AccessMode.AllowList ? "AllowList" : "BlockList";
            }

            public static string Result(ProxyAccessResult result)
            {
                switch (result)
                {
                    case ProxyAccessResult.AllowedByPolicy:
                        return "允许";
                    case ProxyAccessResult.BlockedByPolicy:
                        return "黑/白名单拦截";
                    case ProxyAccessResult.BlockedByTimeLimit:
                        return "限时已用完";
                    case ProxyAccessResult.BlockedByCategory:
                        return "违规内容拦截";
                    case ProxyAccessResult.BlockPageFailure:
                        return "拦截页异常";
                    default:
                        return result.ToString();
                }
            }
        }
    }
}