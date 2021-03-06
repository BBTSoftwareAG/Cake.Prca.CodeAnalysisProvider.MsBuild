﻿namespace Cake.Prca.Issues.MsBuild
{
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Xml.Linq;
    using Core.Diagnostics;

    /// <summary>
    /// MsBuild log format as written by the <c>XmlFileLogger</c> class from MSBuild Extension Pack.
    /// </summary>
    internal class XmlFileLoggerFormat : LogFileFormat
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="XmlFileLoggerFormat"/> class.
        /// </summary>
        /// <param name="log">The Cake log instance.</param>
        public XmlFileLoggerFormat(ICakeLog log)
            : base(log)
        {
        }

        /// <inheritdoc/>
        public override IEnumerable<ICodeAnalysisIssue> ReadIssues(
            PrcaSettings prcaSettings,
            MsBuildIssuesSettings settings)
        {
            prcaSettings.NotNull(nameof(prcaSettings));
            settings.NotNull(nameof(settings));

            var result = new List<ICodeAnalysisIssue>();

            var logDocument = XDocument.Parse(settings.LogFileContent);

            // Loop through all warning tags.
            foreach (var warning in logDocument.Descendants("warning"))
            {
                // Read affected file from the warning.
                string fileName;
                if (!this.TryGetFile(warning, prcaSettings, out fileName))
                {
                    continue;
                }

                // Read affected line from the warning.
                int? line;
                if (!TryGetLine(warning, out line))
                {
                    continue;
                }

                // Read rule code from the warning.
                string rule;
                if (!TryGetRule(warning, out rule))
                {
                    continue;
                }

                result.Add(new CodeAnalysisIssue<MsBuildIssuesProvider>(
                    fileName,
                    line,
                    warning.Value,
                    0,
                    rule,
                    MsBuildRuleUrlResolver.Instance.ResolveRuleUrl(rule)));
            }

            return result;
        }

        /// <summary>
        /// Reads the affected line from a warning logged in a MsBuild log.
        /// </summary>
        /// <param name="warning">Warning element from MsBuild log.</param>
        /// <param name="line">Returns line.</param>
        /// <returns>True if the line could be parsed.</returns>
        private static bool TryGetLine(XElement warning, out int? line)
        {
            line = null;

            var lineAttr = warning.Attribute("line");

            var lineValue = lineAttr?.Value;
            if (string.IsNullOrWhiteSpace(lineValue))
            {
                return false;
            }

            line = int.Parse(lineValue, CultureInfo.InvariantCulture);

            // Convert negative line numbers or line number 0 to null
            if (line <= 0)
            {
                line = null;
            }

            return true;
        }

        /// <summary>
        /// Reads the rule code from a warning logged in a MsBuild log.
        /// </summary>
        /// <param name="warning">Warning element from MsBuild log.</param>
        /// <param name="rule">Returns the code of the rule.</param>
        /// <returns>True if the rule code could be parsed.</returns>
        private static bool TryGetRule(XElement warning, out string rule)
        {
            rule = string.Empty;

            var codeAttr = warning.Attribute("code");
            if (codeAttr == null)
            {
                return false;
            }

            rule = codeAttr.Value;
            return !string.IsNullOrWhiteSpace(rule);
        }

        /// <summary>
        /// Reads the affected file path from a warning logged in a MsBuild log.
        /// </summary>
        /// <param name="warning">Warning element from MsBuild log.</param>
        /// <param name="prcaSettings">General settings to use.</param>
        /// <param name="fileName">Returns the full path to the affected file.</param>
        /// <returns>True if the file path could be parsed.</returns>
        private bool TryGetFile(
            XElement warning,
            PrcaSettings prcaSettings,
            out string fileName)
        {
            fileName = string.Empty;

            var fileAttr = warning.Attribute("file");
            if (fileAttr == null)
            {
                return true;
            }

            fileName = fileAttr.Value;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return true;
            }

            // If not absolute path, combine with file path from compile task.
            if (!fileName.IsFullPath())
            {
                var parentFileAttr = warning.Parent?.Attribute("file");
                if (parentFileAttr != null)
                {
                    var compileTaskDirectory = Path.GetDirectoryName(parentFileAttr.Value);
                    fileName = Path.Combine(compileTaskDirectory, fileName);
                }
            }

            // Ignore files from outside the repository.
            if (!fileName.IsSubpathOf(prcaSettings.RepositoryRoot.FullPath))
            {
                this.Log.Warning(
                    "Ignored issue for file '{0}' since it is outside the repository folder at {1}.",
                    fileName,
                    prcaSettings.RepositoryRoot);

                return false;
            }

            // Make path relative to repository root.
            fileName = fileName.Substring(prcaSettings.RepositoryRoot.FullPath.Length);

            // Remove leading directory separator.
            if (fileName.StartsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fileName = fileName.Substring(1);
            }

            return true;
        }
    }
}
