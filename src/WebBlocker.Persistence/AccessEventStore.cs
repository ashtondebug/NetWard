using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using WebBlocker.Shared;

namespace WebBlocker.Persistence
{
    public sealed class AccessEventStore
    {
        private const int CurrentSchemaVersion = 2;
        private readonly string _connectionString;

        public AccessEventStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentException("A database path is required.", "databasePath");
            }

            DatabasePath = Path.GetFullPath(databasePath);
            var directory = Path.GetDirectoryName(DatabasePath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new ArgumentException("The database path must have a parent directory.", "databasePath");
            }

            Directory.CreateDirectory(directory);
            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Version = 3,
                Pooling = false,
                FailIfMissing = false,
                DefaultTimeout = 5
            };
            _connectionString = builder.ConnectionString;
            Initialize();
        }

        public string DatabasePath { get; private set; }

        public void BackupTo(string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException("A backup destination path is required.", "destinationPath");
            }

            var fullDestinationPath = Path.GetFullPath(destinationPath);
            if (string.Equals(DatabasePath, fullDestinationPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The backup destination must differ from the source database.");
            }

            if (File.Exists(fullDestinationPath) || Directory.Exists(fullDestinationPath))
            {
                throw new IOException("The backup destination already exists.");
            }

            var destinationDirectory = Path.GetDirectoryName(fullDestinationPath);
            if (string.IsNullOrEmpty(destinationDirectory))
            {
                throw new ArgumentException("The backup destination must have a parent directory.", "destinationPath");
            }

            Directory.CreateDirectory(destinationDirectory);
            var temporaryPath = fullDestinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
            var destinationBuilder = new SQLiteConnectionStringBuilder
            {
                DataSource = temporaryPath,
                Version = 3,
                Pooling = false,
                FailIfMissing = false,
                DefaultTimeout = 5
            };

            try
            {
                using (var source = OpenConnection())
                using (var destination = new SQLiteConnection(destinationBuilder.ConnectionString))
                {
                    destination.Open();
                    source.BackupDatabase(destination, "main", "main", -1, null, 0);
                }

                File.Move(temporaryPath, fullDestinationPath);
            }
            catch
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                throw;
            }
        }

        public int PruneAccessEventsBefore(DateTime occurredBeforeUtc)
        {
            if (occurredBeforeUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("The retention cutoff must be UTC.", "occurredBeforeUtc");
            }

            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            using (var command = CreateCommand(connection, transaction,
                "DELETE FROM AccessEvents WHERE OccurredAtUtc < @cutoffUtc;"))
            {
                AddParameter(command, "@cutoffUtc", occurredBeforeUtc.ToString("o", CultureInfo.InvariantCulture));
                var deletedCount = command.ExecuteNonQuery();
                transaction.Commit();
                return deletedCount;
            }
        }

        public long SavePolicy(AccessPolicy policy)
        {
            if (policy == null)
            {
                throw new ArgumentNullException("policy");
            }

            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                var version = SavePolicyInTransaction(connection, transaction, policy);
                transaction.Commit();
                return version;
            }
        }

        public IList<PolicyHistoryEntry> QueryPolicyHistory(int pageNumber, int pageSize)
        {
            if (pageNumber < 1)
            {
                throw new ArgumentOutOfRangeException("pageNumber");
            }

            if (pageSize < 1 || pageSize > 100)
            {
                throw new ArgumentOutOfRangeException("pageSize", "Policy history page size must be between 1 and 100.");
            }

            var offset = checked((pageNumber - 1) * pageSize);
            var entries = new List<PolicyHistoryEntry>();
            using (var connection = OpenConnection())
            using (var command = CreateCommand(connection, null,
                "SELECT V.Version, V.Mode, V.UpdatedAtUtc, COUNT(R.RuleId) " +
                "FROM PolicyVersions V LEFT JOIN PolicyVersionRules R ON R.Version = V.Version " +
                "GROUP BY V.Version, V.Mode, V.UpdatedAtUtc " +
                "ORDER BY V.Version DESC LIMIT @pageSize OFFSET @offset;"))
            {
                AddParameter(command, "@pageSize", pageSize);
                AddParameter(command, "@offset", offset);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var modeValue = reader.GetInt32(1);
                        if (modeValue != (int)AccessMode.AllowList && modeValue != (int)AccessMode.BlockList)
                        {
                            throw new InvalidDataException("The database contains an unsupported historical policy mode.");
                        }

                        DateTime updatedAtUtc;
                        if (!DateTime.TryParseExact(reader.GetString(2), "o", CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out updatedAtUtc) ||
                            updatedAtUtc.Kind != DateTimeKind.Utc)
                        {
                            throw new InvalidDataException("The database contains an invalid policy history timestamp.");
                        }

                        entries.Add(new PolicyHistoryEntry(
                            reader.GetInt64(0),
                            (AccessMode)modeValue,
                            updatedAtUtc,
                            reader.GetInt32(3)));
                    }
                }
            }

            return entries.AsReadOnly();
        }

        public long RestorePolicy(long historicalVersion)
        {
            if (historicalVersion < 1)
            {
                throw new ArgumentOutOfRangeException("historicalVersion");
            }

            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                AccessMode mode;
                using (var command = CreateCommand(connection, transaction,
                    "SELECT Mode FROM PolicyVersions WHERE Version = @version;"))
                {
                    AddParameter(command, "@version", historicalVersion);
                    var value = command.ExecuteScalar();
                    if (value == null || value == DBNull.Value)
                    {
                        throw new KeyNotFoundException("The requested policy version does not exist.");
                    }

                    var modeValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    if (modeValue != (int)AccessMode.AllowList && modeValue != (int)AccessMode.BlockList)
                    {
                        throw new InvalidDataException("The database contains an unsupported historical policy mode.");
                    }

                    mode = (AccessMode)modeValue;
                }

                var rules = new List<DomainRule>();
                using (var command = CreateCommand(connection, transaction,
                    "SELECT RuleId, Domain, IncludeSubdomains, IsEnabled " +
                    "FROM PolicyVersionRules WHERE Version = @version ORDER BY Ordinal;"))
                {
                    AddParameter(command, "@version", historicalVersion);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Guid ruleId;
                            if (!Guid.TryParse(reader.GetString(0), out ruleId))
                            {
                                throw new InvalidDataException("The database contains an invalid historical rule ID.");
                            }

                            rules.Add(new DomainRule(
                                ruleId,
                                reader.GetString(1),
                                reader.GetInt32(2) != 0,
                                reader.GetInt32(3) != 0));
                        }
                    }
                }

                var restoredVersion = SavePolicyInTransaction(
                    connection,
                    transaction,
                    new AccessPolicy(mode, rules));
                transaction.Commit();
                return restoredVersion;
            }
        }

        public bool TryLoadPolicy(out PolicySnapshot snapshot)
        {
            using (var connection = OpenConnection())
            {
                AccessMode mode;
                long version;
                using (var command = CreateCommand(connection, null,
                    "SELECT Mode, Version FROM Policies WHERE PolicyId = 1;"))
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        snapshot = null;
                        return false;
                    }

                    var modeValue = reader.GetInt32(0);
                    if (modeValue != (int)AccessMode.AllowList && modeValue != (int)AccessMode.BlockList)
                    {
                        throw new InvalidDataException("The database contains an unsupported access policy mode.");
                    }

                    mode = (AccessMode)modeValue;
                    version = reader.GetInt64(1);
                }

                var rules = new List<DomainRule>();
                using (var command = CreateCommand(connection, null,
                    "SELECT RuleId, Domain, IncludeSubdomains, IsEnabled FROM Rules ORDER BY rowid;"))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Guid ruleId;
                        if (!Guid.TryParse(reader.GetString(0), out ruleId))
                        {
                            throw new InvalidDataException("The database contains an invalid rule ID.");
                        }

                        rules.Add(new DomainRule(
                            ruleId,
                            reader.GetString(1),
                            reader.GetInt32(2) != 0,
                            reader.GetInt32(3) != 0));
                    }
                }

                snapshot = new PolicySnapshot(new AccessPolicy(mode, rules), version);
                return true;
            }
        }

        public bool RecordAccessEvent(ProxyAccessEvent accessEvent)
        {
            if (accessEvent == null)
            {
                throw new ArgumentNullException("accessEvent");
            }

            using (var connection = OpenConnection())
            using (var command = CreateCommand(connection, null,
                "INSERT OR IGNORE INTO AccessEvents " +
                "(EventId, OccurredAtUtc, Domain, Method, Result, MatchedRuleId, PolicyVersion) " +
                "VALUES (@eventId, @occurredAtUtc, @domain, @method, @result, @matchedRuleId, @policyVersion);"))
            {
                AddParameter(command, "@eventId", accessEvent.EventId.ToString("D"));
                AddParameter(command, "@occurredAtUtc",
                    accessEvent.OccurredAtUtc.ToString("o", CultureInfo.InvariantCulture));
                AddParameter(command, "@domain", accessEvent.Domain);
                AddParameter(command, "@method", accessEvent.Method);
                AddParameter(command, "@result", (int)accessEvent.Result);
                AddParameter(command, "@matchedRuleId",
                    accessEvent.MatchedRuleId.HasValue
                        ? (object)accessEvent.MatchedRuleId.Value.ToString("D")
                        : DBNull.Value);
                AddParameter(command, "@policyVersion", accessEvent.PolicyVersion);
                return command.ExecuteNonQuery() == 1;
            }
        }

        public void UpdateAccessEventResult(Guid eventId, ProxyAccessResult result)
        {
            if (eventId == Guid.Empty)
            {
                throw new ArgumentException("An event ID is required.", "eventId");
            }

            if (result != ProxyAccessResult.AllowedByPolicy &&
                result != ProxyAccessResult.BlockedByPolicy &&
                result != ProxyAccessResult.BlockPageFailure)
            {
                throw new ArgumentOutOfRangeException("result");
            }

            using (var connection = OpenConnection())
            using (var command = CreateCommand(connection, null,
                "UPDATE AccessEvents SET Result = @result WHERE EventId = @eventId;"))
            {
                AddParameter(command, "@result", (int)result);
                AddParameter(command, "@eventId", eventId.ToString("D"));
                if (command.ExecuteNonQuery() != 1)
                {
                    throw new InvalidOperationException("The access event could not be updated because it does not exist.");
                }
            }
        }

        public AccessEventPage QueryAccessEvents(
            bool onlyBlocked,
            string domainContains,
            int pageNumber,
            int pageSize)
        {
            if (pageNumber < 1)
            {
                throw new ArgumentOutOfRangeException("pageNumber");
            }

            if (pageSize < 1 || pageSize > 500)
            {
                throw new ArgumentOutOfRangeException("pageSize", "Page size must be between 1 and 500.");
            }

            var where = " WHERE 1 = 1 ";
            if (onlyBlocked)
            {
                where += " AND Result <> @allowedResult ";
            }

            var normalizedSearch = string.IsNullOrWhiteSpace(domainContains)
                ? null
                : domainContains.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(normalizedSearch))
            {
                where += " AND Domain LIKE @domain ESCAPE '\\' ";
                normalizedSearch = normalizedSearch
                    .Replace("\\", "\\\\")
                    .Replace("%", "\\%")
                    .Replace("_", "\\_");
            }

            using (var connection = OpenConnection())
            {
                long totalCount;
                using (var command = CreateCommand(connection, null, "SELECT COUNT(*) FROM AccessEvents" + where + ";"))
                {
                    BindQueryParameters(command, onlyBlocked, normalizedSearch);
                    totalCount = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                }

                var events = new List<ProxyAccessEvent>();
                var offset = checked((pageNumber - 1) * pageSize);
                using (var command = CreateCommand(connection, null,
                    "SELECT EventId, OccurredAtUtc, Domain, Method, Result, MatchedRuleId, PolicyVersion " +
                    "FROM AccessEvents" + where + " ORDER BY OccurredAtUtc DESC, EventId DESC " +
                    "LIMIT @pageSize OFFSET @offset;"))
                {
                    BindQueryParameters(command, onlyBlocked, normalizedSearch);
                    AddParameter(command, "@pageSize", pageSize);
                    AddParameter(command, "@offset", offset);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Guid eventId;
                            if (!Guid.TryParse(reader.GetString(0), out eventId))
                            {
                                throw new InvalidDataException("The database contains an invalid access event ID.");
                            }

                            Guid? ruleId = null;
                            if (!reader.IsDBNull(5))
                            {
                                Guid parsedRuleId;
                                if (!Guid.TryParse(reader.GetString(5), out parsedRuleId))
                                {
                                    throw new InvalidDataException("The database contains an invalid matched rule ID.");
                                }

                                ruleId = parsedRuleId;
                            }

                            DateTime occurredAtUtc;
                            if (!DateTime.TryParseExact(reader.GetString(1), "o", CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind, out occurredAtUtc) ||
                                occurredAtUtc.Kind != DateTimeKind.Utc)
                            {
                                throw new InvalidDataException("The database contains an invalid UTC event timestamp.");
                            }

                            var result = reader.GetInt32(4);
                            if (result < (int)ProxyAccessResult.AllowedByPolicy ||
                                result > (int)ProxyAccessResult.BlockPageFailure)
                            {
                                throw new InvalidDataException("The database contains an unsupported access result.");
                            }

                            events.Add(new ProxyAccessEvent(
                                eventId,
                                occurredAtUtc,
                                reader.GetString(2),
                                reader.GetString(3),
                                (ProxyAccessResult)result,
                                ruleId,
                                reader.GetInt64(6)));
                        }
                    }
                }

                return new AccessEventPage(events.AsReadOnly(), totalCount);
            }
        }

        private static long SavePolicyInTransaction(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            AccessPolicy policy)
        {
            long version;
            using (var command = CreateCommand(connection, transaction,
                "SELECT Version FROM Policies WHERE PolicyId = 1;"))
            {
                var current = command.ExecuteScalar();
                version = current == null || current == DBNull.Value
                    ? 1
                    : checked(Convert.ToInt64(current, CultureInfo.InvariantCulture) + 1);
            }

            var updatedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            using (var command = CreateCommand(connection, transaction,
                "INSERT OR REPLACE INTO Policies (PolicyId, Mode, Version, UpdatedAtUtc) " +
                "VALUES (1, @mode, @version, @updatedAtUtc);"))
            {
                AddParameter(command, "@mode", (int)policy.Mode);
                AddParameter(command, "@version", version);
                AddParameter(command, "@updatedAtUtc", updatedAtUtc);
                command.ExecuteNonQuery();
            }

            using (var command = CreateCommand(connection, transaction, "DELETE FROM Rules;"))
            {
                command.ExecuteNonQuery();
            }

            using (var command = CreateCommand(connection, transaction,
                "INSERT INTO PolicyVersions (Version, Mode, UpdatedAtUtc) VALUES (@version, @mode, @updatedAtUtc);"))
            {
                AddParameter(command, "@version", version);
                AddParameter(command, "@mode", (int)policy.Mode);
                AddParameter(command, "@updatedAtUtc", updatedAtUtc);
                command.ExecuteNonQuery();
            }

            for (var index = 0; index < policy.Rules.Count; index++)
            {
                var rule = policy.Rules[index];
                using (var command = CreateCommand(connection, transaction,
                    "INSERT INTO Rules (RuleId, Domain, IncludeSubdomains, IsEnabled) " +
                    "VALUES (@ruleId, @domain, @includeSubdomains, @isEnabled);"))
                {
                    AddParameter(command, "@ruleId", rule.Id.ToString("D"));
                    AddParameter(command, "@domain", rule.Domain);
                    AddParameter(command, "@includeSubdomains", rule.IncludeSubdomains ? 1 : 0);
                    AddParameter(command, "@isEnabled", rule.IsEnabled ? 1 : 0);
                    command.ExecuteNonQuery();
                }

                using (var command = CreateCommand(connection, transaction,
                    "INSERT INTO PolicyVersionRules " +
                    "(Version, RuleId, Domain, IncludeSubdomains, IsEnabled, Ordinal) " +
                    "VALUES (@version, @ruleId, @domain, @includeSubdomains, @isEnabled, @ordinal);"))
                {
                    AddParameter(command, "@version", version);
                    AddParameter(command, "@ruleId", rule.Id.ToString("D"));
                    AddParameter(command, "@domain", rule.Domain);
                    AddParameter(command, "@includeSubdomains", rule.IncludeSubdomains ? 1 : 0);
                    AddParameter(command, "@isEnabled", rule.IsEnabled ? 1 : 0);
                    AddParameter(command, "@ordinal", index);
                    command.ExecuteNonQuery();
                }
            }

            return version;
        }

        private void Initialize()
        {
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA user_version;";
                var schemaVersion = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                if (schemaVersion > CurrentSchemaVersion)
                {
                    throw new InvalidDataException("The database schema is newer than this application supports.");
                }

                if (schemaVersion == CurrentSchemaVersion)
                {
                    return;
                }

                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    while (schemaVersion < CurrentSchemaVersion)
                    {
                        if (schemaVersion == 0)
                        {
                            using (var migration = CreateCommand(connection, transaction,
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
                                "CREATE INDEX IX_AccessEvents_Domain ON AccessEvents (Domain);"))
                            {
                                migration.ExecuteNonQuery();
                            }

                            schemaVersion = 1;
                        }
                        else if (schemaVersion == 1)
                        {
                            using (var migration = CreateCommand(connection, transaction,
                                "CREATE TABLE PolicyVersions (" +
                                "Version INTEGER NOT NULL PRIMARY KEY CHECK (Version >= 1), " +
                                "Mode INTEGER NOT NULL CHECK (Mode IN (0, 1)), " +
                                "UpdatedAtUtc TEXT NOT NULL);" +
                                "CREATE TABLE PolicyVersionRules (" +
                                "Version INTEGER NOT NULL, " +
                                "RuleId TEXT NOT NULL, " +
                                "Domain TEXT NOT NULL, " +
                                "IncludeSubdomains INTEGER NOT NULL CHECK (IncludeSubdomains IN (0, 1)), " +
                                "IsEnabled INTEGER NOT NULL CHECK (IsEnabled IN (0, 1)), " +
                                "Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0), " +
                                "PRIMARY KEY (Version, RuleId), " +
                                "UNIQUE (Version, Ordinal), " +
                                "UNIQUE (Version, Domain, IncludeSubdomains), " +
                                "FOREIGN KEY (Version) REFERENCES PolicyVersions (Version) ON DELETE CASCADE);" +
                                "INSERT INTO PolicyVersions (Version, Mode, UpdatedAtUtc) " +
                                "SELECT Version, Mode, UpdatedAtUtc FROM Policies WHERE PolicyId = 1;" +
                                "INSERT INTO PolicyVersionRules " +
                                "(Version, RuleId, Domain, IncludeSubdomains, IsEnabled, Ordinal) " +
                                "SELECT P.Version, R.RuleId, R.Domain, R.IncludeSubdomains, R.IsEnabled, R.rowid " +
                                "FROM Policies P CROSS JOIN Rules R WHERE P.PolicyId = 1 ORDER BY R.rowid;"))
                            {
                                migration.ExecuteNonQuery();
                            }

                            schemaVersion = 2;
                        }
                        else
                        {
                            throw new InvalidDataException("No migration is available for database schema version " + schemaVersion + ".");
                        }

                        using (var migration = CreateCommand(connection, transaction,
                            "PRAGMA user_version = " + schemaVersion.ToString(CultureInfo.InvariantCulture) + ";"))
                        {
                            migration.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                }
            }
        }

        private SQLiteConnection OpenConnection()
        {
            var connection = new SQLiteConnection(_connectionString);
            try
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA synchronous = FULL; PRAGMA busy_timeout = 5000;";
                    command.ExecuteNonQuery();
                }

                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private static SQLiteCommand CreateCommand(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string commandText)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;
            return command;
        }

        private static void AddParameter(SQLiteCommand command, string name, object value)
        {
            command.Parameters.AddWithValue(name, value);
        }

        private static void BindQueryParameters(
            SQLiteCommand command,
            bool onlyBlocked,
            string normalizedSearch)
        {
            if (onlyBlocked)
            {
                AddParameter(command, "@allowedResult", (int)ProxyAccessResult.AllowedByPolicy);
            }

            if (!string.IsNullOrEmpty(normalizedSearch))
            {
                AddParameter(command, "@domain", "%" + normalizedSearch + "%");
            }
        }
    }
}
