using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WebBlocker.Shared
{
    public enum AccessMode
    {
        AllowList,
        BlockList
    }

    public sealed class AccessPolicy
    {
        private readonly ReadOnlyCollection<DomainRule> _rules;
        private readonly ReadOnlyCollection<TimedAccessCategory> _timedAccessCategories;
        private readonly ReadOnlyCollection<TimedAccessRule> _timedAccessRules;

        public AccessPolicy(AccessMode mode, IEnumerable<DomainRule> rules)
            : this(mode, rules, new TimedAccessCategory[0], new TimedAccessRule[0])
        {
        }

        public AccessPolicy(
            AccessMode mode,
            IEnumerable<DomainRule> rules,
            IEnumerable<TimedAccessCategory> timedAccessCategories,
            IEnumerable<TimedAccessRule> timedAccessRules)
        {
            if (mode != AccessMode.AllowList && mode != AccessMode.BlockList)
            {
                throw new ArgumentOutOfRangeException("mode");
            }

            if (rules == null)
            {
                throw new ArgumentNullException("rules");
            }

            if (timedAccessCategories == null)
            {
                throw new ArgumentNullException("timedAccessCategories");
            }

            if (timedAccessRules == null)
            {
                throw new ArgumentNullException("timedAccessRules");
            }

            var ruleList = new List<DomainRule>();
            var ruleIds = new HashSet<Guid>();
            var ruleKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var rule in rules)
            {
                if (rule == null)
                {
                    throw new ArgumentException("A policy cannot contain a null rule.", "rules");
                }

                if (!ruleIds.Add(rule.Id))
                {
                    throw new ArgumentException("Rule IDs must be unique.", "rules");
                }

                var key = rule.Domain + "|" + rule.IncludeSubdomains;
                if (!ruleKeys.Add(key))
                {
                    throw new ArgumentException("Duplicate domain rules are not allowed.", "rules");
                }

                ruleList.Add(rule);
            }

            var categoryList = new List<TimedAccessCategory>();
            var categoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in timedAccessCategories)
            {
                if (category == null)
                {
                    throw new ArgumentException("A policy cannot contain a null timed-access category.", "timedAccessCategories");
                }

                if (!categoryNames.Add(category.Name))
                {
                    throw new ArgumentException("Timed-access category names must be unique.", "timedAccessCategories");
                }

                categoryList.Add(category);
            }

            var timedRuleList = new List<TimedAccessRule>();
            var timedRuleIds = new HashSet<Guid>();
            var timedRuleKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var timedRule in timedAccessRules)
            {
                if (timedRule == null)
                {
                    throw new ArgumentException("A policy cannot contain a null timed-access rule.", "timedAccessRules");
                }

                if (!timedRuleIds.Add(timedRule.Id))
                {
                    throw new ArgumentException("Timed-access rule IDs must be unique.", "timedAccessRules");
                }

                var key = timedRule.Domain + "|" + timedRule.IncludeSubdomains;
                if (!timedRuleKeys.Add(key))
                {
                    throw new ArgumentException("Duplicate timed-access domain rules are not allowed.", "timedAccessRules");
                }

                if (!categoryNames.Contains(timedRule.Category))
                {
                    throw new ArgumentException(
                        "Every timed-access rule must reference a configured category.", "timedAccessRules");
                }

                timedRuleList.Add(timedRule);
            }

            Mode = mode;
            _rules = ruleList.AsReadOnly();
            _timedAccessCategories = categoryList.AsReadOnly();
            _timedAccessRules = timedRuleList.AsReadOnly();
        }

        public AccessMode Mode { get; private set; }

        public ReadOnlyCollection<DomainRule> Rules
        {
            get { return _rules; }
        }

        public ReadOnlyCollection<TimedAccessCategory> TimedAccessCategories
        {
            get { return _timedAccessCategories; }
        }

        public ReadOnlyCollection<TimedAccessRule> TimedAccessRules
        {
            get { return _timedAccessRules; }
        }
    }
}
