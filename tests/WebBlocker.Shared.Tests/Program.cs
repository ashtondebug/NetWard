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
            Run("matches timed-access rules by most-specific domain", MatchesTimedAccessRules);
            Run("rejects invalid timed-access categories and references", RejectsInvalidTimedAccessPolicies);
            Run("bundles a truthful standalone violation notice without image-capture claims",
                ViolationNoticeIsBundledAndTransparent);
            RunAsync("SQLite policy and event records survive restart with deduplication and filters",
                PersistenceTests.PolicyAndAccessEventsPersistAcrossStoreInstancesAsync).GetAwaiter().GetResult();
            RunAsync("SQLite online backup and event retention preserve policy and cutoff semantics",
                PersistenceTests.BackupAndRetentionPreservePolicyAndApplyCutoffAsync).GetAwaiter().GetResult();
            RunAsync("automatic access-event retention deletes records older than 30 days using UTC",
                PersistenceTests.AutomaticRetentionUsesThirtyDayUtcCutoffAsync).GetAwaiter().GetResult();
            RunAsync("SQLite schema migrates v1 policy data and rejects a newer unsupported version",
                PersistenceTests.SchemaVersionHandlingIsExplicitAsync).GetAwaiter().GetResult();
            RunAsync("SQLite timed-access windows retain first-use expiry and reset at local midnight",
                PersistenceTests.TimedAccessWindowsUseDailyWallClockAsync).GetAwaiter().GetResult();
            RunAsync("missing local block page is logged as a filter failure",
                ProxyIntegrationTests.BlockPageFailureIsPersistedAndDoesNotMasqueradeAsSuccessAsync).GetAwaiter().GetResult();
            RunAsync("explicit HTTP proxy enforces rules and serves the local block page",
                ProxyIntegrationTests.HttpProxyEnforcesPolicyAsync).GetAwaiter().GetResult();
            RunAsync("explicit CONNECT proxy blocks denied hosts and tunnels allowed hosts",
                ProxyIntegrationTests.ConnectProxyFiltersAndTunnelsAsync).GetAwaiter().GetResult();
            RunAsync("proxy rejects mismatched request and Host headers",
                ProxyIntegrationTests.RejectsMismatchedHostHeaderAsync).GetAwaiter().GetResult();
            RunAsync("proxy serves timeout page after daily timed access expires",
                ProxyIntegrationTests.TimedAccessExpiryServesTimeoutPageAsync).GetAwaiter().GetResult();
            RunAsync("timed rules do not override a base-policy denial",
                ProxyIntegrationTests.TimedAccessDoesNotOverrideBaseDenialAsync).GetAwaiter().GetResult();
            RunAsync("an active CONNECT tunnel closes at timed-window expiry",
                ProxyIntegrationTests.TimedConnectTunnelClosesAtExpiryAsync).GetAwaiter().GetResult();

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

        private static void MatchesTimedAccessRules()
        {
            var category = new TimedAccessCategory("Study", 30);
            var broadRule = new TimedAccessRule(Guid.NewGuid(), "example.com", true, "Study", true);
            var exactRule = new TimedAccessRule(Guid.NewGuid(), "school.example.com", false, "Study", true);
            var policy = new AccessPolicy(
                AccessMode.BlockList,
                new DomainRule[0],
                new[] { category },
                new[] { broadRule, exactRule });

            AssertEqual(exactRule.Id, PolicyEvaluator.FindTimedAccessRule(policy, "school.example.com").Id);
            AssertEqual(broadRule.Id, PolicyEvaluator.FindTimedAccessRule(policy, "www.example.com").Id);
            AssertTrue(PolicyEvaluator.FindTimedAccessRule(policy, "notexample.com") == null);
            AssertTrue(PolicyEvaluator.Evaluate(policy, "school.example.com").IsAllowed);
        }

        private static void RejectsInvalidTimedAccessPolicies()
        {
            AssertThrows<ArgumentOutOfRangeException>(() => new TimedAccessCategory("Study", 0));
            AssertThrows<ArgumentException>(() => new TimedAccessCategory("Bad\rName", 30));

            var rule = new TimedAccessRule(Guid.NewGuid(), "example.com", true, "Unknown", true);
            AssertThrows<ArgumentException>(() => new AccessPolicy(
                AccessMode.BlockList,
                new DomainRule[0],
                new[] { new TimedAccessCategory("Study", 30) },
                new[] { rule }));
        }

        private static void ViolationNoticeIsBundledAndTransparent()
        {
            var pagePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "violation.html");
            AssertTrue(System.IO.File.Exists(pagePath));

            var page = System.IO.File.ReadAllText(pagePath, System.Text.Encoding.UTF8);
            AssertTrue(page.Contains("此计算机因访问违规内容已被阻断"));
            AssertTrue(page.Contains("请等待管理人员与你联系"));
            AssertTrue(page.Contains("本次违规访问已记录"));
            AssertTrue(page.Contains("HTTP 请求的完整网址、访问时间、主机名、请求方式和策略结果已记录"));
            AssertTrue(page.Contains("HTTPS 仅能看到目标主机和端口"));
            AssertTrue(page.Contains("当前未采集 IP 地址或 Windows 用户名"));
            AssertTrue(page.Contains("<footer>NETWARD 网界卫士</footer>"));
            AssertFalse(page.Contains("已调用本机摄像头固定证据"));
            AssertFalse(page.Contains("已通过截图固定证据"));
            AssertFalse(page.Contains("已固定证据"));
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
