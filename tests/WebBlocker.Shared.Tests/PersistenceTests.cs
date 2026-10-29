using System;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using WebBlocker.Persistence;
using WebBlocker.Shared;

namespace WebBlocker.Shared.Tests
{
    internal static class PersistenceTests
    {
        public static Task PolicyAndAccessEventsPersistAcrossStoreInstancesAsync()
        {
            using (var database = new TestDatabase())
            {
                var store = new AccessEventStore(database.Path);
                var firstRule = new DomainRule(Guid.NewGuid(), "allowed.example.test", true, true);
                var secondRule = new DomainRule(Guid.NewGuid(), "disabled.example.test", false, false);
                var policy = new AccessPolicy(AccessMode.AllowList, new[] { firstRule, secondRule });
                var policyVersion = store.SavePolicy(policy);

                AssertEqual(1L, policyVersion);

                var blockedEvent = new ProxyAccessEvent(
                    Guid.NewGuid(),
                    new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc),
                    "blocked.example.test",
                    "GET",
                    ProxyAccessResult.BlockedByPolicy,
                    null,
                    policyVersion);
                var allowedEvent = new ProxyAccessEvent(
                    Guid.NewGuid(),
                    new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc),
                    "allowed.example.test",
                    "CONNECT",
                    ProxyAccessResult.AllowedByPolicy,
                    firstRule.Id,
                    policyVersion);

                AssertTrue(store.RecordAccessEvent(blockedEvent));
                AssertTrue(store.RecordAccessEvent(allowedEvent));
                AssertFalse(store.RecordAccessEvent(allowedEvent));

                var restartedStore = new AccessEventStore(database.Path);
                PolicySnapshot snapshot;
                AssertTrue(restartedStore.TryLoadPolicy(out snapshot));
                AssertEqual(AccessMode.AllowList, snapshot.Policy.Mode);
                AssertEqual(policyVersion, snapshot.Version);
                AssertEqual(2, snapshot.Policy.Rules.Count);
                AssertEqual(firstRule.Domain, snapshot.Policy.Rules[0].Domain);
                AssertFalse(snapshot.Policy.Rules[1].IsEnabled);

                var blockedEvents = restartedStore.QueryAccessEvents(true, null, 1, 1);
                AssertEqual(1L, blockedEvents.TotalCount);
                AssertEqual(1, blockedEvents.Events.Count);
                AssertEqual(blockedEvent.EventId, blockedEvents.Events[0].EventId);
                AssertEqual("blocked.example.test", blockedEvents.Events[0].Domain);

                var domainEvents = restartedStore.QueryAccessEvents(false, "ALLOWED.EXAMPLE", 1, 10);
                AssertEqual(1L, domainEvents.TotalCount);
                AssertEqual(ProxyAccessResult.AllowedByPolicy, domainEvents.Events[0].Result);

                var nextVersion = restartedStore.SavePolicy(
                    new AccessPolicy(AccessMode.BlockList, new[] { firstRule }));
                AssertEqual(2L, nextVersion);

                var history = restartedStore.QueryPolicyHistory(1, 10);
                AssertEqual(2, history.Count);
                AssertEqual(2L, history[0].Version);
                AssertEqual(1, history[0].RuleCount);
                AssertEqual(1L, history[1].Version);
                AssertEqual(2, history[1].RuleCount);

                var restoredVersion = restartedStore.RestorePolicy(1);
                AssertEqual(3L, restoredVersion);
                PolicySnapshot updatedSnapshot;
                AssertTrue(restartedStore.TryLoadPolicy(out updatedSnapshot));
                AssertEqual(AccessMode.AllowList, updatedSnapshot.Policy.Mode);
                AssertEqual(3L, updatedSnapshot.Version);
                AssertEqual(2, updatedSnapshot.Policy.Rules.Count);
                AssertEqual(firstRule.Id, updatedSnapshot.Policy.Rules[0].Id);
                AssertEqual(secondRule.Id, updatedSnapshot.Policy.Rules[1].Id);
                AssertEqual(3, restartedStore.QueryPolicyHistory(1, 10).Count);
                AssertEqual(2L, restartedStore.QueryAccessEvents(false, null, 1, 10).TotalCount);

                AssertThrows<System.Collections.Generic.KeyNotFoundException>(
                    () => restartedStore.RestorePolicy(99));
                AssertThrows<ArgumentOutOfRangeException>(
                    () => restartedStore.QueryPolicyHistory(1, 101));
                AssertThrows<ArgumentOutOfRangeException>(
                    () => restartedStore.QueryAccessEvents(false, null, 1, 501));
            }

            return Task.FromResult(true);
        }

        public static Task BackupAndRetentionPreservePolicyAndApplyCutoffAsync()
        {
            using (var database = new TestDatabase())
            {
                var store = new AccessEventStore(database.Path);
                var policy = new AccessPolicy(
                    AccessMode.AllowList,
                    new[] { new DomainRule(Guid.NewGuid(), "allowed.example.test", true, true) });
                store.SavePolicy(policy);

                var cutoff = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
                var oldEvent = CreateEvent(cutoff.AddTicks(-1), "old.example.test");
                var boundaryEvent = CreateEvent(cutoff, "boundary.example.test");
                var recentEvent = CreateEvent(cutoff.AddTicks(1), "recent.example.test");
                AssertTrue(store.RecordAccessEvent(oldEvent));
                AssertTrue(store.RecordAccessEvent(boundaryEvent));
                AssertTrue(store.RecordAccessEvent(recentEvent));

                store.BackupTo(database.BackupPath);
                var backupStore = new AccessEventStore(database.BackupPath);
                PolicySnapshot backupPolicy;
                AssertTrue(backupStore.TryLoadPolicy(out backupPolicy));
                AssertEqual(AccessMode.AllowList, backupPolicy.Policy.Mode);
                AssertEqual(1L, backupPolicy.Version);
                AssertEqual(3L, backupStore.QueryAccessEvents(false, null, 1, 10).TotalCount);

                AssertThrows<IOException>(() => store.BackupTo(database.Path));
                AssertThrows<IOException>(() => store.BackupTo(database.BackupPath));

                AssertEqual(1, store.PruneAccessEventsBefore(cutoff));
                var remainingEvents = store.QueryAccessEvents(false, null, 1, 10);
                AssertEqual(2L, remainingEvents.TotalCount);
                AssertEqual(recentEvent.EventId, remainingEvents.Events[0].EventId);
                AssertEqual(boundaryEvent.EventId, remainingEvents.Events[1].EventId);

                AssertThrows<ArgumentException>(
                    () => store.PruneAccessEventsBefore(DateTime.SpecifyKind(cutoff, DateTimeKind.Local)));
                AssertEqual(3L, backupStore.QueryAccessEvents(false, null, 1, 10).TotalCount);
            }

            return Task.FromResult(true);
        }

        public static Task SchemaVersionHandlingIsExplicitAsync()
        {
            using (var database = new TestDatabase())
            {
                new AccessEventStore(database.Path);
                AssertEqual(2, ReadSchemaVersion(database.Path));

                var futureDatabasePath = Path.Combine(database.DirectoryPath, "future-schema.db");
                var builder = new SQLiteConnectionStringBuilder
                {
                    DataSource = futureDatabasePath,
                    Version = 3,
                    Pooling = false
                };
                using (var connection = new SQLiteConnection(builder.ConnectionString))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "PRAGMA user_version = 999;";
                        command.ExecuteNonQuery();
                    }
                }

                AssertThrows<InvalidDataException>(() => new AccessEventStore(futureDatabasePath));

                var legacyDatabasePath = Path.Combine(database.DirectoryPath, "schema-v1.db");
                CreateVersionOneDatabase(legacyDatabasePath);
                var migratedStore = new AccessEventStore(legacyDatabasePath);
                AssertEqual(2, ReadSchemaVersion(legacyDatabasePath));
                PolicySnapshot migratedPolicy;
                AssertTrue(migratedStore.TryLoadPolicy(out migratedPolicy));
                AssertEqual(4L, migratedPolicy.Version);
                AssertEqual(AccessMode.BlockList, migratedPolicy.Policy.Mode);
                AssertEqual("legacy.example.test", migratedPolicy.Policy.Rules[0].Domain);
                AssertFalse(migratedPolicy.Policy.Rules[0].IsEnabled);
                var migratedHistory = migratedStore.QueryPolicyHistory(1, 10);
                AssertEqual(1, migratedHistory.Count);
                AssertEqual(4L, migratedHistory[0].Version);
                AssertEqual(1, migratedHistory[0].RuleCount);
                AssertEqual(5L, migratedStore.RestorePolicy(4));
            }

            return Task.FromResult(true);
        }

        private static void CreateVersionOneDatabase(string databasePath)
        {
            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = databasePath,
                Version = 3,
                Pooling = false
            };
            using (var connection = new SQLiteConnection(builder.ConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "CREATE TABLE Policies (" +
                        "PolicyId INTEGER NOT NULL PRIMARY KEY CHECK (PolicyId = 1), " +
                        "Mode INTEGER NOT NULL CHECK (Mode IN (0, 1)), " +
                        "Version INTEGER NOT NULL CHECK (Version >= 1), " +
                        "UpdatedAtUtc TEXT NOT NULL);" +
                        "CREATE TABLE Rules (" +
                        "RuleId TEXT NOT NULL PRIMARY KEY, " +
                        "Domain TEXT NOT NULL, " +
                        "IncludeSubdomains INTEGER NOT NULL CHECK (IncludeSubdomains IN (0, 1)), " +
                        "IsEnabled INTEGER NOT NULL CHECK (IsEnabled IN (0, 1)), " +
                        "UNIQUE (Domain, IncludeSubdomains));" +
                        "CREATE TABLE AccessEvents (" +
                        "EventId TEXT NOT NULL PRIMARY KEY, " +
                        "OccurredAtUtc TEXT NOT NULL, " +
                        "Domain TEXT NOT NULL, " +
                        "Method TEXT NOT NULL, " +
                        "Result INTEGER NOT NULL CHECK (Result IN (0, 1, 2)), " +
                        "MatchedRuleId TEXT NULL, " +
                        "PolicyVersion INTEGER NOT NULL CHECK (PolicyVersion >= 1));" +
                        "CREATE INDEX IX_AccessEvents_OccurredAtUtc ON AccessEvents (OccurredAtUtc DESC, EventId DESC);" +
                        "CREATE INDEX IX_AccessEvents_Domain ON AccessEvents (Domain);" +
                        "INSERT INTO Policies (PolicyId, Mode, Version, UpdatedAtUtc) " +
                        "VALUES (1, 1, 4, '2026-10-29T08:00:00.0000000Z');" +
                        "INSERT INTO Rules (RuleId, Domain, IncludeSubdomains, IsEnabled) " +
                        "VALUES ('" + Guid.NewGuid().ToString("D") + "', 'legacy.example.test', 1, 0);" +
                        "PRAGMA user_version = 1;";
                    command.ExecuteNonQuery();
                }
            }
        }

        private static int ReadSchemaVersion(string databasePath)
        {
            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = databasePath,
                Version = 3,
                Pooling = false
            };
            using (var connection = new SQLiteConnection(builder.ConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA user_version;";
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }

        private static ProxyAccessEvent CreateEvent(DateTime occurredAtUtc, string domain)
        {
            return new ProxyAccessEvent(
                Guid.NewGuid(),
                occurredAtUtc,
                domain,
                "GET",
                ProxyAccessResult.BlockedByPolicy,
                null,
                1);
        }

        private static void AssertTrue(bool condition)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Expected true.");
            }
        }

        private static void AssertFalse(bool condition)
        {
            if (condition)
            {
                throw new InvalidOperationException("Expected false.");
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

        private static void AssertThrows<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
        }

    }
}
