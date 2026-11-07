using System;
using WebBlocker.Shared;

namespace WebBlocker.Persistence
{
    /// <summary>
    /// An access event received from a paired endpoint over the LAN. It keeps the endpoint identity so
    /// records from different devices can never be mixed, and keeps the endpoint's own event id so a
    /// repeated synchronization is idempotent.
    /// </summary>
    public sealed class RemoteAccessEvent
    {
        public RemoteAccessEvent(
            string deviceId,
            Guid eventId,
            DateTime occurredAtUtc,
            string domain,
            string method,
            ProxyAccessResult result,
            string requestedUrl,
            int? destinationPort)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 64)
            {
                throw new ArgumentException("A device id of 1 to 64 characters is required.", "deviceId");
            }

            if (eventId == Guid.Empty)
            {
                throw new ArgumentException("An event ID is required.", "eventId");
            }

            if (occurredAtUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("The event timestamp must be UTC.", "occurredAtUtc");
            }

            if (!ProxyAccessEvent.IsKnownResult(result))
            {
                throw new ArgumentOutOfRangeException("result");
            }

            if (destinationPort.HasValue && (destinationPort.Value < 1 || destinationPort.Value > 65535))
            {
                throw new ArgumentOutOfRangeException("destinationPort");
            }

            DeviceId = deviceId.Trim();
            EventId = eventId;
            OccurredAtUtc = occurredAtUtc;
            Domain = DomainName.Normalize(domain);
            Method = NormalizeMethod(method);
            Result = result;
            RequestedUrl = NormalizeRequestedUrl(requestedUrl);
            DestinationPort = destinationPort;
        }

        public string DeviceId { get; private set; }

        public Guid EventId { get; private set; }

        public DateTime OccurredAtUtc { get; private set; }

        public string Domain { get; private set; }

        public string Method { get; private set; }

        public ProxyAccessResult Result { get; private set; }

        public string RequestedUrl { get; private set; }

        public int? DestinationPort { get; private set; }

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
                throw new ArgumentException(
                    "The requested URL must be a valid HTTP URL without credentials or a fragment.", "requestedUrl");
            }

            return requestedUrl;
        }
    }
}
