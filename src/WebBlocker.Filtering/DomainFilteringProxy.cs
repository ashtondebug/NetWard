using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebBlocker.Persistence;
using WebBlocker.Shared;

namespace WebBlocker.Filtering
{
    public sealed class ProxyErrorEventArgs : EventArgs
    {
        internal ProxyErrorEventArgs(Exception exception)
        {
            Exception = exception;
        }

        public Exception Exception { get; private set; }
    }

    public sealed class DomainFilteringProxy : IDisposable
    {
        private const int MaximumHeaderBytes = 32768;
        private const int MaximumBodyBytes = 16 * 1024 * 1024;
        private const int MaximumConcurrentClients = 32;
        private static readonly TimeSpan ClientTimeout = TimeSpan.FromMinutes(5);

        private readonly AccessPolicy _policy;
        private readonly AccessEventStore _eventStore;
        private readonly long _policyVersion;
        private readonly IProxyDnsResolver _dnsResolver;
        private readonly string _blockedPagePath;
        private readonly string _timeoutPagePath;
        private readonly Func<DateTime> _localClock;
        private readonly SemaphoreSlim _clientSlots;
        private readonly ConcurrentDictionary<TcpClient, Task> _connections =
            new ConcurrentDictionary<TcpClient, Task>();
        private readonly CancellationTokenSource _stopToken = new CancellationTokenSource();
        private TcpListener _listener;
        private Task _acceptTask;
        private bool _disposed;

        public DomainFilteringProxy(
            AccessPolicy policy,
            int port,
            IProxyDnsResolver dnsResolver = null,
            string blockedPagePath = null,
            int maximumConcurrentClients = MaximumConcurrentClients,
            AccessEventStore eventStore = null,
            long policyVersion = 1,
            string timeoutPagePath = null,
            Func<DateTime> localClock = null)
        {
            if (policy == null)
            {
                throw new ArgumentNullException("policy");
            }

            if (port < 0 || port > 65535)
            {
                throw new ArgumentOutOfRangeException("port");
            }

            if (maximumConcurrentClients < 1 || maximumConcurrentClients > 1024)
            {
                throw new ArgumentOutOfRangeException("maximumConcurrentClients");
            }

            if (eventStore == null)
            {
                throw new ArgumentNullException("eventStore", "Proxy access events must be durably stored.");
            }

            if (policyVersion < 1)
            {
                throw new ArgumentOutOfRangeException("policyVersion");
            }

            _policy = policy;
            _eventStore = eventStore;
            _policyVersion = policyVersion;
            _dnsResolver = dnsResolver ?? new SystemProxyDnsResolver();
            _blockedPagePath = blockedPagePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index.html");
            _timeoutPagePath = timeoutPagePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "timeout.html");
            _localClock = localClock ?? (() => DateTime.Now);
            _clientSlots = new SemaphoreSlim(maximumConcurrentClients, maximumConcurrentClients);
            Port = port;
        }

        public event EventHandler<ProxyErrorEventArgs> ClientError;

        public int Port { get; private set; }

        public bool IsRunning
        {
            get { return _listener != null && _acceptTask != null && !_acceptTask.IsCompleted; }
        }

        public void Start()
        {
            ThrowIfDisposed();
            if (_listener != null)
            {
                throw new InvalidOperationException("The proxy has already been started.");
            }

            var listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Start();
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _acceptTask = AcceptClientsAsync();
        }

        public async Task StopAsync()
        {
            if (_listener == null)
            {
                return;
            }

            _stopToken.Cancel();
            _listener.Stop();
            foreach (var connection in _connections.Keys)
            {
                connection.Close();
            }

            try
            {
                await _acceptTask.ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
            catch (OperationCanceledException)
            {
            }

            var activeTasks = _connections.Values.ToArray();
            if (activeTasks.Length > 0)
            {
                await Task.WhenAll(activeTasks).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            StopAsync().GetAwaiter().GetResult();
            _clientSlots.Dispose();
            _stopToken.Dispose();
            _disposed = true;
        }

        private async Task AcceptClientsAsync()
        {
            while (!_stopToken.IsCancellationRequested)
            {
                try
                {
                    await _clientSlots.WaitAsync(_stopToken.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (_stopToken.IsCancellationRequested)
                    {
                        break;
                    }

                    throw;
                }

                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    _clientSlots.Release();
                    break;
                }
                catch (SocketException)
                {
                    _clientSlots.Release();
                    if (_stopToken.IsCancellationRequested)
                    {
                        break;
                    }

                    throw;
                }

                client.ReceiveTimeout = (int)ClientTimeout.TotalMilliseconds;
                client.SendTimeout = (int)ClientTimeout.TotalMilliseconds;
                var startSignal = new TaskCompletionSource<bool>();
                var task = HandleClientAndReleaseAsync(client, _stopToken.Token, startSignal.Task);
                if (!_connections.TryAdd(client, task))
                {
                    client.Close();
                    _clientSlots.Release();
                    ReportError(new InvalidOperationException("A duplicate proxy client was accepted."));
                    continue;
                }

                startSignal.SetResult(true);
            }
        }

        private async Task HandleClientAndReleaseAsync(
            TcpClient client,
            CancellationToken cancellationToken,
            Task startSignal)
        {
            using (var clientTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            try
            {
                clientTimeout.CancelAfter(ClientTimeout);
                await startSignal.ConfigureAwait(false);
                await HandleClientAsync(client, clientTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            catch (Exception exception)
            {
                ReportError(exception);
                TryWriteErrorResponse(client, 500, "Internal Server Error");
            }
            finally
            {
                client.Close();
                Task ignored;
                _connections.TryRemove(client, out ignored);
                _clientSlots.Release();
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var reader = new BufferedNetworkReader(client.GetStream());
            byte[] rawHeaders;
            try
            {
                rawHeaders = await reader.ReadHeadersAsync(MaximumHeaderBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                await WriteTextResponseAsync(client.GetStream(), 431, "Request Header Fields Too Large", cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            ProxyRequest request;
            try
            {
                request = ProxyRequest.Parse(rawHeaders, MaximumBodyBytes);
            }
            catch (NotSupportedException)
            {
                await WriteTextResponseAsync(client.GetStream(), 501, "Not Implemented", cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (InvalidDataException)
            {
                await WriteTextResponseAsync(client.GetStream(), 400, "Bad Request", cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var decision = PolicyEvaluator.Evaluate(_policy, request.Host);
            var timedRule = decision.IsAllowed
                ? PolicyEvaluator.FindTimedAccessRule(_policy, request.Host)
                : null;
            TimedAccessWindow timedWindow = null;
            if (timedRule != null)
            {
                var category = _policy.TimedAccessCategories.FirstOrDefault(item =>
                    string.Equals(item.Name, timedRule.Category, StringComparison.OrdinalIgnoreCase));
                if (category == null)
                {
                    throw new InvalidDataException("A timed-access rule references a missing category.");
                }

                timedWindow = _eventStore.AuthorizeTimedAccess(category, _localClock());
            }

            var eventId = Guid.NewGuid();
            var isAllowed = decision.IsAllowed && (timedWindow == null || timedWindow.IsAllowed);
            var result = isAllowed
                ? ProxyAccessResult.AllowedByPolicy
                : timedWindow != null && !timedWindow.IsAllowed
                    ? ProxyAccessResult.BlockedByTimeLimit
                    : ProxyAccessResult.BlockedByPolicy;
            var matchedRuleId = result == ProxyAccessResult.BlockedByTimeLimit
                ? (Guid?)timedRule.Id
                : decision.MatchedRule == null ? (Guid?)null : decision.MatchedRule.Id;
            _eventStore.RecordAccessEvent(new ProxyAccessEvent(
                eventId,
                DateTime.UtcNow,
                request.Host,
                request.Method,
                result,
                matchedRuleId,
                _policyVersion,
                request.RequestedUrl,
                request.Port));

            if (!isAllowed)
            {
                if (request.IsConnect)
                {
                    await WriteTextResponseAsync(client.GetStream(), 403, "Forbidden", cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    var pagePath = result == ProxyAccessResult.BlockedByTimeLimit
                        ? _timeoutPagePath
                        : _blockedPagePath;
                    await WriteBlockedPageAsync(client.GetStream(), eventId, pagePath, cancellationToken)
                        .ConfigureAwait(false);
                }

                return;
            }

            await ForwardAllowedRequestAsync(
                client,
                reader,
                request,
                timedWindow == null ? (DateTime?)null : timedWindow.ExpiresAtUtc,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task ForwardAllowedRequestAsync(
            TcpClient client,
            BufferedNetworkReader reader,
            ProxyRequest request,
            DateTime? timedExpiryUtc,
            CancellationToken cancellationToken)
        {
            CancellationToken requestToken = cancellationToken;
            CancellationTokenSource timedDeadline = null;
            if (timedExpiryUtc.HasValue)
            {
                timedDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var remaining = timedExpiryUtc.Value - _localClock().ToUniversalTime();
                if (remaining <= TimeSpan.Zero)
                {
                    timedDeadline.Cancel();
                }
                else
                {
                    timedDeadline.CancelAfter(remaining);
                }

                requestToken = timedDeadline.Token;
            }

            using (timedDeadline)
            {
                TcpClient upstream;
                try
                {
                    upstream = await ConnectUpstreamAsync(request.Host, request.Port, requestToken)
                        .ConfigureAwait(false);
                }
                catch (SocketException exception)
                {
                    ReportError(exception);
                    await WriteTextResponseAsync(client.GetStream(), 502, "Bad Gateway", requestToken)
                        .ConfigureAwait(false);
                    return;
                }
                catch (TimeoutException exception)
                {
                    ReportError(exception);
                    await WriteTextResponseAsync(client.GetStream(), 502, "Bad Gateway", requestToken)
                        .ConfigureAwait(false);
                    return;
                }

                using (upstream)
                using (requestToken.Register(() =>
                {
                    client.Close();
                    upstream.Close();
                }))
                {
                    if (request.IsConnect)
                    {
                        await WriteRawAsync(client.GetStream(), "HTTP/1.1 200 Connection Established\r\n\r\n",
                            requestToken).ConfigureAwait(false);
                        await reader.WriteBufferedToAsync(upstream.GetStream(), requestToken).ConfigureAwait(false);
                        await TunnelAsync(client, upstream, requestToken).ConfigureAwait(false);
                        return;
                    }

                    byte[] body = request.HasBody
                        ? await reader.ReadExactlyAsync(request.ContentLength, requestToken).ConfigureAwait(false)
                        : new byte[0];
                    var upstreamStream = upstream.GetStream();
                    await WriteRawAsync(upstreamStream, request.RewrittenHeaders, requestToken).ConfigureAwait(false);
                    if (body.Length > 0)
                    {
                        await upstreamStream.WriteAsync(body, 0, body.Length, requestToken).ConfigureAwait(false);
                    }

                    await upstreamStream.FlushAsync(requestToken).ConfigureAwait(false);
                    await upstreamStream.CopyToAsync(client.GetStream(), 81920, requestToken).ConfigureAwait(false);
                }
            }
        }

        private async Task<TcpClient> ConnectUpstreamAsync(
            string host,
            int port,
            CancellationToken cancellationToken)
        {
            var addresses = await _dnsResolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
            if (addresses == null || addresses.Length == 0)
            {
                throw new SocketException((int)SocketError.HostNotFound);
            }

            Exception lastError = null;
            foreach (var address in addresses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var client = new TcpClient(address.AddressFamily);
                try
                {
                    var connectTask = client.ConnectAsync(address, port);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    if (await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false) != connectTask)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new TimeoutException("Connecting to the destination exceeded 10 seconds.");
                    }

                    await connectTask.ConfigureAwait(false);
                    client.ReceiveTimeout = (int)ClientTimeout.TotalMilliseconds;
                    client.SendTimeout = (int)ClientTimeout.TotalMilliseconds;
                    client.NoDelay = true;
                    return client;
                }
                catch (SocketException exception)
                {
                    client.Close();
                    lastError = exception;
                }
                catch (OperationCanceledException)
                {
                    client.Close();
                    throw;
                }
                catch (TimeoutException exception)
                {
                    client.Close();
                    lastError = exception;
                }
            }

            throw lastError ?? new SocketException((int)SocketError.HostUnreachable);
        }

        private static async Task TunnelAsync(
            TcpClient client,
            TcpClient upstream,
            CancellationToken cancellationToken)
        {
            var clientToUpstream = PumpAsync(
                client.GetStream(), upstream.GetStream(), upstream.Client, cancellationToken);
            var upstreamToClient = PumpAsync(
                upstream.GetStream(), client.GetStream(), client.Client, cancellationToken);
            await Task.WhenAll(clientToUpstream, upstreamToClient).ConfigureAwait(false);
        }

        private static async Task PumpAsync(
            Stream source,
            Stream destination,
            Socket destinationSocket,
            CancellationToken cancellationToken)
        {
            try
            {
                await source.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            finally
            {
                try
                {
                    destinationSocket.Shutdown(SocketShutdown.Send);
                }
                catch (SocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private async Task WriteBlockedPageAsync(
            NetworkStream stream,
            Guid eventId,
            string pagePath,
            CancellationToken cancellationToken)
        {
            byte[] body;
            try
            {
                body = File.ReadAllBytes(pagePath);
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException)
            {
                ReportError(new IOException("The local filtering page file could not be read.", exception));
                _eventStore.UpdateAccessEventResult(eventId, ProxyAccessResult.BlockPageFailure);
                await WriteTextResponseAsync(stream, 503, "Filtering Error", cancellationToken).ConfigureAwait(false);
                return;
            }

            var headers = string.Format(
                "HTTP/1.1 403 Forbidden\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {0}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n",
                body.Length);
            await WriteRawAsync(stream, headers, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task WriteTextResponseAsync(
            NetworkStream stream,
            int statusCode,
            string reason,
            CancellationToken cancellationToken)
        {
            var body = Encoding.UTF8.GetBytes(statusCode + " " + reason + "\r\n");
            var headers = string.Format(
                "HTTP/1.1 {0} {1}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {2}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n",
                statusCode, reason, body.Length);
            await WriteRawAsync(stream, headers, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task WriteRawAsync(
            NetworkStream stream,
            string value,
            CancellationToken cancellationToken)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
        }

        private void ReportError(Exception exception)
        {
            Trace.TraceError("NetWard proxy error: {0}", exception);
            var handler = ClientError;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, new ProxyErrorEventArgs(exception));
            }
            catch (Exception subscriberException)
            {
                Trace.TraceError("NetWard proxy error subscriber failed: {0}", subscriberException);
            }
        }

        private static void TryWriteErrorResponse(TcpClient client, int statusCode, string reason)
        {
            if (!client.Connected)
            {
                return;
            }

            try
            {
                WriteTextResponseAsync(client.GetStream(), statusCode, reason, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Trace.TraceError("NetWard proxy could not send an error response: {0}", exception);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}
