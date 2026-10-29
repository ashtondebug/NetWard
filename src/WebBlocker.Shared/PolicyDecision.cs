namespace WebBlocker.Shared
{
    public sealed class PolicyDecision
    {
        internal PolicyDecision(bool isAllowed, DomainRule matchedRule)
        {
            IsAllowed = isAllowed;
            MatchedRule = matchedRule;
        }

        public bool IsAllowed { get; private set; }

        public DomainRule MatchedRule { get; private set; }
    }
}
