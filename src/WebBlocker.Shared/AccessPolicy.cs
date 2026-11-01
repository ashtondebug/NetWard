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
        private readonly ReadOnlyCollection<CategoryConfig> _categories;
        private readonly ReadOnlyCollection<CategoryRule> _categoryRules;

        public AccessPolicy(AccessMode mode, IEnumerable<DomainRule> rules)
            : this(mode, rules, new TimedAccessCategory[0], new TimedAccessRule[0])
        {
        }

        public AccessPolicy(
            AccessMode mode,
            IEnumerable<DomainRule> rules,
            IEnumerable<TimedAccessCategory> timedAccessCategories,
            IEnumerable<TimedAccessRule> timedAccessRules)
            : this(
                mode,
                rules,
                timedAccessCategories,
                timedAccessRules,
                new CategoryConfig[0],
                new CategoryRule[0])
        {
        }

        public AccessPolicy(
            AccessMode mode,
            IEnumerable<DomainRule> rules,
            IEnumerable<TimedAccessCategory> timedAccessCategories,
            IEnumerable<TimedAccessRule> timedAccessRules,
            IEnumerable<CategoryConfig> categories,
            IEnumerable<CategoryRule> categoryRules)
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

            if (categories == null)
            {
                throw new ArgumentNullException("categories");
            }

            if (categoryRules == null)
            {
                throw new ArgumentNullException("categoryRules");
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

            var blockedCategoryList = new List<CategoryConfig>();
            var blockedCategoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in categories)
            {
                if (category == null)
                {
                    throw new ArgumentException("A policy cannot contain a null blocked-content category.", "categories");
                }

                if (!blockedCategoryNames.Add(category.Name))
                {
                    throw new ArgumentException("Blocked-content category names must be unique.", "categories");
                }

                blockedCategoryList.Add(category);
            }

            var categoryRuleList = new List<CategoryRule>();
            var categoryRuleIds = new HashSet<Guid>();
            var categoryRuleKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var categoryRule in categoryRules)
            {
                if (categoryRule == null)
                {
                    throw new ArgumentException("A policy cannot contain a null category rule.", "categoryRules");
                }

                if (!categoryRuleIds.Add(categoryRule.Id))
                {
                    throw new ArgumentException("Category rule IDs must be unique.", "categoryRules");
                }

                var key = categoryRule.Domain + "|" + categoryRule.IncludeSubdomains;
                if (!categoryRuleKeys.Add(key))
                {
                    throw new ArgumentException("Duplicate category domain rules are not allowed.", "categoryRules");
                }

                if (!blockedCategoryNames.Contains(categoryRule.Category))
                {
                    throw new ArgumentException(
                        "Every category rule must reference a configured category.", "categoryRules");
                }

                categoryRuleList.Add(categoryRule);
            }

            Mode = mode;
            _rules = ruleList.AsReadOnly();
            _timedAccessCategories = categoryList.AsReadOnly();
            _timedAccessRules = timedRuleList.AsReadOnly();
            _categories = blockedCategoryList.AsReadOnly();
            _categoryRules = categoryRuleList.AsReadOnly();
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

        public ReadOnlyCollection<CategoryConfig> Categories
        {
            get { return _categories; }
        }

        public ReadOnlyCollection<CategoryRule> CategoryRules
        {
            get { return _categoryRules; }
        }
    }
}