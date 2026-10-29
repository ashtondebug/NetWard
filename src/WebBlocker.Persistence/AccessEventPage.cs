using System.Collections.Generic;

namespace WebBlocker.Persistence
{
    public sealed class AccessEventPage
    {
        internal AccessEventPage(IList<ProxyAccessEvent> events, long totalCount)
        {
            Events = events;
            TotalCount = totalCount;
        }

        public IList<ProxyAccessEvent> Events { get; private set; }

        public long TotalCount { get; private set; }
    }
}
