using System;

namespace WebBlocker.Shared
{
    public sealed class TimedAccessCategory
    {
        public TimedAccessCategory(string name, int timeLimitMinutes)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A timed-access category name is required.", "name");
            }

            var normalizedName = name.Trim();
            if (normalizedName.Length > 64)
            {
                throw new ArgumentException("A timed-access category name cannot exceed 64 characters.", "name");
            }

            foreach (var character in normalizedName)
            {
                if (char.IsControl(character))
                {
                    throw new ArgumentException("A timed-access category name cannot contain control characters.", "name");
                }
            }

            if (timeLimitMinutes < 1 || timeLimitMinutes > 1440)
            {
                throw new ArgumentOutOfRangeException(
                    "timeLimitMinutes", "A daily timed-access limit must be between 1 and 1440 minutes.");
            }

            Name = normalizedName;
            TimeLimitMinutes = timeLimitMinutes;
        }

        public string Name { get; private set; }

        public int TimeLimitMinutes { get; private set; }
    }
}
