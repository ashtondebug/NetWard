using System;

namespace WebBlocker.Persistence
{
    public sealed class TimedAccessWindow
    {
        internal TimedAccessWindow(string category, DateTime expiresAtUtc, bool isAllowed)
        {
            Category = category;
            ExpiresAtUtc = expiresAtUtc;
            IsAllowed = isAllowed;
        }

        public string Category { get; private set; }

        public DateTime ExpiresAtUtc { get; private set; }

        public bool IsAllowed { get; private set; }
    }
}
