using System;
using System.IO;
using WebBlocker.Persistence;

namespace WebBlocker.Filtering
{
    public enum BlockPageKind
    {
        None = 0,
        Block = 1,
        Timeout = 2,
        Violation = 3
    }

    /// <summary>
    /// Resolves the bundled local HTML page that represents each proxy decision.
    /// The pages are static files next to the executable:
    /// block.html (allowlist/blocklist denial), timeout.html (timed access exhausted) and
    /// violation.html (blocked-content category match).
    /// </summary>
    public sealed class BlockPageCatalog
    {
        public const string BlockFileName = "block.html";
        public const string TimeoutFileName = "timeout.html";
        public const string ViolationFileName = "violation.html";

        public BlockPageCatalog(string blockPagePath, string timeoutPagePath, string violationPagePath)
        {
            if (string.IsNullOrWhiteSpace(blockPagePath))
            {
                throw new ArgumentException("A block page path is required.", "blockPagePath");
            }

            if (string.IsNullOrWhiteSpace(timeoutPagePath))
            {
                throw new ArgumentException("A timeout page path is required.", "timeoutPagePath");
            }

            BlockPagePath = Path.GetFullPath(blockPagePath);
            TimeoutPagePath = Path.GetFullPath(timeoutPagePath);
            ViolationPagePath = string.IsNullOrWhiteSpace(violationPagePath)
                ? null
                : Path.GetFullPath(violationPagePath);
        }

        public string BlockPagePath { get; private set; }

        public string TimeoutPagePath { get; private set; }

        /// <summary>
        /// Optional. When null and a category match occurs, the block page is served instead so the
        /// administrator still sees a denial, but the alpha build always ships this page.
        /// </summary>
        public string ViolationPagePath { get; private set; }

        public static BlockPageCatalog CreateDefault(string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                throw new ArgumentException("A base directory is required.", "baseDirectory");
            }

            return new BlockPageCatalog(
                Path.Combine(baseDirectory, BlockFileName),
                Path.Combine(baseDirectory, TimeoutFileName),
                Path.Combine(baseDirectory, ViolationFileName));
        }

        /// <summary>
        /// Verifies that every required page exists before the proxy accepts traffic. A missing page is a
        /// configuration error, not a per-request failure: without it a denied request would have to
        /// masquerade as an error response.
        /// </summary>
        public void Validate()
        {
            EnsureExists(BlockPagePath, "block");
            EnsureExists(TimeoutPagePath, "timeout");
            if (ViolationPagePath != null)
            {
                EnsureExists(ViolationPagePath, "violation");
            }
        }

        public BlockPageKind Resolve(ProxyAccessResult result)
        {
            switch (result)
            {
                case ProxyAccessResult.BlockedByTimeLimit:
                    return BlockPageKind.Timeout;
                case ProxyAccessResult.BlockedByCategory:
                    return ViolationPagePath == null ? BlockPageKind.Block : BlockPageKind.Violation;
                case ProxyAccessResult.BlockedByPolicy:
                case ProxyAccessResult.BlockPageFailure:
                    return BlockPageKind.Block;
                default:
                    return BlockPageKind.None;
            }
        }

        public string GetPath(BlockPageKind kind)
        {
            switch (kind)
            {
                case BlockPageKind.Timeout:
                    return TimeoutPagePath;
                case BlockPageKind.Violation:
                    return ViolationPagePath ?? BlockPagePath;
                case BlockPageKind.Block:
                    return BlockPagePath;
                default:
                    throw new ArgumentOutOfRangeException("kind");
            }
        }

        private static void EnsureExists(string path, string description)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "The bundled " + description + " page is missing. Expected a static HTML file at: " + path,
                    path);
            }
        }
    }
}