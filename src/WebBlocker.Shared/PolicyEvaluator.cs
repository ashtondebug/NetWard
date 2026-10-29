using System;

namespace WebBlocker.Shared
{
    public static class PolicyEvaluator
    {
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
                if (rule.MatchesNormalizedHost(normalizedHost))
                {
                    matchedRule = rule;
                    break;
                }
            }

            var isAllowed = policy.Mode == AccessMode.AllowList
                ? matchedRule != null
                : matchedRule == null;

            return new PolicyDecision(isAllowed, matchedRule);
        }

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
    }
}
