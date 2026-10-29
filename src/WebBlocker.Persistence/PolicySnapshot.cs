using WebBlocker.Shared;

namespace WebBlocker.Persistence
{
    public sealed class PolicySnapshot
    {
        internal PolicySnapshot(AccessPolicy policy, long version)
        {
            Policy = policy;
            Version = version;
        }

        public AccessPolicy Policy { get; private set; }

        public long Version { get; private set; }
    }
}
