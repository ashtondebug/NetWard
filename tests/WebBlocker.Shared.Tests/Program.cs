using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebBlocker.Shared;

namespace WebBlocker.Shared.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Run("normalizes whitespace, case, and root dot", NormalizeHostname);
            Run("normalizes internationalized domain names", NormalizeIdn);
            Run("rejects malformed hosts and IP addresses", RejectsInvalidHosts);
            Run("matches exact domains and label-boundary subdomains", MatchesDomainBoundaries);
            Run("does not match subdomains when disabled", ExactRuleDoesNotMatchSubdomains);
            Run("evaluates whitelist default deny", EvaluatesWhitelist);
            Run("evaluates blacklist default allow", EvaluatesBlacklist);
            Run("ignores disabled rules", IgnoresDisabledRules);
            Run("rejects duplicate normalized rules", RejectsDuplicateRules);
            RunAsync("SQLite policy and event records survive restart with deduplication and filters",
                PersistenceTests.PolicyAndAccessEventsPersistAcrossStoreInstancesAsync).GetAwaiter().GetResult();
            RunAsync("SQLite online backup and event retention preserve policy and cutoff semantics",
                PersistenceTests.BackupAndRetentionPreservePolicyAndApplyCutoffAsync).GetAwaiter().GetResult();
            RunAsync("SQLite schema migrates v1 policy data and rejects a newer unsupported version",
                PersistenceTests.SchemaVersionHandlingIsExplicitAsync).GetAwaiter().GetResult();
            RunAsync("missing local block page is logged as a filter failure",
                ProxyIntegrationTests.BlockPageFailureIsPersistedAndDoesNotMasqueradeAsSuccessAsync).GetAwaiter().GetResult();
            RunAsync("explicit HTTP proxy enforces rules and serves the local block page",
                ProxyIntegrationTests.HttpProxyEnforcesPolicyAsync).GetAwaiter().GetResult();
            RunAsync("explicit CONNECT proxy blocks denied hosts and tunnels allowed hosts",
                ProxyIntegrationTests.ConnectProxyFiltersAndTunnelsAsync).GetAwaiter().GetResult();
            RunAsync("proxy rejects mismatched request and Host headers",
                ProxyIntegrationTests.RejectsMismatchedHostHeaderAsync).GetAwaiter().GetResult();

            Console.WriteLine("{0} tests passed; {1} failed.", _passed, _failed);
            return Environment.ExitCode;
        }

        private static void NormalizeHostname()
        {
            AssertEqual("example.com", DomainName.Normalize("  EXAMPLE.COM.  "));
        }

        private static void NormalizeIdn()
        {
            AssertEqual("xn--bcher-kva.de", DomainName.Normalize("bücher.de"));
        }

        private static void RejectsInvalidHosts()
        {
            AssertThrows<ArgumentException>(() => DomainName.Normalize(""));
            AssertThrows<ArgumentException>(() => DomainName.Normalize("example.com.."));
            AssertThrows<ArgumentException>(() => DomainName.Normalize("example-.com"));
            AssertThrows<ArgumentException>(() => DomainName.Normalize("example.com/path"));
            AssertThrows<ArgumentException>(() => DomainName.Normalize("127.0.0.1"));
            AssertThrows<ArgumentException>(() => DomainName.Normalize("127.1"));
            AssertThrows<ArgumentException>(() => DomainName.Normalize("2130706433"));
            AssertThrows<ArgumentException>(() => DomainName.Normalize("2001:db8::1"));
        }

        private static void MatchesDomainBoundaries()
        {
            var rule = CreateRule("example.com", true);
            AssertTrue(rule.Matches("example.com"));
            AssertTrue(rule.Matches("www.example.com"));
            AssertTrue(rule.Matches("school.example.com"));
            AssertFalse(rule.Matches("notexample.com"));
            AssertFalse(rule.Matches("example.com.attacker.invalid"));
        }

        private static void ExactRuleDoesNotMatchSubdomains()
        {
            var rule = CreateRule("example.com", false);
            AssertTrue(rule.Matches("example.com"));
            AssertFalse(rule.Matches("www.example.com"));
        }

        private static void EvaluatesWhitelist()
        {
            var rule = CreateRule("example.com", true);
            var policy = new AccessPolicy(AccessMode.AllowList, new[] { rule });

            var allowed = PolicyEvaluator.Evaluate(policy, "school.example.com");
            AssertTrue(allowed.IsAllowed);
            AssertEqual(rule.Id, allowed.MatchedRule.Id);

            var denied = PolicyEvaluator.Evaluate(policy, "other.invalid");
            AssertFalse(denied.IsAllowed);
            AssertTrue(denied.MatchedRule == null);
        }

        private static void EvaluatesBlacklist()
        {
            var rule = CreateRule("example.com", true);
            var policy = new AccessPolicy(AccessMode.BlockList, new[] { rule });

            AssertFalse(PolicyEvaluator.Evaluate(policy, "www.example.com").IsAllowed);
            AssertTrue(PolicyEvaluator.Evaluate(policy, "other.invalid").IsAllowed);
        }

        private static void IgnoresDisabledRules()
        {
            var rule = new DomainRule(Guid.NewGuid(), "example.com", true, false);
            var policy = new AccessPolicy(AccessMode.AllowList, new[] { rule });

            var result = PolicyEvaluator.Evaluate(policy, "example.com");
            AssertFalse(result.IsAllowed);
            AssertTrue(result.MatchedRule == null);
        }

        private static void RejectsDuplicateRules()
        {
            var first = CreateRule("example.com", true);
            var duplicate = CreateRule(" EXAMPLE.com. ", true);
            AssertThrows<ArgumentException>(
                () => new AccessPolicy(AccessMode.BlockList, new List<DomainRule> { first, duplicate }));
        }

        private static DomainRule CreateRule(string domain, bool includeSubdomains)
        {
            return new DomainRule(Guid.NewGuid(), domain, includeSubdomains, true);
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("PASS " + name);
            }
            catch (Exception exception)
            {
                _failed++;
                Console.Error.WriteLine("FAIL " + name + ": " + exception);
                Environment.ExitCode = 1;
            }
        }

        private static async Task RunAsync(string name, Func<Task> test)
        {
            try
            {
                await test().ConfigureAwait(false);
                _passed++;
                Console.WriteLine("PASS " + name);
            }
            catch (Exception exception)
            {
                _failed++;
                Console.Error.WriteLine("FAIL " + name + ": " + exception);
                Environment.ExitCode = 1;
            }
        }

        private static void AssertTrue(bool condition)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Expected true.");
            }
        }

        private static void AssertFalse(bool condition)
        {
            if (condition)
            {
                throw new InvalidOperationException("Expected false.");
            }
        }

        private static void AssertEqual<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    string.Format("Expected '{0}', got '{1}'.", expected, actual));
            }
        }

        private static void AssertThrows<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
        }
    }
}
