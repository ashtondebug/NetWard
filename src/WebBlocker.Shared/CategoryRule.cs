using System;

namespace WebBlocker.Shared
{
    /// <summary>
    /// Maps one normalized domain (optionally including its subdomains) to a blocked-content category.
    /// A matching enabled category rule blocks the request and selects the violation notice.
    /// </summary>
    public sealed class CategoryRule
    {
        public CategoryRule(Guid id, string category, string domain, bool includeSubdomains, bool isEnabled)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException("A category rule ID is required.", "id");
            }

            if (string.IsNullOrWhiteSpace(category))
            {
                throw new ArgumentException("A category rule must reference a category.", "category");
            }

            var normalizedCategory = category.Trim();
            if (normalizedCategory.Length > 64)
            {
                throw new ArgumentException("A category name cannot exceed 64 characters.", "category");
            }

            foreach (var character in normalizedCategory)
            {
                if (char.IsControl(character))
                {
                    throw new ArgumentException("A category name cannot contain control characters.", "category");
                }
            }

            Id = id;
            Category = normalizedCategory;
            Domain = DomainName.Normalize(domain);
            IncludeSubdomains = includeSubdomains;
            IsEnabled = isEnabled;
        }

        public Guid Id { get; private set; }

        public string Category { get; private set; }

        public string Domain { get; private set; }

        public bool IncludeSubdomains { get; private set; }

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