using System;
using System.Collections.Generic;

namespace NetWard.Lan
{
    public static class LanErrorCodes
    {
        public const string BadRequest = "bad-request";
        public const string Unauthorized = "unauthorized";
        public const string InvalidCode = "invalid-code";
        public const string CodeExpired = "code-expired";
        public const string TooManyAttempts = "too-many-attempts";
        public const string AlreadyPaired = "already-paired";
        public const string PolicyRejected = "policy-rejected";
        public const string NotSupported = "not-supported";
        public const string Internal = "internal";
    }

    /// <summary>
    /// A protocol-level failure. The message is safe to show to the administrator; it never contains
    /// pairing codes, key material or remote stack traces.
    /// </summary>
    public sealed class LanProtocolException : Exception
    {
        public LanProtocolException(string code, string message)
            : base(message)
        {
            Code = code ?? LanErrorCodes.Internal;
        }

        public LanProtocolException(string code, string message, Exception innerException)
            : base(message, innerException)
        {
            Code = code ?? LanErrorCodes.Internal;
        }

        public string Code { get; private set; }
    }

    public static class LanRequestTypes
    {
        public const string Ping = "ping";
        public const string Pair = "pair";
        public const string Status = "status";
        public const string PushPolicy = "push-policy";
        public const string PullEvents = "pull-events";
        public const string Unpair = "unpair";
    }

    /// <summary>
    /// One request per connection. <see cref="RequestId"/> is the idempotency key: a repeated id returns
    /// the response that was produced the first time instead of running the operation twice.
    /// </summary>
    public sealed class LanRequest
    {
        public string RequestId { get; set; }

        public string Type { get; set; }

        public PairPayload Pair { get; set; }

        public EventQueryPayload EventsQuery { get; set; }

        public string PolicyJson { get; set; }

        public string TargetId { get; set; }
    }

    public sealed class LanResponse
    {
        public string RequestId { get; set; }

        public bool Ok { get; set; }

        public string Error { get; set; }

        public string Message { get; set; }

        public PairResultPayload Pair { get; set; }

        public StatusPayload Status { get; set; }

        public PolicyResultPayload Policy { get; set; }

        public EventsPayload Events { get; set; }

        public static LanResponse Success(string requestId)
        {
            return new LanResponse { RequestId = requestId, Ok = true };
        }

        public static LanResponse Failure(string requestId, string code, string message)
        {
            return new LanResponse
            {
                RequestId = requestId,
                Ok = false,
                Error = string.IsNullOrEmpty(code) ? LanErrorCodes.Internal : code,
                Message = message ?? string.Empty
            };
        }
    }

    /// <summary>Sent by the administrator to the endpoint during pairing.</summary>
    public sealed class PairPayload
    {
        public string Code { get; set; }

        /// <summary>Stable administrator identity (the administrator device id).</summary>
        public string AdminId { get; set; }

        public string AdminName { get; set; }
    }

    public sealed class PairResultPayload
    {
        public string DeviceId { get; set; }

        public string DeviceName { get; set; }

        public string Fingerprint { get; set; }

        /// <summary>Certificate thumbprint the administrator must pin for every later session.</summary>
        public string Thumbprint { get; set; }

        public string PairedAtUtc { get; set; }
    }

    public sealed class StatusPayload
    {
        public string DeviceId { get; set; }

        public string DeviceName { get; set; }

        public string Fingerprint { get; set; }

        public string PolicyMode { get; set; }

        public long PolicyVersion { get; set; }

        public int Rules { get; set; }

        public int Categories { get; set; }

        public int CategoryRules { get; set; }

        public long Events { get; set; }

        public bool ProxyRunning { get; set; }

        public long UptimeSeconds { get; set; }

        public int PairedAdmins { get; set; }

        public string AgentVersion { get; set; }
    }

    public sealed class PolicyResultPayload
    {
        public long Version { get; set; }

        public int Rules { get; set; }
    }

    public sealed class EventQueryPayload
    {
        /// <summary>ISO-8601 UTC timestamp; events strictly newer than this value are returned.</summary>
        public string SinceUtc { get; set; }

        public int Limit { get; set; }
    }

    public sealed class EventsPayload
    {
        public List<LanEvent> Items { get; set; }

        /// <summary>True when the endpoint has more matching events than this response returned.</summary>
        public bool Truncated { get; set; }
    }

    /// <summary>One access event as exchanged between devices. Mirrors the engine's event JSON.</summary>
    public sealed class LanEvent
    {
        public string Id { get; set; }

        public string OccurredAtUtc { get; set; }

        public string Domain { get; set; }

        public string Method { get; set; }

        public int Result { get; set; }

        public string RequestedUrl { get; set; }

        public int Port { get; set; }
    }
}
