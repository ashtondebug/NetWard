using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebBlocker.Filtering;
using WebBlocker.Persistence;
using WebBlocker.Shared;

namespace WebBlocker.Shared.Tests
{
    internal static class ProxyIntegrationTests
    {
        public static async Task HttpProxyEnforcesPolicyAsync()
        {
            var origin = new TcpListener(IPAddress.Loopback, 0);
            origin.Start();
            var originPort = ((IPEndPoint)origin.LocalEndpoint).Port;

            var allowedRule = new DomainRule(Guid.NewGuid(), "allowed.example.test", true, true);
            var policy = new AccessPolicy(AccessMode.AllowList, new[] { allowedRule });
            using (var database = new TestDatabase())
            {
                var store = new AccessEventStore(database.Path);
                var policyVersion = store.SavePolicy(policy);
                using (var proxy = new DomainFilteringProxy(
                    policy,
                    0,
                    new LoopbackDnsResolver(),
                    GetBlockedPagePath(),
                    eventStore: store,
                    policyVersion: policyVersion))
            {
                proxy.Start();
                try
                {
                    var blockedResponse = await SendRequestAsync(
                        proxy.Port,
                        "GET http://blocked.example.test:" + originPort + "/denied HTTP/1.1\r\n" +
                        "Host: blocked.example.test:" + originPort + "\r\n\r\n").ConfigureAwait(false);

                    AssertContains(blockedResponse, "HTTP/1.1 403 Forbidden");
                    AssertContains(blockedResponse, "此网页已被管理员设置为禁止访问");
                    AssertContains(blockedResponse, "This webpage has been blocked by the administrator.");
                    AssertContains(blockedResponse, "申请访问");
                    AssertContains(blockedResponse, "申请访问的网址");
                    AssertContains(blockedResponse, "申请理由");
                    AssertContains(blockedResponse, "申请未发送");
                    AssertFalse(origin.Pending());

                    var originTask = Task.Run(async () =>
                    {
                        using (var client = await origin.AcceptTcpClientAsync().ConfigureAwait(false))
                        {
                            var request = await ReadHeadersAsync(client.GetStream()).ConfigureAwait(false);
                            var body = Encoding.ASCII.GetBytes("OK");
                            var response = Encoding.ASCII.GetBytes(
                                "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\n");
                            await client.GetStream().WriteAsync(response, 0, response.Length).ConfigureAwait(false);
                            await client.GetStream().WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                            await client.GetStream().FlushAsync().ConfigureAwait(false);
                            return request;
                        }
                    });

                    var allowedResponse = await SendRequestAsync(
                        proxy.Port,
                        "GET http://allowed.example.test:" + originPort + "/resource?q=1 HTTP/1.1\r\n" +
                        "Host: allowed.example.test:" + originPort + "\r\n" +
                        "Proxy-Authorization: Basic test-secret\r\n\r\n").ConfigureAwait(false);
                    var forwardedRequest = await originTask.ConfigureAwait(false);

                    AssertContains(allowedResponse, "HTTP/1.1 200 OK");
                    AssertContains(allowedResponse, "\r\n\r\nOK");
                    AssertContains(forwardedRequest, "GET /resource?q=1 HTTP/1.1");
                    AssertContains(forwardedRequest, "Connection: close");
                    AssertFalse(forwardedRequest.IndexOf("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) >= 0);

                    var blockedEvents = store.QueryAccessEvents(true, null, 1, 10);
                    var allowedEvents = store.QueryAccessEvents(false, "allowed.example.test", 1, 10);
                    AssertEqual(1L, blockedEvents.TotalCount);
                    AssertEqual("blocked.example.test", blockedEvents.Events[0].Domain);
                    AssertEqual(
                        "http://blocked.example.test:" + originPort + "/denied",
                        blockedEvents.Events[0].RequestedUrl);
                    AssertEqual((int?)originPort, blockedEvents.Events[0].DestinationPort);
                    AssertEqual(1L, allowedEvents.TotalCount);
                    AssertEqual(ProxyAccessResult.AllowedByPolicy, allowedEvents.Events[0].Result);
                    AssertEqual(
                        "http://allowed.example.test:" + originPort + "/resource?q=1",
                        allowedEvents.Events[0].RequestedUrl);
                    AssertEqual((int?)originPort, allowedEvents.Events[0].DestinationPort);
                }
                finally
                {
                    await proxy.StopAsync().ConfigureAwait(false);
                    origin.Stop();
                }
            }
            }
        }

        public static async Task ConnectProxyFiltersAndTunnelsAsync()
        {
            var blockedListener = new TcpListener(IPAddress.Loopback, 0);
            blockedListener.Start();
            var blockedPort = ((IPEndPoint)blockedListener.LocalEndpoint).Port;

            var allowedListener = new TcpListener(IPAddress.Loopback, 0);
            allowedListener.Start();
            var allowedPort = ((IPEndPoint)allowedListener.LocalEndpoint).Port;

            var allowedRule = new DomainRule(Guid.NewGuid(), "allowed.example.test", true, true);
            var policy = new AccessPolicy(AccessMode.AllowList, new[] { allowedRule });
            using (var database = new TestDatabase())
            {
                var store = new AccessEventStore(database.Path);
                var policyVersion = store.SavePolicy(policy);
                using (var proxy = new DomainFilteringProxy(
                    policy, 0, new LoopbackDnsResolver(), eventStore: store, policyVersion: policyVersion))
                {
                    proxy.Start();
                    try
                    {
                        var blockedResponse = await SendRequestAsync(
                            proxy.Port,
                            "CONNECT blocked.example.test:" + blockedPort + " HTTP/1.1\r\n" +
                            "Host: blocked.example.test:" + blockedPort + "\r\n\r\n").ConfigureAwait(false);

                        AssertContains(blockedResponse, "HTTP/1.1 403 Forbidden");
                        AssertFalse(blockedListener.Pending());

                        var echoTask = Task.Run(async () =>
                        {
                            using (var remote = await allowedListener.AcceptTcpClientAsync().ConfigureAwait(false))
                            {
                                var stream = remote.GetStream();
                                var buffer = new byte[1];
                                var read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                                if (read != 0)
                                {
                                    await stream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                                    await stream.FlushAsync().ConfigureAwait(false);
                                }
                            }
                        });

                        using (var client = new TcpClient())
                        {
                            await client.ConnectAsync(IPAddress.Loopback, proxy.Port).ConfigureAwait(false);
                            var stream = client.GetStream();
                            var connectRequest = Encoding.ASCII.GetBytes(
                                "CONNECT allowed.example.test:" + allowedPort + " HTTP/1.1\r\n" +
                                "Host: allowed.example.test:" + allowedPort + "\r\n\r\n");
                            await stream.WriteAsync(connectRequest, 0, connectRequest.Length).ConfigureAwait(false);
                            var responseHeaders = await ReadHeadersAsync(stream).ConfigureAwait(false);
                            AssertContains(responseHeaders, "HTTP/1.1 200 Connection Established");

                            var probe = Encoding.ASCII.GetBytes("W");
                            await stream.WriteAsync(probe, 0, probe.Length).ConfigureAwait(false);
                            var echoed = new byte[1];
                            var echoCount = await stream.ReadAsync(echoed, 0, echoed.Length).ConfigureAwait(false);
                            AssertEqual(1, echoCount);
                            AssertEqual((byte)'W', echoed[0]);
                        }

                        await echoTask.ConfigureAwait(false);
                        var events = store.QueryAccessEvents(true, null, 1, 10);
                        AssertEqual(1L, events.TotalCount);
                        AssertEqual("blocked.example.test", events.Events[0].Domain);
                        AssertEqual((int?)blockedPort, events.Events[0].DestinationPort);
                        AssertTrue(events.Events[0].RequestedUrl == null);
                    }
                    finally
                    {
                        await proxy.StopAsync().ConfigureAwait(false);
                        blockedListener.Stop();
                        allowedListener.Stop();
                    }
                }
            }
        }

        public static async Task RejectsMismatchedHostHeaderAsync()
        {
            var origin = new TcpListener(IPAddress.Loopback, 0);
            origin.Start();
            var originPort = ((IPEndPoint)origin.LocalEndpoint).Port;
            var policy = new AccessPolicy(AccessMode.BlockList, new DomainRule[0]);

            using (var database = new TestDatabase())
            {
                var store = new AccessEventStore(database.Path);
                var policyVersion = store.SavePolicy(policy);
                using (var proxy = new DomainFilteringProxy(
                    policy, 0, new LoopbackDnsResolver(), eventStore: store, policyVersion: policyVersion))
                {
                    proxy.Start();
                    try
                    {
                        var response = await SendRequestAsync(
                            proxy.Port,
                            "GET http://allowed.example.test:" + originPort + "/ HTTP/1.1\r\n" +
                            "Host: other.example.test:" + originPort + "\r\n\r\n").ConfigureAwait(false);

                        AssertContains(response, "HTTP/1.1 400 Bad Request");
                        AssertFalse(origin.Pending());

                        var connectionHostResponse = await SendRequestAsync(
                            proxy.Port,
                            "GET http://allowed.example.test:" + originPort + "/ HTTP/1.1\r\n" +
                            "Host: allowed.example.test:" + originPort + "\r\n" +
                            "Connection: Host\r\n\r\n").ConfigureAwait(false);

                        AssertContains(connectionHostResponse, "HTTP/1.1 400 Bad Request");
                        AssertFalse(origin.Pending());
                    }

                    finally
                    {
                        await proxy.StopAsync().ConfigureAwait(false);
                        origin.Stop();
                    }
                }
            }
        }

        public static async Task TimedAccessExpiryServesTimeoutPageAsync()
        {
            using (var database = new TestDatabase())
            {
                var store = new AccessEventStore(database.Path);
                var localNow = DateTime.SpecifyKind(
                    new DateTime(2026, 1, 15, 12, 0, 0),
                    DateTimeKind.Local);
                var category = new TimedAccessCategory("Study", 1);
                var timedRule = new TimedAccessRule(
                    Guid.NewGuid(), "timed.example.test", true, category.Name, true);
                var policy = new AccessPolicy(
                    AccessMode.BlockList,
                    new DomainRule[0],
                    new[] { category },
                    new[] { timedRule });
                var policyVersion = store.SavePolicy(policy);
                store.AuthorizeTimedAccess(category, localNow.AddMinutes(-2));

                using (var proxy = new DomainFilteringProxy(
                    policy,
                    0,
                    new LoopbackDnsResolver(),
                    eventStore: store,
                    policyVersion: policyVersion,
                    timeoutPagePath: GetTimeoutPagePath(),
                    localClock: () => localNow))
                {
                    proxy.Start();
                    try
                    {
                        var response = await SendRequestAsync(
                            proxy.Port,
                            "GET http://timed.example.test/ HTTP/1.1\r\n" +
                            "Host: timed.example.test\r\n\r\n").ConfigureAwait(false);
                        AssertContains(response, "HTTP/1.1 403 Forbidden");
                        AssertContains(response, "本网页的可访问时间已到，已禁止访问");
                        AssertContains(response, "申请访问");
                        AssertContains(response, "申请访问的网址");
                        AssertContains(response, "申请理由");
                        AssertContains(response, "申请未发送");
                        AssertFalse(response.IndexOf("此网页已被管理员设置为禁止访问", StringComparison.Ordinal) >= 0);

                        var events = store.QueryAccessEvents(true, null, 1, 10);
                        AssertEqual(1L, events.TotalCount);
                        AssertEqual(ProxyAccessResult.BlockedByTimeLimit, events.Events[0].Result);
                        AssertEqual(timedRule.Id, events.Events[0].MatchedRuleId.Value);
                    }
                    finally
                    {
                        await proxy.StopAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        public static async Task TimedAccessDoesNotOverrideBaseDenialAsync()
        {
            using (var database = new TestDatabase())
            {
                var store = new AccessEventStore(database.Path);
                var category = new TimedAccessCategory("Study", 30);
                var timedRule = new TimedAccessRule(
                    Guid.NewGuid(), "timed.example.test", true, category.Name, true);
                var allowRule = new DomainRule(Guid.NewGuid(), "allowed.example.test", true, true);
                var policy = new AccessPolicy(
                    AccessMode.AllowList,
                    new[] { allowRule },
                    new[] { category },
                    new[] { timedRule });
                var policyVersion = store.SavePolicy(policy);

                using (var proxy = new DomainFilteringProxy(
                    policy,
                    0,
                    new LoopbackDnsResolver(),
                    GetBlockedPagePath(),
                    eventStore: store,
                    policyVersion: policyVersion))
                {
                    proxy.Start();
                    try
                    {
                        var response = await SendRequestAsync(
                            proxy.Port,
                            "GET http://timed.example.test/ HTTP/1.1\r\n" +
                            "Host: timed.example.test\r\n\r\n").ConfigureAwait(false);
                        AssertContains(response, "HTTP/1.1 403 Forbidden");
                        AssertContains(response, "此网页已被管理员设置为禁止访问");

                        var events = store.QueryAccessEvents(true, null, 1, 10);
                        AssertEqual(ProxyAccessResult.BlockedByPolicy, events.Events[0].Result);
                        AssertTrue(!events.Events[0].MatchedRuleId.HasValue);
                    }
                    finally
                    {
                        await proxy.StopAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        public static async Task TimedConnectTunnelClosesAtExpiryAsync()
        {
            var origin = new TcpListener(IPAddress.Loopback, 0);
            origin.Start();
            var originPort = ((IPEndPoint)origin.LocalEndpoint).Port;

            using (var database = new TestDatabase())
            {
                var store = new AccessEventStore(database.Path);
                var localNow = DateTime.SpecifyKind(
                    new DateTime(2026, 1, 15, 12, 0, 0),
                    DateTimeKind.Local);
                var category = new TimedAccessCategory("Study", 1);
                var timedRule = new TimedAccessRule(
                    Guid.NewGuid(), "timed.example.test", true, category.Name, true);
                var policy = new AccessPolicy(
                    AccessMode.BlockList,
                    new DomainRule[0],
                    new[] { category },
                    new[] { timedRule });
                var policyVersion = store.SavePolicy(policy);
                store.AuthorizeTimedAccess(category, localNow.AddMinutes(-1).AddSeconds(1));

                var originTask = Task.Run(async () =>
                {
                    using (var accepted = await origin.AcceptTcpClientAsync().ConfigureAwait(false))
                    {
                        await accepted.GetStream().ReadAsync(new byte[1], 0, 1).ConfigureAwait(false);
                    }
                });

                using (var proxy = new DomainFilteringProxy(
                    policy,
                    0,
                    new LoopbackDnsResolver(),
                    eventStore: store,
                    policyVersion: policyVersion,
                    localClock: () => localNow))
                using (var client = new TcpClient())
                {
                    proxy.Start();
                    try
                    {
                        await client.ConnectAsync(IPAddress.Loopback, proxy.Port).ConfigureAwait(false);
                        var stream = client.GetStream();
                        var request = Encoding.ASCII.GetBytes(
                            "CONNECT timed.example.test:" + originPort + " HTTP/1.1\r\n" +
                            "Host: timed.example.test:" + originPort + "\r\n\r\n");
                        await stream.WriteAsync(request, 0, request.Length).ConfigureAwait(false);
                        var responseHeaders = await ReadHeadersAsync(stream).ConfigureAwait(false);
                        AssertContains(responseHeaders, "HTTP/1.1 200 Connection Established");

                        var readTask = stream.ReadAsync(new byte[1], 0, 1);
                        if (await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(4))).ConfigureAwait(false) != readTask)
                        {
                            throw new InvalidOperationException("The timed CONNECT tunnel remained open past expiry.");
                        }

                        AssertEqual(0, await readTask.ConfigureAwait(false));
                    }
                    finally
                    {
                        await proxy.StopAsync().ConfigureAwait(false);
                        origin.Stop();
                    }
                }

                await originTask.ConfigureAwait(false);
            }
        }

        public static async Task BlockPageFailureIsPersistedAndDoesNotMasqueradeAsSuccessAsync()
        {
            var policy = new AccessPolicy(AccessMode.AllowList, new DomainRule[0]);
            using (var database = new TestDatabase())
            {
                var store = new AccessEventStore(database.Path);
                var policyVersion = store.SavePolicy(policy);
                var missingPage = Path.Combine(database.DirectoryPath, "missing-index.html");
                using (var proxy = new DomainFilteringProxy(
                    policy,
                    0,
                    new LoopbackDnsResolver(),
                    missingPage,
                    eventStore: store,
                    policyVersion: policyVersion))
                {
                    proxy.Start();
                    try
                    {
                        var response = await SendRequestAsync(
                            proxy.Port,
                            "GET http://blocked.example.test/ HTTP/1.1\r\n" +
                            "Host: blocked.example.test\r\n\r\n").ConfigureAwait(false);
                        AssertContains(response, "HTTP/1.1 503 Filtering Error");

                        var events = store.QueryAccessEvents(true, null, 1, 10);
                        AssertEqual(1L, events.TotalCount);
                        AssertEqual(ProxyAccessResult.BlockPageFailure, events.Events[0].Result);
                    }
                    finally
                    {
                        await proxy.StopAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        private static async Task<string> SendRequestAsync(int proxyPort, string request)
        {
            using (var client = new TcpClient())
            {
                client.ReceiveTimeout = 10000;
                await client.ConnectAsync(IPAddress.Loopback, proxyPort).ConfigureAwait(false);
                var bytes = Encoding.ASCII.GetBytes(request);
                await client.GetStream().WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                await client.GetStream().FlushAsync().ConfigureAwait(false);

                using (var response = new MemoryStream())
                {
                    await client.GetStream().CopyToAsync(response).ConfigureAwait(false);
                    return Encoding.UTF8.GetString(response.ToArray());
                }
            }
        }

        private static async Task<string> ReadHeadersAsync(NetworkStream stream)
        {
            using (var headers = new MemoryStream())
            {
                var matched = 0;
                var buffer = new byte[1];
                while (headers.Length < 32768)
                {
                    var read = await stream.ReadAsync(buffer, 0, 1).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    var value = buffer[0];
                    headers.WriteByte(value);
                    switch (matched)
                    {
                        case 0:
                            matched = value == 13 ? 1 : 0;
                            break;
                        case 1:
                            matched = value == 10 ? 2 : (value == 13 ? 1 : 0);
                            break;
                        case 2:
                            matched = value == 13 ? 3 : 0;
                            break;
                        case 3:
                            if (value == 10)
                            {
                                return Encoding.ASCII.GetString(headers.ToArray());
                            }

                            matched = value == 13 ? 1 : 0;
                            break;
                    }
                }

                throw new InvalidDataException("The response did not contain complete headers.");
            }
        }

        private static string GetBlockedPagePath()
        {
            var outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index.html");
            if (File.Exists(outputPath))
            {
                return outputPath;
            }

            var sourcePath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                @"..\..\..\..\..\src\WebBlocker.Filtering\index.html"));
            if (File.Exists(sourcePath))
            {
                return sourcePath;
            }

            throw new FileNotFoundException("The local blocked-page fixture could not be found.", outputPath);
        }

        private static string GetTimeoutPagePath()
        {
            var outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "timeout.html");
            if (File.Exists(outputPath))
            {
                return outputPath;
            }

            var sourcePath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                @"..\..\..\..\..\src\WebBlocker.Filtering\timeout.html"));
            if (File.Exists(sourcePath))
            {
                return sourcePath;
            }

            throw new FileNotFoundException("The local timeout-page fixture could not be found.", outputPath);
        }

        private static void AssertContains(string value, string expected)
        {
            if (value.IndexOf(expected, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("Expected response to contain: " + expected);
            }
        }

        private static void AssertFalse(bool condition)
        {
            if (condition)
            {
                throw new InvalidOperationException("Expected false.");
            }
        }

        private static void AssertTrue(bool condition)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Expected true.");
            }
        }

        private static void AssertEqual<T>(T expected, T actual)
        {
            if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    string.Format("Expected '{0}', got '{1}'.", expected, actual));
            }
        }

        private sealed class LoopbackDnsResolver : IProxyDnsResolver
        {
            public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new[] { IPAddress.Loopback });
            }
        }
    }
}
