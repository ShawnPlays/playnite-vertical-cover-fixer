using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using VerticalCoverFixer.Core;

namespace VerticalCoverFixer
{
    internal enum FixOutcome
    {
        Replaced,
        NotFound,
        Failed,
    }

    internal sealed class FixResult
    {
        public CoverCheck Check { get; set; }

        public FixOutcome Outcome { get; set; }

        public string Source { get; set; }

        public int NewWidth { get; set; }

        public int NewHeight { get; set; }

        /// <summary>Where the old cover was copied to, if there was one.</summary>
        public string BackupPath { get; set; }

        public string Note { get; set; }
    }

    /// <summary>Replaces one game's cover at a time, backing up the old one first.</summary>
    internal sealed class CoverFixer : IDisposable
    {
        private readonly IPlayniteAPI api;
        private readonly CoverDownloader downloader;
        private readonly string backupFolder;

        /// <param name="backupFolder">Created on the first backup, so a run that changes nothing leaves no empty folder.</param>
        public CoverFixer(IPlayniteAPI api, VerticalCoverFixerSettings settings, string backupFolder)
        {
            this.api = api;
            this.backupFolder = backupFolder;
            downloader = new CoverDownloader(settings);
        }

        public void Dispose() => downloader.Dispose();

        public FixResult Fix(CoverCheck check, CancellationToken cancel)
        {
            var notes = new List<string>();
            int rejected = 0;

            foreach (var candidate in downloader.FindCandidates(check.Game, notes, cancel))
            {
                cancel.ThrowIfCancellationRequested();

                var bytes = downloader.TryDownload(candidate.Url, cancel);
                if (bytes == null)
                {
                    continue; // e.g. Steam has no art at this address; try the next one
                }

                // Don't trust the listing: only use what really is a vertical image.
                var info = ImageHeader.TryRead(bytes);
                if (info == null || info.Value.Shape != CoverShape.Vertical)
                {
                    rejected++;
                    continue;
                }

                return new FixResult
                {
                    Check = check,
                    Outcome = FixOutcome.Replaced,
                    Source = candidate.Source,
                    NewWidth = info.Value.Width,
                    NewHeight = info.Value.Height,
                    BackupPath = Apply(check, bytes, info.Value),
                };
            }

            if (rejected > 0)
            {
                notes.Add($"{rejected} downloaded image(s) weren't vertical");
            }

            return new FixResult
            {
                Check = check,
                Outcome = FixOutcome.NotFound,
                Note = notes.Count > 0 ? string.Join("; ", notes) : "No vertical cover found",
            };
        }

        /// <summary>Sets the new cover and returns where the old one was backed up to (or null).</summary>
        private string Apply(CoverCheck check, byte[] bytes, ImageInfo info)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"VerticalCoverFixer_{Guid.NewGuid():N}{info.Extension}");
            File.WriteAllBytes(tempFile, bytes);
            try
            {
                return OnUiThread(() =>
                {
                    var db = api.Database;
                    var game = db.Games.Get(check.Game.Id) ?? throw new InvalidOperationException("The game is no longer in the library");

                    string oldCover = game.CoverImage;
                    string backup = Backup(check);

                    game.CoverImage = db.AddFile(tempFile, game.Id); // Playnite copies the file into its library folder
                    db.Games.Update(game);

                    // Playnite's own editor deletes a replaced cover file too. Covers outside its folder are left alone.
                    if (CoverCheck.IsDatabaseFile(oldCover) && !string.Equals(oldCover, game.CoverImage, StringComparison.OrdinalIgnoreCase))
                    {
                        db.RemoveFile(oldCover);
                    }

                    return backup;
                });
            }
            finally
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch (IOException)
                {
                    // Only a temp file; Windows clears these out eventually.
                }
            }
        }

        private string Backup(CoverCheck check)
        {
            if (string.IsNullOrEmpty(check.CoverPath) || !File.Exists(check.CoverPath))
            {
                return null;
            }

            Directory.CreateDirectory(backupFolder);
            string name = $"{SafeFileName(check.Game.Name)} [{check.Game.Id}]{Path.GetExtension(check.CoverPath)}";
            string backupPath = Path.Combine(backupFolder, name);
            File.Copy(check.CoverPath, backupPath, overwrite: true);
            return backupPath;
        }

        private T OnUiThread<T>(Func<T> action)
        {
            var dispatcher = api.MainView.UIDispatcher;
            return dispatcher.CheckAccess() ? action() : (T)dispatcher.Invoke(action);
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string((name ?? "Untitled").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return safe.Length > 80 ? safe.Substring(0, 80) : safe;
        }
    }
}
