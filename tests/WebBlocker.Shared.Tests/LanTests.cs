using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NetWard.Lan;

namespace WebBlocker.Shared.Tests
{
    /// <summary>
    /// Loopback tests for the LAN pairing and session layer. They use real TLS sockets on 127.0.0.1,
    /// real self-signed device certificates and the real pairing state files; no external network is used.
    /// </summary>
    internal static class LanTests
    {
        public static Task IdentityIsStableAcrossReloadsAsync()
        {
            var directory = CreateTemporaryDirectory("identity");
            try
            {
                using (var first = DeviceIdentity.LoadOrCreate(directory, "test"))
                using (var second = DeviceIdentity.LoadOrCreate(directory, "ignored"))
                {
                    AssertEqual(first.DeviceId, second.DeviceId, "device id must be stable");
                    AssertTrue(DeviceIdentity.ThumbprintEquals(first.Thumbprint, second.Thumbprint),
                        "certificate thumbprint must be stable");
                    AssertTrue(first.Fingerprint.Length == 19 && first.Fingerprint[4] == '-' && first.Fingerprint[9] == '-' &&
                               first.Fingerprint[14] == '-', "fingerprint format must be four groups of four hex digits");
                    AssertTrue(first.Certificate.HasPrivateKey, "the identity must keep its private key");
                }
            }
            finally
            {
                DeleteDirectory(directory);
            }

            return Task.CompletedTask;
        }

        public static Task PairingCodeIsSingleUseAndExpiresAsync()
        {
            var directory = CreateTemporaryDirectory("store");
            try
            {
                var store = new AgentLanStore(directory);
                var now = DateTime.UtcNow;
                var code = store.EnsurePairingCode(now);
                AssertTrue(code.Length == 6, "the pairing code must have six digits");

                var outcome = store.TryPair(code, NewAdmin("admin-1", "指纹一"), now);
                AssertEqual(PairingOutcome.Paired, outcome, "a correct code must pair");

                var replay = store.TryPair(code, NewAdmin("admin-2", "指纹二"), now);
                AssertEqual(PairingOutcome.ExpiredCode, replay, "a used code must not pair a second administrator");
                AssertEqual(1, store.CountAdmins(), "only one administrator may be paired");

                var expiredCode = store.RotatePairingCode(now);
                var expired = store.TryPair(expiredCode, NewAdmin("admin-3", "指纹三"), now.AddMinutes(11));
                AssertEqual(PairingOutcome.ExpiredCode, expired, "an expired code must be rejected");
            }
            finally
            {
                DeleteDirectory(directory);
            }

            return Task.CompletedTask;
        }

        public static Task WrongPairingCodeLocksAfterFiveAttemptsAsync()
        {
            var directory = CreateTemporaryDirectory("lock");
            try
            {
                var store = new AgentLanStore(directory);
                var now = DateTime.UtcNow;
                var code = store.EnsurePairingCode(now);
                var wrong = code == "000000" ? "111111" : "000000";

                for (var attempt = 0; attempt < AgentLanStore.MaximumFailedAttempts; attempt++)
                {
                    AssertEqual(PairingOutcome.InvalidCode, store.TryPair(wrong, NewAdmin(null, "指纹"), now),
                        "a wrong code must be rejected");
                }

                AssertEqual(PairingOutcome.TooManyAttempts, store.TryPair(wrong, NewAdmin(null, "指纹"), now),
                    "after five failures the code must be locked");

                // Even the correct code must not rescue a locked enrolment.
                AssertEqual(PairingOutcome.TooManyAttempts, store.TryPair(code, NewAdmin(null, "指纹"), now),
                    "a locked code must stay locked");
            }
            finally
            {
                DeleteDirectory(directory);
            }

            return Task.CompletedTask;
        }

        public static Task PairingThenStatusPolicyAndEventsWorkAsync()
        {
            var root = CreateTemporaryDirectory("session");
            try
            {
                var handler = new FakeAgentHandler();
                var store = new AgentLanStore(Path.Combine(root, "agent"));
                using (var serverIdentity = DeviceIdentity.LoadOrCreate(Path.Combine(root, "agent"), "被控端"))
                using (var administrator = DeviceIdentity.LoadOrCreate(Path.Combine(root, "admin"), "管理端"))
                using (var server = new LanServer(0, serverIdentity, store, handler))
                {
                    server.Start();
                    var code = store.EnsurePairingCode(DateTime.UtcNow);
                    var paired = LanClient.Pair("127.0.0.1", server.Port, code, administrator, "测试管理端");

                    AssertEqual(serverIdentity.DeviceId, paired.DeviceId, "the endpoint id must be returned");
                    AssertEqual(DeviceIdentity.FormatFingerprint(serverIdentity.Certificate), paired.Fingerprint,
                        "the endpoint fingerprint must match its certificate");
                    AssertEqual(1, store.CountAdmins(), "the administrator must be stored on the endpoint");

                    var device = new PairedDevice
                    {
                        DeviceId = paired.DeviceId,
                        Name = paired.DeviceName,
                        Address = "127.0.0.1",
                        Port = server.Port,
                        Thumbprint = serverIdentity.Thumbprint,
                        Fingerprint = paired.Fingerprint
                    };

                    var status = LanClient.GetStatus(device, administrator);
                    AssertEqual(7L, status.PolicyVersion, "status must come from the handler");
                    AssertEqual(3, status.Rules, "status rule count must come from the handler");

                    var policyResult = LanClient.PushPolicy(device, administrator, "{\"Mode\":\"AllowList\"}");
                    AssertEqual(9L, policyResult.Version, "the pushed policy version must be returned");
                    AssertEqual("{\"Mode\":\"AllowList\"}", handler.LastPolicyJson,
                        "the endpoint must receive the policy payload unchanged");

                    var events = LanClient.PullEvents(device, administrator, DateTime.MinValue, 50);
                    AssertEqual(2, events.Items.Count, "events must be returned from the handler");
                    AssertEqual("domain.example.test", events.Items[0].Domain, "event payload must survive the wire");
                }
            }
            finally
            {
                DeleteDirectory(root);
            }

            return Task.CompletedTask;
        }

        public static Task UnpairedAdministratorIsRejectedAsync()
        {
            var root = CreateTemporaryDirectory("unpaired");
            try
            {
                var handler = new FakeAgentHandler();
                var store = new AgentLanStore(Path.Combine(root, "agent"));
                using (var serverIdentity = DeviceIdentity.LoadOrCreate(Path.Combine(root, "agent"), "被控端"))
                using (var pairedAdmin = DeviceIdentity.LoadOrCreate(Path.Combine(root, "admin-paired"), "管理端"))
                using (var stranger = DeviceIdentity.LoadOrCreate(Path.Combine(root, "admin-stranger"), "陌生管理端"))
                using (var server = new LanServer(0, serverIdentity, store, handler))
                {
                    server.Start();
                    var code = store.EnsurePairingCode(DateTime.UtcNow);
                    var paired = LanClient.Pair("127.0.0.1", server.Port, code, pairedAdmin, "已配对管理端");
                    AssertTrue(paired != null, "pairing must succeed for the first administrator");

                    var strangerDevice = new PairedDevice
                    {
                        DeviceId = paired.DeviceId,
                        Address = "127.0.0.1",
                        Port = server.Port,
                        Thumbprint = serverIdentity.Thumbprint
                    };

                    var rejected = false;
                    try
                    {
                        LanClient.GetStatus(strangerDevice, stranger);
                    }
                    catch (LanProtocolException exception)
                    {
                        rejected = exception.Code == LanErrorCodes.Unauthorized;
                    }

                    AssertTrue(rejected, "an administrator that never paired must be rejected as unauthorized");
                }
            }
            finally
            {
                DeleteDirectory(root);
            }

            return Task.CompletedTask;
        }

        public static Task ChangedEndpointCertificateIsRejectedAsync()
        {
            var root = CreateTemporaryDirectory("rotate");
            try
            {
                var handler = new FakeAgentHandler();
                var agentDirectory = Path.Combine(root, "agent");
                var adminDirectory = Path.Combine(root, "admin");
                PairedDevice device;

                using (var administrator = DeviceIdentity.LoadOrCreate(adminDirectory, "管理端"))
                {
                    int port;
                    var store = new AgentLanStore(agentDirectory);
                    using (var identity = DeviceIdentity.LoadOrCreate(agentDirectory, "被控端"))
                    using (var server = new LanServer(0, identity, store, handler))
                    {
                        server.Start();
                        port = server.Port;
                        var code = store.EnsurePairingCode(DateTime.UtcNow);
                        var paired = LanClient.Pair("127.0.0.1", port, code, administrator, "管理端");
                        device = new PairedDevice
                        {
                            DeviceId = paired.DeviceId,
                            Address = "127.0.0.1",
                            Port = port,
                            Thumbprint = identity.Thumbprint
                        };
                    }

                    // A different endpoint certificate on the same address must not be trusted by pinning.
                    var impostorDirectory = Path.Combine(root, "impostor");
                    var impostorStore = new AgentLanStore(impostorDirectory);
                    using (var impostor = DeviceIdentity.LoadOrCreate(impostorDirectory, "冒充者"))
                    using (var impostorServer = new LanServer(port, impostor, impostorStore, handler))
                    {
                        impostorServer.Start();
                        var rejected = false;
                        try
                        {
                            LanClient.GetStatus(device, administrator);
                        }
                        catch (LanProtocolException exception)
                        {
                            rejected = exception.Code == LanErrorCodes.Unauthorized;
                        }

                        AssertTrue(rejected, "a changed endpoint certificate must be rejected by the pinned thumbprint");
                    }
                }
            }
            finally
            {
                DeleteDirectory(root);
            }

            return Task.CompletedTask;
        }

        public static Task DeviceStoreRoundTripsAsync()
        {
            var directory = CreateTemporaryDirectory("devices");
            try
            {
                var store = new DeviceStore(directory);
                AssertEqual(0, store.Load().Count, "a new store must be empty");

                store.AddOrReplace(new PairedDevice
                {
                    DeviceId = "device-1",
                    Name = "教室电脑",
                    Address = "192.168.1.20",
                    Port = 18770,
                    Thumbprint = "ABC",
                    Fingerprint = "AAAA-BBBB-CCCC-DDDD",
                    PairedAtUtc = DeviceStore.FormatUtc(DateTime.UtcNow)
                });
                store.AddOrReplace(new PairedDevice
                {
                    DeviceId = "device-2",
                    Name = "办公室电脑",
                    Address = "192.168.1.21",
                    Port = 18770,
                    Thumbprint = "DEF"
                });

                var reloaded = new DeviceStore(directory);
                AssertEqual(2, reloaded.Load().Count, "both devices must survive a reload");
                AssertTrue(reloaded.Find("device-1") != null, "a stored device must be found by id");
                AssertEqual("教室电脑", reloaded.Find("device-1").Name, "unicode device names must round-trip");

                AssertTrue(reloaded.Remove("device-1"), "removing a device must report success");
                AssertEqual(1, new DeviceStore(directory).Load().Count, "removal must be persisted");
            }
            finally
            {
                DeleteDirectory(directory);
            }

            return Task.CompletedTask;
        }

        private static PairedAdmin NewAdmin(string adminId, string name)
        {
            return new PairedAdmin
            {
                AdminId = adminId ?? Guid.NewGuid().ToString("N"),
                Name = name,
                Thumbprint = Guid.NewGuid().ToString("N").ToUpperInvariant()
            };
        }

        private static string CreateTemporaryDirectory(string purpose)
        {
            var path = Path.Combine(Path.GetTempPath(), "netward-lan-" + purpose + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    message + " (expected '" + expected + "', got '" + actual + "')");
            }
        }

        private sealed class FakeAgentHandler : ILanAgentHandler
        {
            public FakeAgentHandler()
            {
                Events = new List<LanEvent>
                {
                    new LanEvent
                    {
                        Id = Guid.NewGuid().ToString("D"),
                        OccurredAtUtc = DateTime.UtcNow.AddMinutes(-2).ToString("o"),
                        Domain = "domain.example.test",
                        Method = "GET",
                        Result = 1,
                        RequestedUrl = "http://domain.example.test/path",
                        Port = 80
                    },
                    new LanEvent
                    {
                        Id = Guid.NewGuid().ToString("D"),
                        OccurredAtUtc = DateTime.UtcNow.AddMinutes(-1).ToString("o"),
                        Domain = "allowed.example.test",
                        Method = "CONNECT",
                        Result = 0,
                        Port = 443
                    }
                };
            }

            public string DeviceName
            {
                get { return "测试被控端"; }
            }

            public string LastPolicyJson { get; private set; }

            public List<LanEvent> Events { get; private set; }

            public StatusPayload GetStatus()
            {
                return new StatusPayload
                {
                    DeviceId = "device",
                    DeviceName = DeviceName,
                    PolicyVersion = 7,
                    Rules = 3,
                    Categories = 1,
                    Events = 42,
                    ProxyRunning = true,
                    UptimeSeconds = 90
                };
            }

            public PolicyResultPayload ApplyPolicy(string policyJson)
            {
                LastPolicyJson = policyJson;
                return new PolicyResultPayload { Version = 9, Rules = 3 };
            }

            public EventsPayload GetEvents(string sinceUtc, int limit)
            {
                return new EventsPayload { Items = Events, Truncated = false };
            }
        }
    }
}
