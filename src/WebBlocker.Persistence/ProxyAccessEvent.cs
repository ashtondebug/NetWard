using System;

namespace WebBlocker.Persistence
{
    public enum ProxyAccessResult
    {
        AllowedByPolicy = 0,
        BlockedByPolicy = 1,
        BlockPageFailure = 2,
        BlockedByTimeLimit = 3
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
            long policyVersion,
            string requestedUrl = null,
            int? destinationPort = null)
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
                result != ProxyAccessResult.BlockPageFailure &&
                result != ProxyAccessResult.BlockedByTimeLimit)
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
            RequestedUrl = NormalizeRequestedUrl(requestedUrl);
            if (destinationPort.HasValue &&
                (destinationPort.Value < 1 || destinationPort.Value > 65535))
            {
                throw new ArgumentOutOfRangeException("destinationPort");
            }

            DestinationPort = destinationPort;
        }

        public Guid EventId { get; private set; }

        public DateTime OccurredAtUtc { get; private set; }

        public string Domain { get; private set; }

        public string Method { get; private set; }

        public ProxyAccessResult Result { get; private set; }

        public Guid? MatchedRuleId { get; private set; }

        public long PolicyVersion { get; private set; }

        public string RequestedUrl { get; private set; }

        public int? DestinationPort { get; private set; }

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

        private static string NormalizeRequestedUrl(string requestedUrl)
        {
            if (requestedUrl == null)
            {
                return null;
            }

            Uri uri;
            if (requestedUrl.Length > 32768 ||
                !Uri.TryCreate(requestedUrl, UriKind.Absolute, out uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.UserInfo.Length != 0 ||
                uri.Fragment.Length != 0)
            {
                throw new ArgumentException("The requested URL must be a valid HTTP URL without credentials or a fragment.", "requestedUrl");
            }

            return requestedUrl;
        }
    }
}
