using System;
using System.IO;

namespace WebBlocker.Agent
{
    internal static class AgentPaths
    {
        /// <summary>
        /// Keeps the legacy WebBlocker folder name so existing policy data survives the product rename.
        /// </summary>
        public const string DataFolderName = "WebBlocker";

        public const string DatabaseFileName = "proxy.db";

        public static string GetDefaultDatabasePath()
        {
            var applicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(applicationDataPath))
            {
                throw new InvalidOperationException("The current user's local application-data directory is unavailable.");
            }

            return Path.Combine(applicationDataPath, DataFolderName, DatabaseFileName);
        }

        /// <summary>
        /// Per-session browser profile. It is separate from the user's normal profile so the explicit proxy
        /// argument cannot leak into ordinary browsing, and it can be deleted when a session ends.
        /// </summary>
        public static string GetManagedProfileRoot()
        {
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localApplicationData, DataFolderName, "managed-browser");
        }

        public static string ResolveDatabasePath(string[] args)
        {
            var databasePath = GetDefaultDatabasePath();
            if (args == null)
            {
                return databasePath;
            }

            for (var index = 0; index < args.Length; index++)
            {
                var argument = args[index];
                if (!string.Equals(argument, "--database", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("用法: NetWard.Agent.exe [--database path]");
                }

                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("--database 需要一个文件路径。");
                }

                databasePath = args[++index];
            }

            return Path.GetFullPath(databasePath);
        }
    }
}