using System;

namespace WebBlocker.Shared
{
    /// <summary>
    /// A configured blocked-content category (for example adult or gambling content).
    /// Category membership is stored separately from base allowlist/blocklist rules so that a
    /// category match can show the dedicated violation notice instead of the normal block page.
    /// </summary>
    public sealed class CategoryConfig
    {
        public CategoryConfig(string name, string sourceName, bool isEnabled)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A category name is required.", "name");
            }

            var normalizedName = name.Trim();
            if (normalizedName.Length > 64)
            {
                throw new ArgumentException("A category name cannot exceed 64 characters.", "name");
            }

            foreach (var character in normalizedName)
            {
                if (char.IsControl(character))
                {
                    throw new ArgumentException("A category name cannot contain control characters.", "name");
                }
            }

            if (sourceName != null)
            {
                var normalizedSource = sourceName.Trim();
                if (normalizedSource.Length > 260)
                {
                    throw new ArgumentException("A category source reference cannot exceed 260 characters.", "sourceName");
                }

                foreach (var character in normalizedSource)
                {
                    if (char.IsControl(character))
                    {
                        throw new ArgumentException("A category source reference cannot contain control characters.", "sourceName");
                    }
                }

                SourceName = normalizedSource.Length == 0 ? null : normalizedSource;
            }

            Name = normalizedName;
            IsEnabled = isEnabled;
        }

        public string Name { get; private set; }

        /// <summary>
        /// Describes where the reviewed list came from (file name, version, date). Null when unknown.
        /// </summary>
        public string SourceName { get; private set; }

        public bool IsEnabled { get; private set; }

        public CategoryConfig WithEnabled(bool isEnabled)
        {
            return new CategoryConfig(Name, SourceName, isEnabled);
        }

        public CategoryConfig WithSource(string sourceName)
        {
            return new CategoryConfig(Name, sourceName, IsEnabled);
        }
    }
}