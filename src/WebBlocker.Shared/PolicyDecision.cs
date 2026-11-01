namespace WebBlocker.Shared
{
    public sealed class PolicyDecision
    {
        internal PolicyDecision(bool isAllowed, DomainRule matchedRule)
            : this(isAllowed, matchedRule, null)
        {
        }

        internal PolicyDecision(bool isAllowed, DomainRule matchedRule, string matchedCategory)
        {
            IsAllowed = isAllowed;
            MatchedRule = matchedRule;
            MatchedCategory = matchedCategory;
        }

        public bool IsAllowed { get; private set; }

        /// <summary>
        /// The base allowlist/blocklist rule that produced the decision, if any.
        /// In allowlist mode a match is the reason access was allowed; in blocklist mode it is the
        /// reason access was denied.
        /// </summary>
        public DomainRule MatchedRule { get; private set; }

        /// <summary>
        /// The blocked-content category that matched, if category enforcement denied the request.
        /// Non-null only when <see cref="IsAllowed"/> is false because of a category match.
        /// </summary>
        public string MatchedCategory { get; private set; }
    }
}