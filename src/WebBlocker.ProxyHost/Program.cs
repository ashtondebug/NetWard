using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using WebBlocker.Filtering;
using WebBlocker.Persistence;
using WebBlocker.Shared;

namespace WebBlocker.ProxyHost
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 1 &&
                    (string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(args[0], "-h", StringComparison.OrdinalIgnoreCase)))
                {
                    WriteUsage();
                    return 0;
                }

                var options = ParseArguments(args);
                if (options.IsMaintenanceCommand &&
                    !File.Exists(options.DatabasePath))
                {
                    throw new FileNotFoundException("The local policy database does not exist.", options.DatabasePath);
                }

                var store = new AccessEventStore(options.DatabasePath);
                if (options.BackupPath != null)
                {
                    store.BackupTo(options.BackupPath);
                    Console.WriteLine("Database backup created: " + Path.GetFullPath(options.BackupPath));
                    return 0;
                }

                if (options.PruneEventsOlderThanDays.HasValue)
                {
                    var cutoffUtc = DateTime.UtcNow.AddDays(-options.PruneEventsOlderThanDays.Value);
                    var deletedCount = store.PruneAccessEventsBefore(cutoffUtc);
                    Console.WriteLine(
                        "Removed {0} access events older than {1} days. Policy data was not changed.",
                        deletedCount,
                        options.PruneEventsOlderThanDays.Value);
                    return 0;
                }

                if (options.ShowPolicyHistory)
                {
                    var entries = store.QueryPolicyHistory(1, 100);
                    if (entries.Count == 0)
                    {
                        Console.WriteLine("No saved policy history exists.");
                        return 0;
                    }

                    foreach (var entry in entries)
                    {
                        Console.WriteLine(
                            "Version {0} | {1} | {2} rule(s) | {3}",
                            entry.Version,
                            entry.Mode,
                            entry.RuleCount,
                            entry.UpdatedAtUtc.ToString("o", CultureInfo.InvariantCulture));
                    }

                    return 0;
                }

                if (options.RestorePolicyVersion.HasValue)
                {
                    var restoredVersion = store.RestorePolicy(options.RestorePolicyVersion.Value);
                    Console.WriteLine(
                        "Policy version {0} was restored as new version {1}.",
                        options.RestorePolicyVersion.Value,
                        restoredVersion);
                    return 0;
                }

                AccessPolicy policy;
                long policyVersion;
                if (options.Mode.HasValue)
                {
                    if (options.Rules.Count == 0 && !options.AllowEmptyRules)
                    {
                        throw new ArgumentException("Supply at least one --rule, or add --allow-empty-rules to intentionally replace the current rules with an empty list.");
                    }

                    policy = new AccessPolicy(options.Mode.Value, options.Rules);
                    policyVersion = store.SavePolicy(policy);
                }
                else
                {
                    PolicySnapshot snapshot;
                    if (!store.TryLoadPolicy(out snapshot))
                    {
                        throw new ArgumentException("No saved policy exists. Set --mode and at least one --rule, or use --allow-empty-rules.");
                    }

                    policy = snapshot.Policy;
                    policyVersion = snapshot.Version;
                }

                using (var shutdown = new ManualResetEventSlim())
                using (var proxy = new DomainFilteringProxy(
                    policy,
                    options.Port,
                    eventStore: store,
                    policyVersion: policyVersion))
                {
                    proxy.ClientError += (sender, eventArgs) =>
                        Console.Error.WriteLine("Proxy error: " + eventArgs.Exception.Message);

                    Console.CancelKeyPress += OnCancelKeyPress;
                    _shutdownEvent = shutdown;
                    try
                    {
                        try
                        {
                            proxy.Start();
                        }
                        catch (SocketException exception)
                        {
                            Console.Error.WriteLine("Could not start the local proxy: " + exception.Message);
                            return 1;
                        }

                        Console.WriteLine("WebBlocker explicit proxy listening on 127.0.0.1:{0}", proxy.Port);
                        Console.WriteLine("Policy: {0}; rules: {1}; version: {2}",
                            policy.Mode, policy.Rules.Count, policyVersion);
                        Console.WriteLine("Local policy and access events: " + store.DatabasePath);
                        Console.WriteLine("HTTP is inspected by hostname. HTTPS CONNECT is filtered by hostname only; TLS content is not inspected.");
                        Console.WriteLine("This proxy only filters clients explicitly configured to use it; it does not enforce system-wide network policy.");
                        Console.WriteLine("Press Ctrl+C to stop.");
                        shutdown.Wait();
                        proxy.StopAsync().GetAwaiter().GetResult();
                    }
                    finally
                    {
                        _shutdownEvent = null;
                        Console.CancelKeyPress -= OnCancelKeyPress;
                    }
                }

                return 0;
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine(exception.Message);
                WriteUsage();
                return 2;
            }
            catch (KeyNotFoundException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 2;
            }
            catch (SQLiteException exception)
            {
                Console.Error.WriteLine("The local policy database operation failed: " + exception.Message);
                return 1;
            }
            catch (IOException exception)
            {
                Console.Error.WriteLine("The local policy database could not be read or written: " + exception.Message);
                return 1;
            }
            catch (UnauthorizedAccessException exception)
            {
                Console.Error.WriteLine("Access to the local policy database was denied: " + exception.Message);
                return 1;
            }
        }

        private static ManualResetEventSlim _shutdownEvent;

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            var shutdown = _shutdownEvent;
            if (shutdown != null)
            {
                shutdown.Set();
            }
        }

        private static ProxyOptions ParseArguments(string[] args)
        {
            AccessMode? mode = null;
            var port = 18080;
            var databasePath = GetDefaultDatabasePath();
            var allowEmptyRules = false;
            var portSpecified = false;
            string backupPath = null;
            int? pruneEventsOlderThanDays = null;
            var showPolicyHistory = false;
            long? restorePolicyVersion = null;
            var rules = new List<DomainRule>();
            var ruleKeys = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < args.Length; index++)
            {
                var argument = args[index];
                if (string.Equals(argument, "--mode", StringComparison.OrdinalIgnoreCase))
                {
                    var value = RequireValue(args, ref index, argument);
                    if (string.Equals(value, "allowlist", StringComparison.OrdinalIgnoreCase))
                    {
                        mode = AccessMode.AllowList;
                    }
                    else if (string.Equals(value, "blocklist", StringComparison.OrdinalIgnoreCase))
                    {
                        mode = AccessMode.BlockList;
                    }
                    else
                    {
                        throw new ArgumentException("Mode must be allowlist or blocklist.");
                    }
                }
                else if (string.Equals(argument, "--port", StringComparison.OrdinalIgnoreCase))
                {
                    var value = RequireValue(args, ref index, argument);
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
                        port < 0 || port > 65535)
                    {
                        throw new ArgumentException("Port must be between 0 and 65535.");
                    }

                    portSpecified = true;
                }
                else if (string.Equals(argument, "--rule", StringComparison.OrdinalIgnoreCase))
                {
                    var value = RequireValue(args, ref index, argument);
                    var separator = value.IndexOf(':');
                    if (separator <= 0 || separator == value.Length - 1)
                    {
                        throw new ArgumentException("A rule must use exact:domain or subdomains:domain.");
                    }

                    var scope = value.Substring(0, separator);
                    var includeSubdomains = string.Equals(scope, "subdomains", StringComparison.OrdinalIgnoreCase);
                    if (!includeSubdomains && !string.Equals(scope, "exact", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException("Rule scope must be exact or subdomains.");
                    }

                    var rule = new DomainRule(Guid.NewGuid(), value.Substring(separator + 1), includeSubdomains, true);
                    var key = rule.Domain + "|" + rule.IncludeSubdomains;
                    if (!ruleKeys.Add(key))
                    {
                        throw new ArgumentException("Duplicate normalized domain rule: " + rule.Domain);
                    }

                    rules.Add(rule);
                }
                else if (string.Equals(argument, "--database", StringComparison.OrdinalIgnoreCase))
                {
                    databasePath = RequireValue(args, ref index, argument);
                }
                else if (string.Equals(argument, "--backup-to", StringComparison.OrdinalIgnoreCase))
                {
                    if (backupPath != null)
                    {
                        throw new ArgumentException("Specify --backup-to only once.");
                    }

                    backupPath = RequireValue(args, ref index, argument);
                }
                else if (string.Equals(argument, "--prune-events-older-than-days", StringComparison.OrdinalIgnoreCase))
                {
                    var value = RequireValue(args, ref index, argument);
                    int parsedDays;
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsedDays) ||
                        parsedDays < 1 || parsedDays > 36500)
                    {
                        throw new ArgumentException("Event retention must be between 1 and 36500 days.");
                    }

                    pruneEventsOlderThanDays = parsedDays;
                }
                else if (string.Equals(argument, "--policy-history", StringComparison.OrdinalIgnoreCase))
                {
                    showPolicyHistory = true;
                }
                else if (string.Equals(argument, "--restore-policy-version", StringComparison.OrdinalIgnoreCase))
                {
                    var value = RequireValue(args, ref index, argument);
                    long parsedVersion;
                    if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsedVersion) ||
                        parsedVersion < 1)
                    {
                        throw new ArgumentException("A policy version must be a positive integer.");
                    }

                    restorePolicyVersion = parsedVersion;
                }
                else if (string.Equals(argument, "--allow-empty-rules", StringComparison.OrdinalIgnoreCase))
                {
                    allowEmptyRules = true;
                }
                else
                {
                    throw new ArgumentException("Unknown option: " + argument);
                }
            }

            var maintenanceCommandCount =
                (backupPath != null ? 1 : 0) +
                (pruneEventsOlderThanDays.HasValue ? 1 : 0) +
                (showPolicyHistory ? 1 : 0) +
                (restorePolicyVersion.HasValue ? 1 : 0);
            if (maintenanceCommandCount > 1)
            {
                throw new ArgumentException("Choose only one database maintenance or policy-history command for each run.");
            }

            var maintenanceRequested = maintenanceCommandCount == 1;
            if (maintenanceRequested &&
                (mode.HasValue || rules.Count > 0 || allowEmptyRules || portSpecified))
            {
                throw new ArgumentException("Database maintenance and policy-history commands cannot be combined with proxy or policy options.");
            }

            if (rules.Count > 0 && !mode.HasValue)
            {
                throw new ArgumentException("A policy mode is required when specifying --rule.");
            }

            return new ProxyOptions(
                mode,
                port,
                databasePath,
                allowEmptyRules,
                rules,
                backupPath,
                pruneEventsOlderThanDays,
                showPolicyHistory,
                restorePolicyVersion);
        }

        private static string GetDefaultDatabasePath()
        {
            var applicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(applicationDataPath))
            {
                throw new InvalidOperationException("The current user's local application-data directory is unavailable.");
            }

            return System.IO.Path.Combine(applicationDataPath, "WebBlocker", "proxy.db");
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

        private static void WriteUsage()
        {
            Console.WriteLine("WebBlocker explicit loopback proxy");
            Console.WriteLine("Usage: WebBlocker.ProxyHost.exe [--mode allowlist|blocklist] [--port 0..65535] [--database path] [--rule exact:domain|subdomains:domain ...] [--allow-empty-rules]");
            Console.WriteLine("       WebBlocker.ProxyHost.exe [--database path] --backup-to destination");
            Console.WriteLine("       WebBlocker.ProxyHost.exe [--database path] --prune-events-older-than-days 1..36500");
            Console.WriteLine("       WebBlocker.ProxyHost.exe [--database path] --policy-history");
            Console.WriteLine("       WebBlocker.ProxyHost.exe [--database path] --restore-policy-version version");
            Console.WriteLine("Default port: 18080. Port 0 selects an available ephemeral port.");
            Console.WriteLine("The first run requires --mode and at least one --rule; --allow-empty-rules explicitly permits no rules.");
            Console.WriteLine("Policy and access events are stored in the user's LocalAppData\\WebBlocker\\proxy.db by default.");
            Console.WriteLine("Later runs may omit --mode to restore the most recently saved policy.");
            Console.WriteLine("Only clients explicitly configured for this proxy are filtered. It does not configure Windows networking.");
            Console.WriteLine("HTTPS CONNECT decisions use the requested hostname; TLS is not decrypted.");
            Console.WriteLine("Backups use SQLite's online backup API and do not overwrite an existing destination.");
            Console.WriteLine("Event pruning removes only records strictly older than the requested UTC age; back up first.");
            Console.WriteLine("Restoring a policy creates a new, monotonically increasing version; history is read-only.");
        }

        private sealed class ProxyOptions
        {
            public ProxyOptions(
                AccessMode? mode,
                int port,
                string databasePath,
                bool allowEmptyRules,
                List<DomainRule> rules,
                string backupPath,
                int? pruneEventsOlderThanDays,
                bool showPolicyHistory,
                long? restorePolicyVersion)
            {
                Mode = mode;
                Port = port;
                DatabasePath = databasePath;
                AllowEmptyRules = allowEmptyRules;
                Rules = rules;
                BackupPath = backupPath;
                PruneEventsOlderThanDays = pruneEventsOlderThanDays;
                ShowPolicyHistory = showPolicyHistory;
                RestorePolicyVersion = restorePolicyVersion;
            }

            public AccessMode? Mode { get; private set; }

            public int Port { get; private set; }

            public string DatabasePath { get; private set; }

            public bool AllowEmptyRules { get; private set; }

            public List<DomainRule> Rules { get; private set; }

            public string BackupPath { get; private set; }

            public int? PruneEventsOlderThanDays { get; private set; }

            public bool ShowPolicyHistory { get; private set; }

            public long? RestorePolicyVersion { get; private set; }

            public bool IsMaintenanceCommand
            {
                get
                {
                    return BackupPath != null ||
                           PruneEventsOlderThanDays.HasValue ||
                           ShowPolicyHistory ||
                           RestorePolicyVersion.HasValue;
                }
            }
        }

    }
}
