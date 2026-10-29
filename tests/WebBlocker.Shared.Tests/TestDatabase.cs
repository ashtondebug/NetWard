using System;
using System.IO;

namespace WebBlocker.Shared.Tests
{
    internal sealed class TestDatabase : IDisposable
    {
        public TestDatabase()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "WebBlocker-tests-" + Guid.NewGuid().ToString("N"),
                "events.db");
            DirectoryPath = System.IO.Path.GetDirectoryName(Path);
            BackupPath = System.IO.Path.Combine(DirectoryPath, "events-backup.db");
        }

        public string Path { get; private set; }

        public string DirectoryPath { get; private set; }

        public string BackupPath { get; private set; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, true);
            }
        }
    }
}
