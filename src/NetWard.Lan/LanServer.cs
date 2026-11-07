using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetWard.Lan
{
    /// <summary>Session log entry surfaced to the endpoint UI; never contains codes or key material.</summary>
    public sealed class LanSessionEventArgs : EventArgs
    {
        public LanSessionEventArgs(string requestType, string administrator, bool success, string detail, DateTime occurredAtUtc)
        {
            RequestType = requestType;
            Administrator = administrator;
            Success = success;
            Detail = detail;
            OccurredAtUtc = occurredAtUtc;
        }

        public string RequestType { get; private set; }

        public string Administrator { get; private set; }

        public bool Success { get; private set; }

        public string Detail { get; private set; }

        public DateTime OccurredAtUtc { get; private set; }
    }

    /// <summary>Endpoint-side operations the listener performs on behalf of a paired administrator.</summary>
    public interface ILanAgentHandler
    {
        string DeviceName { get; }

        StatusPayload GetStatus();

        PolicyResultPayload ApplyPolicy(string policyJson);

        EventsPayload GetEvents(string sinceUtc, int limit);
    }

    /// <summary>
    /// LAN listener on the endpoint. Every connection is a mutually authenticated TLS session: the
    /// endpoint presents its own certificate and requires a client certificate whose thumbprint belongs
    /// to a completed pairing. One request per connection keeps failure handling simple.
    /// </summary>
    public sealed class LanServer : IDisposable
    {
        public const int DefaultPort = 18770;
        private const int MaximumConcurrentConnections = 16;
        private const int MaximumCachedResponses = 128;

        private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

        private readonly object _sync = new object();
        private readonly DeviceIdentity _identity;
        private readonly AgentLanStore _store;
        private readonly ILanAgentHandler _handler;
        private readonly SemaphoreSlim _slots = new SemaphoreSlim(MaximumConcurrentConnections, MaximumConcurrentConnections);
        private readonly ConcurrentDictionary<string, LanResponse> _responseCache =
            new ConcurrentDictionary<string, LanResponse>(StringComparer.Ordinal);
        private TcpListener _listener;
        private CancellationTokenSource _stop;
        private Task _acceptTask;

        public LanServer(int port, DeviceIdentity identity, AgentLanStore store, ILanAgentHandler handler)
        {
            if (port < 0 || port > 65535)
            {
                throw new ArgumentOutOfRangeException("port");
            }

            if (identity == null)
            {
                throw new ArgumentNullException("identity");
            }

            if (store == null)
            {
                throw new ArgumentNullException("store");
            }

            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }

            Port = port;
            _identity = identity;
            _store = store;
            _handler = handler;
        }

        public event EventHandler<LanSessionEventArgs> SessionHandled;

        public int Port { get; private set; }

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _listener != null;
                }
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_listener != null)
                {
                    throw new InvalidOperationException("The LAN listener has already been started.");
                }

                var listener = new TcpListener(IPAddress.Any, Port);
                listener.Start();
                _listener = listener;
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _stop = new CancellationTokenSource();
                _acceptTask = AcceptLoopAsync(listener, _stop.Token);
            }
        }

        public void Stop()
        {
            TcpListener listener;
            CancellationTokenSource stop;
            Task acceptTask;
            lock (_sync)
            {
                listener = _listener;
                stop = _stop;
                acceptTask = _acceptTask;
                _listener = null;
                _stop = null;
                _acceptTask = null;
            }

            if (listener == null)
            {
                return;
            }

            try
            {
                stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                listener.Stop();
            }
            catch (SocketException)
            {
            }

            try
            {
                acceptTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    continue;
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                // Fire and forget: each session is independent and bounded by the semaphore below.
                var ignored = HandleClientAsync(client, cancellationToken);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var acquired = false;
            try
            {
                await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired = true;
            }
            catch (OperationCanceledException)
            {
                client.Close();
                return;
            }

            string requestType = null;
            try
            {
                using (client)
                {
                    client.ReceiveTimeout = (int)RequestTimeout.TotalMilliseconds;
                    client.SendTimeout = (int)RequestTimeout.TotalMilliseconds;

                    var ssl = await HandshakeAsync(client, cancellationToken).ConfigureAwait(false);
                    if (ssl == null)
                    {
                        return;
                    }

                    using (ssl)
                    {
                        try
                        {
                            var requestJson = await LanFraming.ReadMessageAsync(ssl, cancellationToken)
                                .ConfigureAwait(false);
                            if (requestJson == null)
                            {
                                return;
                            }

                            var request = ParseRequest(requestJson);
                            requestType = request.Type;
                            var response = ProcessRequest(request, ssl.RemoteCertificate, cancellationToken);
                            await LanFraming.WriteMessageAsync(ssl, LanJson.Serialize(response), cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (LanProtocolException protocolError)
                        {
                            // The failure is written back over the same TLS session so the administrator
                            // sees a protocol error instead of a bare connection reset.
                            await TryWriteFailureAsync(ssl, protocolError).ConfigureAwait(false);
                            RaiseSession(requestType, null, false, protocolError.Message);
                        }
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                RaiseSession(requestType, null, false, "会话处理失败：" + exception.Message);
            }
            finally
            {
                client.Close();
                if (acquired)
                {
                    _slots.Release();
                }
            }
        }

        private async Task<SslStream> HandshakeAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var ssl = new SslStream(client.GetStream(), false, AcceptAnyRemoteCertificate);
            var handshake = ssl.AuthenticateAsServerAsync(_identity.Certificate, true, SslProtocols.Tls12, false);
            var completed = await Task.WhenAny(handshake, Task.Delay(HandshakeTimeout)).ConfigureAwait(false);
            if (completed != handshake)
            {
                ssl.Dispose();
                return null;
            }

            await handshake.ConfigureAwait(false);
            if (ssl.RemoteCertificate == null)
            {
                ssl.Dispose();
                throw new LanProtocolException(LanErrorCodes.Unauthorized, "客户端未提供证书。");
            }

            return ssl;
        }

        private static LanRequest ParseRequest(string requestJson)
        {
            var request = LanJson.Deserialize<LanRequest>(requestJson);
            if (request == null || string.IsNullOrWhiteSpace(request.Type))
            {
                throw new LanProtocolException(LanErrorCodes.BadRequest, "缺少消息类型。");
            }

            return request;
        }

        private LanResponse ProcessRequest(LanRequest request, X509Certificate remoteCertificate, CancellationToken cancellationToken)
        {
            var requestId = NormalizeRequestId(request.RequestId);
            LanResponse cached;
            if (requestId != null && _responseCache.TryGetValue(requestId, out cached))
            {
                return cached;
            }

            var response = Dispatch(request, requestId, remoteCertificate, cancellationToken);
            if (requestId != null && IsIdempotent(request.Type))
            {
                if (_responseCache.Count >= MaximumCachedResponses)
                {
                    _responseCache.Clear();
                }

                _responseCache[requestId] = response;
            }

            return response;
        }

        private LanResponse Dispatch(LanRequest request, string requestId, X509Certificate remoteCertificate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var thumbprint = GetThumbprint(remoteCertificate);

            if (string.Equals(request.Type, LanRequestTypes.Pair, StringComparison.Ordinal))
            {
                return HandlePair(requestId, request.Pair, thumbprint);
            }

            var admin = _store.FindAdmin(thumbprint);
            if (admin == null)
            {
                throw new LanProtocolException(
                    LanErrorCodes.Unauthorized, "该管理端未与被控端配对，请先在管理端完成配对。");
            }

            if (string.Equals(request.Type, LanRequestTypes.Ping, StringComparison.Ordinal))
            {
                return LanResponse.Success(requestId);
            }

            if (string.Equals(request.Type, LanRequestTypes.Status, StringComparison.Ordinal))
            {
                var status = _handler.GetStatus();
                RaiseSession("status", admin.Name, true, "读取状态");
                return new LanResponse { RequestId = requestId, Ok = true, Status = status };
            }

            if (string.Equals(request.Type, LanRequestTypes.PushPolicy, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(request.PolicyJson))
                {
                    throw new LanProtocolException(LanErrorCodes.BadRequest, "缺少策略内容。");
                }

                var result = _handler.ApplyPolicy(request.PolicyJson);
                RaiseSession("push-policy", admin.Name, true,
                    "下发策略，版本 " + result.Version.ToString(CultureInfo.InvariantCulture));
                return new LanResponse { RequestId = requestId, Ok = true, Policy = result };
            }

            if (string.Equals(request.Type, LanRequestTypes.PullEvents, StringComparison.Ordinal))
            {
                var query = request.EventsQuery ?? new EventQueryPayload();
                var limit = query.Limit <= 0 ? 200 : query.Limit;
                if (limit > 500)
                {
                    limit = 500;
                }

                var events = _handler.GetEvents(query.SinceUtc, limit);
                var count = events == null || events.Items == null ? 0 : events.Items.Count;
                RaiseSession("pull-events", admin.Name, true, "同步日志 " + count + " 条");
                return new LanResponse { RequestId = requestId, Ok = true, Events = events };
            }

            if (string.Equals(request.Type, LanRequestTypes.Unpair, StringComparison.Ordinal))
            {
                _store.RemoveAdmin(admin.AdminId);
                RaiseSession("unpair", admin.Name, true, "解除配对");
                return LanResponse.Success(requestId);
            }

            throw new LanProtocolException(LanErrorCodes.NotSupported, "不支持的消息类型：" + request.Type);
        }

        private LanResponse HandlePair(string requestId, PairPayload payload, string thumbprint)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.Code))
            {
                throw new LanProtocolException(LanErrorCodes.BadRequest, "缺少配对码。");
            }

            var adminId = (payload.AdminId ?? string.Empty).Trim();
            if (adminId.Length == 0 || adminId.Length > 64)
            {
                throw new LanProtocolException(LanErrorCodes.BadRequest, "管理端标识无效。");
            }

            var adminName = NormalizeName(payload.AdminName);
            var admin = new PairedAdmin
            {
                AdminId = adminId,
                Name = adminName,
                Thumbprint = thumbprint
            };

            var outcome = _store.TryPair(payload.Code, admin, DateTime.UtcNow);
            switch (outcome)
            {
                case PairingOutcome.InvalidCode:
                    throw new LanProtocolException(LanErrorCodes.InvalidCode, "配对码不正确。");
                case PairingOutcome.ExpiredCode:
                    throw new LanProtocolException(LanErrorCodes.CodeExpired, "配对码已过期，请查看被控端当前显示的配对码。");
                case PairingOutcome.TooManyAttempts:
                    throw new LanProtocolException(LanErrorCodes.TooManyAttempts, "配对码尝试次数过多，已失效。");
            }

            var fingerprint = DeviceIdentity.FormatFingerprint(_identity.Certificate);
            RaiseSession("pair", adminName, true, outcome == PairingOutcome.AlreadyPaired ? "重复配对请求" : "完成配对");
            return new LanResponse
            {
                RequestId = requestId,
                Ok = true,
                Pair = new PairResultPayload
                {
                    DeviceId = _identity.DeviceId,
                    DeviceName = _handler.DeviceName,
                    Fingerprint = fingerprint,
                    Thumbprint = _identity.Thumbprint,
                    PairedAtUtc = string.IsNullOrEmpty(admin.PairedAtUtc)
                        ? DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                        : admin.PairedAtUtc
                }
            };
        }

        private static bool IsIdempotent(string requestType)
        {
            return string.Equals(requestType, LanRequestTypes.Pair, StringComparison.Ordinal) ||
                   string.Equals(requestType, LanRequestTypes.PushPolicy, StringComparison.Ordinal) ||
                   string.Equals(requestType, LanRequestTypes.Unpair, StringComparison.Ordinal);
        }

        private static string GetThumbprint(X509Certificate certificate)
        {
            if (certificate == null)
            {
                return null;
            }

            var certificate2 = certificate as X509Certificate2;
            return certificate2 != null ? certificate2.Thumbprint : certificate.GetCertHashString();
        }

        private static string NormalizeRequestId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length > 64 ? trimmed.Substring(0, 64) : trimmed;
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "管理端";
            }

            var builder = new StringBuilder(value.Length);
            foreach (var character in value.Trim())
            {
                if (!char.IsControl(character))
                {
                    builder.Append(character);
                }

                if (builder.Length >= 64)
                {
                    break;
                }
            }

            return builder.Length == 0 ? "管理端" : builder.ToString();
        }

        private static bool AcceptAnyRemoteCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            // Self-signed device certificates never chain to a trusted root. Authentication happens on
            // the application layer instead: the thumbprint must belong to a paired administrator.
            return true;
        }

        private static async Task TryWriteFailureAsync(Stream stream, LanProtocolException exception)
        {
            try
            {
                var response = LanResponse.Failure(null, exception.Code, exception.Message);
                await LanFraming.WriteMessageAsync(stream, LanJson.Serialize(response), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        private void RaiseSession(string requestType, string administrator, bool success, string detail)
        {
            var handler = SessionHandled;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, new LanSessionEventArgs(requestType, administrator, success, detail, DateTime.UtcNow));
            }
            catch (Exception)
            {
            }
        }

        public void Dispose()
        {
            Stop();
            _slots.Dispose();
        }
    }
}
