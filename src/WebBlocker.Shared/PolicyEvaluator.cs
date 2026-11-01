using System;

namespace WebBlocker.Shared
{
    public static class PolicyEvaluator
    {
        /// <summary>
        /// Evaluates the base allowlist/blocklist policy. Overlapping rules are resolved by taking the
        /// most specific match (longest domain, then exact before subdomain) so the result does not depend
        /// on storage order.
        /// </summary>
        public static PolicyDecision Evaluate(AccessPolicy policy, string host)
        {
            if (policy == null)
            {
                throw new ArgumentNullException("policy");
            }

            var normalizedHost = DomainName.Normalize(host);
            DomainRule matchedRule = null;

            foreach (var rule in policy.Rules)
            {
                if (!rule.MatchesNormalizedHost(normalizedHost))
                {
                    continue;
                }

                if (matchedRule == null || IsMoreSpecific(rule.Domain, rule.IncludeSubdomains, matchedRule.Domain, matchedRule.IncludeSubdomains))
                {
                    matchedRule = rule;
                }
            }

            // Allowlist: only an explicitly matching host is allowed.
            // Blocklist: a matching rule is a denial; every other host is allowed.
            var isAllowed = policy.Mode == AccessMode.AllowList
                ? matchedRule != null
                : matchedRule == null;

            return new PolicyDecision(isAllowed, matchedRule);
        }

        /// <summary>
        /// Finds the most specific enabled timed-access rule for the host, or null when none matches.
        /// </summary>
        public static TimedAccessRule FindTimedAccessRule(AccessPolicy policy, string host)
        {
            if (policy == null)
            {
                throw new ArgumentNullException("policy");
            }

            var normalizedHost = DomainName.Normalize(host);
            TimedAccessRule matchedRule = null;
            foreach (var rule in policy.TimedAccessRules)
            {
                if (!rule.MatchesNormalizedHost(normalizedHost))
                {
                    continue;
                }

                if (matchedRule == null ||
                    rule.Domain.Length > matchedRule.Domain.Length ||
                    (rule.Domain.Length == matchedRule.Domain.Length &&
                     !rule.IncludeSubdomains && matchedRule.IncludeSubdomains))
                {
                    matchedRule = rule;
                }
            }

            return matchedRule;
        }

        /// <summary>
        /// Evaluates category enforcement. Returns a denying decision carrying the matched category name,
        /// or null when no enabled category rule matches.
        /// </summary>
        public static PolicyDecision EvaluateCategory(AccessPolicy policy, string host)
        {
            if (policy == null)
            {
                throw new ArgumentNullException("policy");
            }

            var normalizedHost = DomainName.Normalize(host);
            if (policy.CategoryRules.Count == 0)
            {
                return null;
            }

            var enabledCategories = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in policy.Categories)
            {
                if (category.IsEnabled)
                {
                    enabledCategories.Add(category.Name);
                }
            }

            if (enabledCategories.Count == 0)
            {
                return null;
            }

            CategoryRule matchedRule = null;
            foreach (var rule in policy.CategoryRules)
            {
                if (!rule.MatchesNormalizedHost(normalizedHost) || !enabledCategories.Contains(rule.Category))
                {
                    continue;
                }

                if (matchedRule == null ||
                    rule.Domain.Length > matchedRule.Domain.Length ||
                    (rule.Domain.Length == matchedRule.Domain.Length &&
                     !rule.IncludeSubdomains && matchedRule.IncludeSubdomains))
                {
                    matchedRule = rule;
                }
            }

            return matchedRule == null ? null : new PolicyDecision(false, null, matchedRule.Category);
        }

        private static bool IsMoreSpecific(
            string candidateDomain,
            bool candidateSubdomains,
            string currentDomain,
            bool currentSubdomains)
        {
            if (candidateDomain.Length != currentDomain.Length)
            {
                return candidateDomain.Length > currentDomain.Length;
            }

            if (candidateSubdomains != currentSubdomains)
            {
                return !candidateSubdomains;
            }

            return false;
        }
    }
}