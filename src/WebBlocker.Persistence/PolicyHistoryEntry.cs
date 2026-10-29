using System;
using WebBlocker.Shared;

namespace WebBlocker.Persistence
{
    public sealed class PolicyHistoryEntry
    {
        internal PolicyHistoryEntry(long version, AccessMode mode, DateTime updatedAtUtc, int ruleCount)
        {
            Version = version;
            Mode = mode;
            UpdatedAtUtc = updatedAtUtc;
            RuleCount = ruleCount;
        }

        public long Version { get; private set; }

        public AccessMode Mode { get; private set; }

        public DateTime UpdatedAtUtc { get; private set; }

        public int RuleCount { get; private set; }
    }
}
