using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using WebBlocker.Shared;

namespace WebBlocker.Administrator
{
    /// <summary>
    /// Parses owner-reviewed Markdown category files into categories and normalized domain rules.
    /// The expected shape is one "## Category name" heading followed by "- domain[ include:subdomains]" lines.
    /// Lines that cannot be normalized are reported instead of being silently dropped or invented.
    /// </summary>
    internal sealed class CategoryListParser
    {
        // A single "#" heading is the document title; only "##" (or deeper) headings start a category.
        private static readonly Regex CategoryHeading = new Regex(
            @"^\s{0,3}#{2,6}\s+(?<name>.+?)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex CategoryTitle = new Regex(
            @"^\s{0,3}#\s+\S",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex DomainEntry = new Regex(
            @"^\s*(?:[-*+]|\d+[.)])\s+(?<value>.+?)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public CategoryImportResult Parse(string filePath, string sourceName)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A Markdown file path is required.", "filePath");
            }

            var text = File.ReadAllText(filePath, Encoding.UTF8);
            return ParseText(text, sourceName ?? Path.GetFileName(filePath));
        }

        public CategoryImportResult ParseText(string text, string sourceName)
        {
            if (text == null)
            {
                throw new ArgumentNullException("text");
            }

            var result = new CategoryImportResult();
            var categories = new List<CategoryConfig>();
            var rules = new List<CategoryRule>();
            var categoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ruleKeys = new HashSet<string>(StringComparer.Ordinal);
            string currentCategory = null;

            var lineNumber = 0;
            foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
            {
                lineNumber++;
                var line = rawLine.TrimEnd();

                var headingMatch = CategoryHeading.Match(line);
                if (headingMatch.Success)
                {
                    var name = headingMatch.Groups["name"].Value.Trim();
                    if (name.Length == 0)
                    {
                        result.Errors.Add("第 " + lineNumber + " 行：标题下缺少类别名称。");
                        continue;
                    }

                    if (!categoryNames.Add(name))
                    {
                        result.Errors.Add("第 " + lineNumber + " 行：类别「" + name + "」重复，已忽略。");
                        currentCategory = null;
                        continue;
                    }

                    categories.Add(new CategoryConfig(name, sourceName, false));
                    currentCategory = name;
                    continue;
                }

                if (CategoryTitle.IsMatch(line))
                {
                    // The document title itself is not a category.
                    currentCategory = null;
                    continue;
                }

                var entryMatch = DomainEntry.Match(line);
                if (!entryMatch.Success)
                {
                    continue;
                }

                if (currentCategory == null)
                {
                    result.Errors.Add("第 " + lineNumber + " 行：域名「" + line.Trim() + "」前缺少类别标题，已忽略。");
                    continue;
                }

                var rawValue = entryMatch.Groups["value"].Value.Trim();
                if (rawValue.Length == 0 || rawValue.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                var includeSubdomains = false;
                if (rawValue.EndsWith(" include:subdomains", StringComparison.OrdinalIgnoreCase))
                {
                    includeSubdomains = true;
                    rawValue = rawValue.Substring(0, rawValue.Length - " include:subdomains".Length).Trim();
                }

                // Only a bare hostname (or a leading "*." wildcard, which is treated as include-subdomains)
                // is accepted. URLs, paths and wildcards in the middle are rejected rather than guessed at.
                if (rawValue.StartsWith("*.", StringComparison.Ordinal))
                {
                    includeSubdomains = true;
                    rawValue = rawValue.Substring(2);
                }

                string normalized;
                try
                {
                    normalized = DomainName.Normalize(rawValue);
                }
                catch (ArgumentException)
                {
                    result.Errors.Add("第 " + lineNumber + " 行：无法识别的域名「" + rawValue + "」，已忽略。");
                    continue;
                }

                var key = currentCategory + "|" + normalized + "|" + includeSubdomains;
                if (!ruleKeys.Add(key))
                {
                    result.Duplicates++;
                    continue;
                }

                rules.Add(new CategoryRule(
                    Guid.NewGuid(),
                    currentCategory,
                    normalized,
                    includeSubdomains,
                    true));
            }

            if (categories.Count == 0)
            {
                result.Errors.Add("文件中没有找到任何「## 类别名称」标题。");
            }

            result.Categories.AddRange(categories);
            result.Rules.AddRange(rules);
            return result;
        }
    }

    internal sealed class CategoryImportResult
    {
        public CategoryImportResult()
        {
            Categories = new List<CategoryConfig>();
            Rules = new List<CategoryRule>();
            Errors = new List<string>();
        }

        public List<CategoryConfig> Categories { get; private set; }

        public List<CategoryRule> Rules { get; private set; }

        public List<string> Errors { get; private set; }

        public int Duplicates { get; set; }

        public bool HasContent
        {
            get { return Categories.Count > 0; }
        }
    }
}