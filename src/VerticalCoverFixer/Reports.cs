using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VerticalCoverFixer
{
    /// <summary>Writes CSV reports that open straight in Excel.</summary>
    internal static class Reports
    {
        public static string WriteCheck(string folder, IEnumerable<CoverCheck> checks)
        {
            var rows = checks
                .OrderBy(c => c.Shape)
                .ThenBy(c => c.Game.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(c => new[]
                {
                    c.Game.Name, c.Library, c.Game.Hidden ? "Yes" : "", c.Shape.ToString(),
                    Size(c.Width, c.Height), c.CoverPath ?? c.CoverImage, c.Note,
                });

            return Write(folder, "cover-check",
                new[] { "Game", "Library", "Hidden", "Cover shape", "Size", "Cover file", "Note" }, rows);
        }

        public static string WriteFix(string folder, IEnumerable<FixResult> results)
        {
            var rows = results
                .OrderBy(r => r.Outcome)
                .ThenBy(r => r.Check.Game.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(r => new[]
                {
                    r.Check.Game.Name, r.Check.Library, r.Outcome.ToString(), r.Check.Shape.ToString(),
                    Size(r.NewWidth, r.NewHeight), r.Source, r.BackupPath, r.Note,
                });

            return Write(folder, "cover-fix",
                new[] { "Game", "Library", "Result", "Old shape", "New size", "New cover from", "Old cover backed up to", "Note" }, rows);
        }

        private static string Write(string folder, string prefix, string[] header, IEnumerable<string[]> rows)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"{prefix}-{DateTime.Now:yyyy-MM-dd_HHmmss}.csv");

            var csv = new StringBuilder();
            foreach (var row in new[] { header }.Concat(rows))
            {
                csv.AppendLine(string.Join(",", row.Select(Escape)));
            }

            // With a byte-order mark, Excel reads accented game names correctly.
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return path;
        }

        private static string Size(int width, int height) => width > 0 && height > 0 ? $"{width}x{height}" : "";

        private static string Escape(string value)
        {
            value = value ?? "";
            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }
    }
}
