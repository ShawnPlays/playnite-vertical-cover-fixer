using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.IO;
using VerticalCoverFixer.Core;

namespace VerticalCoverFixer
{
    /// <summary>One game's cover, as found by a check.</summary>
    internal sealed class CoverCheck
    {
        /// <summary>An unreadable file smaller than this can't be a real cover in some unusual format.</summary>
        private const long BrokenFileMaxBytes = 1024;

        public Game Game { get; set; }

        /// <summary>Library name (Steam, GOG, …), captured on the UI thread so reports can use it from any thread.</summary>
        public string Library { get; set; }

        /// <summary>Game.CoverImage as stored: a database file id, a full path, or a web address.</summary>
        public string CoverImage { get; set; }

        /// <summary>The cover's full path on disk, when it has one.</summary>
        public string CoverPath { get; set; }

        public CoverShape Shape { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public string Note { get; set; }

        /// <summary>Reads the game's details. Call on Playnite's UI thread, where it expects database access.</summary>
        public static CoverCheck Snapshot(IGameDatabaseAPI db, Game game)
        {
            var check = new CoverCheck
            {
                Game = game,
                Library = game.Source?.Name ?? string.Empty,
                CoverImage = game.CoverImage,
            };

            if (!string.IsNullOrWhiteSpace(game.CoverImage) && !IsWebAddress(game.CoverImage))
            {
                check.CoverPath = Path.IsPathRooted(game.CoverImage) ? game.CoverImage : db.GetFullFilePath(game.CoverImage);
            }

            return check;
        }

        /// <summary>Works out the cover's shape from its file. Safe to call off the UI thread.</summary>
        public void Inspect()
        {
            if (string.IsNullOrWhiteSpace(CoverImage))
            {
                Shape = CoverShape.Missing;
                Note = "No cover set";
                return;
            }

            if (IsWebAddress(CoverImage))
            {
                Shape = CoverShape.Unknown;
                Note = "Cover is a web address that Playnite hasn't downloaded";
                return;
            }

            if (!File.Exists(CoverPath))
            {
                Shape = CoverShape.Missing;
                Note = "Cover file not found on disk";
                return;
            }

            var info = ImageHeader.TryRead(CoverPath);
            if (info == null && new FileInfo(CoverPath).Length < BrokenFileMaxBytes)
            {
                // Empty or junk files turn up now and then (a failed download saved as-is), and are as good as no cover.
                Shape = CoverShape.Missing;
                Note = "Cover file is empty or damaged";
                return;
            }

            if (info == null)
            {
                Shape = CoverShape.Unknown;
                Note = "Couldn't read the image's size (unusual file format)";
                return;
            }

            Width = info.Value.Width;
            Height = info.Value.Height;
            Shape = info.Value.Shape;
        }

        /// <summary>True for covers stored in Playnite's own library folder, which Playnite expects us to clean up.</summary>
        public static bool IsDatabaseFile(string coverImage) =>
            !string.IsNullOrWhiteSpace(coverImage) && !IsWebAddress(coverImage) && !Path.IsPathRooted(coverImage);

        private static bool IsWebAddress(string value) =>
            value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }
}
