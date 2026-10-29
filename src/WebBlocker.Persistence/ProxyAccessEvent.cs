using System;

namespace WebBlocker.Persistence
{
    public enum ProxyAccessResult
    {
        AllowedByPolicy = 0,
        BlockedByPolicy = 1,
        BlockPageFailure = 2
    }

    public sealed class ProxyAccessEvent
    {
        public ProxyAccessEvent(
            Guid eventId,
            DateTime occurredAtUtc,
            string domain,
            string method,
            ProxyAccessResult result,
            Guid? matchedRuleId,
            long policyVersion)
        {
            if (eventId == Guid.Empty)
            {
                throw new ArgumentException("An event ID is required.", "eventId");
            }

            if (occurredAtUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("The event timestamp must be UTC.", "occurredAtUtc");
            }

            if (string.IsNullOrWhiteSpace(domain))
            {
                throw new ArgumentException("An event domain is required.", "domain");
            }

            if (result != ProxyAccessResult.AllowedByPolicy &&
                result != ProxyAccessResult.BlockedByPolicy &&
                result != ProxyAccessResult.BlockPageFailure)
            {
                throw new ArgumentOutOfRangeException("result");
            }

            if (policyVersion < 1)
            {
                throw new ArgumentOutOfRangeException("policyVersion");
            }

            EventId = eventId;
            OccurredAtUtc = occurredAtUtc;
            Domain = WebBlocker.Shared.DomainName.Normalize(domain);
            Method = NormalizeMethod(method);
            Result = result;
            MatchedRuleId = matchedRuleId;
            PolicyVersion = policyVersion;
        }

        public Guid EventId { get; private set; }

        public DateTime OccurredAtUtc { get; private set; }

        public string Domain { get; private set; }

        public string Method { get; private set; }

        public ProxyAccessResult Result { get; private set; }

        public Guid? MatchedRuleId { get; private set; }

        public long PolicyVersion { get; private set; }

        public bool IsBlocked
        {
            get { return Result != ProxyAccessResult.AllowedByPolicy; }
        }

        private static string NormalizeMethod(string method)
        {
            if (string.IsNullOrWhiteSpace(method) || method.Length > 32)
            {
                throw new ArgumentException("An HTTP method of 1 to 32 characters is required.", "method");
            }

            foreach (var character in method)
            {
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'A' && character <= 'Z') ||
                      (character >= 'a' && character <= 'z') ||
                      "!#$%&'*+-.^_`|~".IndexOf(character) >= 0))
                {
                    throw new ArgumentException("The HTTP method contains an invalid character.", "method");
                }
            }

            return method.ToUpperInvariant();
        }
    }
}
