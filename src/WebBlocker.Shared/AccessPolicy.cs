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

        public AccessPolicy(AccessMode mode, IEnumerable<DomainRule> rules)
        {
            if (mode != AccessMode.AllowList && mode != AccessMode.BlockList)
            {
                throw new ArgumentOutOfRangeException("mode");
            }

            if (rules == null)
            {
                throw new ArgumentNullException("rules");
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

            Mode = mode;
            _rules = ruleList.AsReadOnly();
        }

        public AccessMode Mode { get; private set; }

        public ReadOnlyCollection<DomainRule> Rules
        {
            get { return _rules; }
        }
    }
}
