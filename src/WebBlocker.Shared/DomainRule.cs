using System;

namespace WebBlocker.Shared
{
    public sealed class DomainRule
    {
        public DomainRule(Guid id, string domain, bool includeSubdomains, bool isEnabled)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException("A rule ID is required.", "id");
            }

            Id = id;
            Domain = DomainName.Normalize(domain);
            IncludeSubdomains = includeSubdomains;
            IsEnabled = isEnabled;
        }

        public Guid Id { get; private set; }

        public string Domain { get; private set; }

        public bool IncludeSubdomains { get; private set; }

        public bool IsEnabled { get; private set; }

        public bool Matches(string host)
        {
            return MatchesNormalizedHost(DomainName.Normalize(host));
        }

        internal bool MatchesNormalizedHost(string normalizedHost)
        {
            if (!IsEnabled)
            {
                return false;
            }

            return string.Equals(normalizedHost, Domain, StringComparison.Ordinal) ||
                   (IncludeSubdomains &&
                    normalizedHost.EndsWith("." + Domain, StringComparison.Ordinal));
        }
    }
}
