using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using VerticalCoverFixer.Core;

namespace VerticalCoverFixer
{
    /// <summary>
    /// Checks the shape of every game's cover art and swaps square, horizontal and missing covers for
    /// vertical box art from Steam or SteamGridDB. It runs inside Playnite because Playnite keeps its
    /// database locked while running, and only an add-on can change covers safely.
    /// </summary>
    public class VerticalCoverFixerPlugin : GenericPlugin
    {
        private const string Title = "Vertical Cover Fixer";
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly VerticalCoverFixerSettingsViewModel settingsViewModel;

        // Must match the GUID in extension.yaml's Id. Never change it after release.
        public override Guid Id { get; } = Guid.Parse("918bc684-6300-488c-8ad3-c42f62f9036c");

        public VerticalCoverFixerPlugin(IPlayniteAPI api) : base(api)
        {
            settingsViewModel = new VerticalCoverFixerSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };

            // .NET Framework 4.6.2 doesn't offer TLS 1.2 by default, and Steam and SteamGridDB require it.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        private VerticalCoverFixerSettings Settings => settingsViewModel.Settings;

        private string ReportsFolder => GetPluginUserDataPath();

        public override ISettings GetSettings(bool firstRunSettings) => settingsViewModel;

        public override UserControl GetSettingsView(bool firstRunSettings) => new VerticalCoverFixerSettingsView();

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            // "@" puts the section under Playnite's Extensions menu.
            const string section = "@" + Title;
            yield return new MainMenuItem
            {
                MenuSection = section,
                Description = "Check cover shapes (report only, changes nothing)",
                Action = _ => CheckCovers(),
            };
            yield return new MainMenuItem
            {
                MenuSection = section,
                Description = "Replace non-vertical and missing covers…",
                Action = _ => FixCovers(LibraryGames(), confirm: true),
            };
            yield return new MainMenuItem
            {
                MenuSection = section,
                Description = "Open reports and backups folder",
                Action = _ => OpenFolder(ReportsFolder),
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                MenuSection = Title,
                Description = "Replace with vertical cover art (if not vertical)",
                Action = a => FixCovers(a.Games, confirm: false),
            };
        }

        private List<Game> LibraryGames() =>
            PlayniteApi.Database.Games.Where(g => Settings.IncludeHidden || !g.Hidden).ToList();

        private void CheckCovers()
        {
            var checks = CheckShapes(LibraryGames());
            if (checks == null)
            {
                return;
            }

            string report = Reports.WriteCheck(ReportsFolder, checks);
            int toReplace = checks.Count(c => Settings.Rules.Applies(c.Shape));

            string message = $"Checked {checks.Count} games.\n\n{Summary(checks)}\n\n"
                             + $"With your current settings, {toReplace} covers would be replaced.\n\n"
                             + "Open the full report?";
            if (PlayniteApi.Dialogs.ShowMessage(message, Title, MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Open(report);
            }
        }

        private void FixCovers(IEnumerable<Game> games, bool confirm)
        {
            if (!Settings.HasSteamGridDbKey && !Settings.UseSteamArt)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    "There's nowhere to get covers from. Add a SteamGridDB API key, or turn on Steam's official art, "
                    + "in Add-ons → Generic → Vertical Cover Fixer.", Title);
                return;
            }

            var checks = CheckShapes(games.ToList());
            if (checks == null)
            {
                return;
            }

            var toFix = checks.Where(c => Settings.Rules.Applies(c.Shape)).ToList();
            if (toFix.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    checks.Count == 1 ? "This game's cover doesn't need replacing." : "No covers need replacing.", Title);
                return;
            }

            if (confirm)
            {
                string message = $"Replace {toFix.Count} covers with vertical cover art?\n\n{Summary(toFix)}\n\n"
                                 + "Your current covers are backed up first. You can stop at any time.";
                if (!Settings.HasSteamGridDbKey)
                {
                    message += "\n\nWithout a SteamGridDB API key, only Steam games can be fixed.";
                }

                if (PlayniteApi.Dialogs.ShowMessage(message, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var results = new List<FixResult>();
            string stopReason = null;
            string backupFolder = Path.Combine(ReportsFolder, "backups", DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));

            RunWithProgress("Finding vertical cover art…", progress =>
            {
                progress.ProgressMaxValue = toFix.Count;
                using (var fixer = new CoverFixer(PlayniteApi, Settings, backupFolder))
                {
                    foreach (var check in toFix)
                    {
                        if (progress.CancelToken.IsCancellationRequested)
                        {
                            stopReason = "You stopped it before it finished.";
                            break;
                        }

                        progress.Text = $"Finding vertical cover art… ({results.Count + 1} of {toFix.Count})\n{check.Game.Name}";
                        try
                        {
                            results.Add(fixer.Fix(check, progress.CancelToken));
                        }
                        catch (OperationCanceledException)
                        {
                            stopReason = "You stopped it before it finished.";
                            break;
                        }
                        catch (SteamGridDbKeyException ex)
                        {
                            stopReason = ex.Message;
                            break;
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, $"Vertical Cover Fixer: couldn't replace the cover for {check.Game.Name}");
                            results.Add(new FixResult { Check = check, Outcome = FixOutcome.Failed, Note = ex.Message });
                        }

                        progress.CurrentProgressValue = results.Count;
                    }
                }
            });

            if (results.Count == 0 && stopReason == null)
            {
                return;
            }

            string report = Reports.WriteFix(ReportsFolder, results);
            int replaced = results.Count(r => r.Outcome == FixOutcome.Replaced);
            int notFound = results.Count(r => r.Outcome == FixOutcome.NotFound);
            int failed = results.Count(r => r.Outcome == FixOutcome.Failed);

            string summary = $"Replaced {replaced} of {toFix.Count} covers.";
            if (notFound > 0)
            {
                summary += $"\nNo vertical cover found: {notFound}";
            }

            if (failed > 0)
            {
                summary += $"\nErrors: {failed}";
            }

            if (stopReason != null)
            {
                summary += $"\n\n{stopReason}";
            }

            if (PlayniteApi.Dialogs.ShowMessage(summary + "\n\nOpen the report?", Title, MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Open(report);
            }
        }

        /// <summary>Checks every game's cover shape behind a progress bar. Null if cancelled or it failed.</summary>
        private List<CoverCheck> CheckShapes(List<Game> games)
        {
            // Game details are read here on the UI thread; only the image files are read in the background.
            var checks = games.Select(g => CoverCheck.Snapshot(PlayniteApi.Database, g)).ToList();
            bool finished = false;

            RunWithProgress("Checking cover art…", progress =>
            {
                progress.ProgressMaxValue = checks.Count;
                for (int i = 0; i < checks.Count; i++)
                {
                    if (progress.CancelToken.IsCancellationRequested)
                    {
                        return;
                    }

                    checks[i].Inspect();
                    progress.CurrentProgressValue = i + 1;
                }

                finished = true;
            });

            return finished ? checks : null;
        }

        private void RunWithProgress(string text, Action<GlobalProgressActionArgs> action)
        {
            var options = new GlobalProgressOptions(text, cancelable: true) { IsIndeterminate = false };
            var result = PlayniteApi.Dialogs.ActivateGlobalProgress(action, options);
            if (result.Error != null)
            {
                logger.Error(result.Error, "Vertical Cover Fixer failed");
                PlayniteApi.Dialogs.ShowErrorMessage($"Something went wrong:\n{result.Error.Message}", Title);
            }
        }

        private static string Summary(IEnumerable<CoverCheck> checks)
        {
            var counts = checks.GroupBy(c => c.Shape).ToDictionary(g => g.Key, g => g.Count());
            var lines = new[] { CoverShape.Vertical, CoverShape.Square, CoverShape.Horizontal, CoverShape.Missing, CoverShape.Unknown }
                .Where(counts.ContainsKey)
                .Select(shape => $"{Label(shape)}: {counts[shape]}");
            return string.Join("\n", lines);
        }

        private static string Label(CoverShape shape) => shape switch
        {
            CoverShape.Unknown => "Couldn't read",
            _ => shape.ToString(),
        };

        private static void OpenFolder(string folder)
        {
            Directory.CreateDirectory(folder);
            Process.Start("explorer.exe", $"\"{folder}\"");
        }

        private static void Open(string file)
        {
            try
            {
                Process.Start(file);
            }
            catch (Exception ex)
            {
                // No program is set to open .csv files; show it in Explorer instead.
                logger.Warn($"Vertical Cover Fixer: couldn't open {file}: {ex.Message}");
                Process.Start("explorer.exe", $"/select,\"{file}\"");
            }
        }
    }
}
