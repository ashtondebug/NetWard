using System;

namespace WebBlocker.Shared
{
    public sealed class TimedAccessRule
    {
        public TimedAccessRule(
            Guid id,
            string domain,
            bool includeSubdomains,
            string category,
            bool isEnabled)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException("A timed-access rule ID is required.", "id");
            }

            if (string.IsNullOrWhiteSpace(category))
            {
                throw new ArgumentException("A timed-access category is required.", "category");
            }

            var normalizedCategory = category.Trim();
            if (normalizedCategory.Length > 64)
            {
                throw new ArgumentException("A timed-access category name cannot exceed 64 characters.", "category");
            }

            foreach (var character in normalizedCategory)
            {
                if (char.IsControl(character))
                {
                    throw new ArgumentException("A timed-access category name cannot contain control characters.", "category");
                }
            }

            Id = id;
            Domain = DomainName.Normalize(domain);
            IncludeSubdomains = includeSubdomains;
            Category = normalizedCategory;
            IsEnabled = isEnabled;
        }

        public Guid Id { get; private set; }

        public string Domain { get; private set; }

        public bool IncludeSubdomains { get; private set; }

        public string Category { get; private set; }

        public bool IsEnabled { get; private set; }

        internal bool MatchesNormalizedHost(string normalizedHost)
        {
            return IsEnabled &&
                   (string.Equals(normalizedHost, Domain, StringComparison.Ordinal) ||
                    (IncludeSubdomains &&
                     normalizedHost.EndsWith("." + Domain, StringComparison.Ordinal)));
        }
    }
}
