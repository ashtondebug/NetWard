using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebBlocker.Persistence;

namespace WebBlocker.Shared.Tests
{
    /// <summary>
    /// Coverage for the remote audit store used by the LAN synchronization: per-device isolation,
    /// idempotent import (a retried sync must not duplicate rows) and the shared 30-day retention.
    /// </summary>
    internal static class RemoteEventTests
    {
        public static Task RemoteEventsAreIsolatedPerDeviceAndImportIsIdempotentAsync()
        {
            using (var database = new TestDatabase())
            {
                var store = new AccessEventStore(database.Path);
                var baseTime = new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);

                var firstId = Guid.NewGuid();
                var secondId = Guid.NewGuid();
                var classroom = new List<RemoteAccessEvent>
                {
                    new RemoteAccessEvent("device-classroom", firstId, baseTime, "blocked.example.test",
                        "GET", ProxyAccessResult.BlockedByPolicy, "http://blocked.example.test/a", 80),
                    new RemoteAccessEvent("device-classroom", secondId, baseTime.AddMinutes(5), "allowed.example.test",
                        "CONNECT", ProxyAccessResult.AllowedByPolicy, null, 443)
                };

                AssertEqual(2, store.ImportRemoteEvents("device-classroom", classroom), "both events must be imported");
                AssertEqual(0, store.ImportRemoteEvents("device-classroom", classroom),
                    "a repeated import must not create duplicate rows");

                var office = new List<RemoteAccessEvent>
                {
                    new RemoteAccessEvent("device-office", Guid.NewGuid(), baseTime.AddMinutes(1), "office.example.test",
                        "GET", ProxyAccessResult.BlockedByCategory, "http://office.example.test/x", 80)
                };
                AssertEqual(1, store.ImportRemoteEvents("device-office", office), "the second device must be stored separately");

                AssertEqual(2L, store.CountRemoteEvents("device-classroom"), "the classroom device must keep two events");
                AssertEqual(1L, store.CountRemoteEvents("device-office"), "the office device must keep one event");

                var queried = store.QueryRemoteEvents("device-classroom", 10);
                AssertEqual(2, queried.Count, "the query must return the device's events");
                AssertEqual(secondId, queried[0].EventId, "the newest event must come first");
                AssertEqual(firstId, queried[1].EventId, "the older event must come second");
                AssertEqual("blocked.example.test", queried[1].Domain, "the domain must round-trip");

                var rejected = false;
                try
                {
                    store.ImportRemoteEvents("device-classroom", office);
                }
                catch (ArgumentException)
                {
                    rejected = true;
                }

                AssertTrue(rejected, "importing events that belong to another device must be rejected");
            }

            return Task.CompletedTask;
        }

        public static Task RemoteEventsShareTheThirtyDayRetentionAsync()
        {
            using (var database = new TestDatabase())
            {
                var store = new AccessEventStore(database.Path);
                var now = new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);

                var oldEvent = new RemoteAccessEvent("device-classroom", Guid.NewGuid(), now.AddDays(-40),
                    "old.example.test", "GET", ProxyAccessResult.BlockedByPolicy, null, 80);
                var freshEvent = new RemoteAccessEvent("device-classroom", Guid.NewGuid(), now.AddDays(-1),
                    "new.example.test", "GET", ProxyAccessResult.AllowedByPolicy, null, 80);

                store.ImportRemoteEvents("device-classroom", new[] { oldEvent, freshEvent });
                AssertEqual(2L, store.CountRemoteEvents("device-classroom"), "both events must be stored first");

                store.PruneExpiredAccessEvents(now);
                AssertEqual(1L, store.CountRemoteEvents("device-classroom"),
                    "the expired remote event must be pruned with the local retention window");

                var remaining = store.QueryRemoteEvents("device-classroom", 10);
                AssertEqual("new.example.test", remaining[0].Domain, "only the recent event may remain");
            }

            return Task.CompletedTask;
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
    }
}
