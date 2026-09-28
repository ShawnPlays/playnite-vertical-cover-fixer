using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using VerticalCoverFixer.Core;

namespace VerticalCoverFixer
{
    /// <summary>A possible replacement cover: where to download it and where it came from, for the report.</summary>
    internal sealed class CoverCandidate
    {
        public CoverCandidate(string url, string source)
        {
            Url = url;
            Source = source;
        }

        public string Url { get; }

        public string Source { get; }
    }

    /// <summary>SteamGridDB turned the API key down, so there's no point asking about the remaining games.</summary>
    internal sealed class SteamGridDbKeyException : Exception
    {
        public SteamGridDbKeyException()
            : base("SteamGridDB didn't accept the API key. Check it in the add-on's settings.")
        {
        }
    }

    /// <summary>Finds and downloads vertical covers from Steam's CDN and SteamGridDB.</summary>
    internal sealed class CoverDownloader : IDisposable
    {
        private const int MaxImageBytes = 20 * 1024 * 1024;
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly VerticalCoverFixerSettings settings;
        private readonly HttpClient http;

        public CoverDownloader(VerticalCoverFixerSettings settings)
        {
            this.settings = settings;
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("VerticalCoverFixer/1.0 (Playnite add-on)");
        }

        public void Dispose() => http.Dispose();

        /// <summary>
        /// Replacement covers for a game, best first. Lazy, so SteamGridDB is only asked once Steam's art
        /// has been tried, and nothing more is fetched once a cover works out.
        /// </summary>
        /// <param name="notes">Collects why nothing was found, for the report.</param>
        public IEnumerable<CoverCandidate> FindCandidates(Game game, List<string> notes, CancellationToken cancel)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (settings.UseSteamArt && game.PluginId == LibraryIds.Steam)
            {
                foreach (var url in SteamCdn.VerticalCoverUrls(game.GameId))
                {
                    seen.Add(url);
                    yield return new CoverCandidate(url, "Steam");
                }
            }

            if (!settings.HasSteamGridDbKey)
            {
                notes.Add("No SteamGridDB API key set");
                yield break;
            }

            // Steam and GOG games can be looked up by their store id, which avoids name mix-ups.
            string platform = LibraryIds.SteamGridDbPlatform(game.PluginId);
            if (platform != null && !string.IsNullOrEmpty(game.GameId))
            {
                var url = SteamGridDb.GridsForPlatformUrl(platform, game.GameId, settings.AllowNsfw, settings.AllowHumor);
                foreach (var grid in GetGrids(url, cancel))
                {
                    if (seen.Add(grid.Url))
                    {
                        yield return new CoverCandidate(grid.Url, $"SteamGridDB ({platform} id {game.GameId})");
                    }
                }
            }

            var searchJson = GetString(SteamGridDb.SearchUrl(game.Name), cancel);
            var results = searchJson == null ? new List<SgdbGame>() : SteamGridDb.ParseData<SgdbGame>(searchJson);
            var match = SteamGridDb.PickGame(results, game.Name, game.ReleaseYear, settings.AcceptClosestMatch);
            if (match == null)
            {
                notes.Add(results.Count == 0
                    ? "SteamGridDB has no game by this name"
                    : $"No exact name match on SteamGridDB (closest: \"{results[0].Name}\")");
                yield break;
            }

            string source = match.Exact
                ? $"SteamGridDB (\"{match.Game.Name}\")"
                : $"SteamGridDB (closest match: \"{match.Game.Name}\")";
            var gridsUrl = SteamGridDb.GridsForGameUrl(match.Game.Id, settings.AllowNsfw, settings.AllowHumor);
            int found = 0;
            foreach (var grid in GetGrids(gridsUrl, cancel))
            {
                found++;
                if (seen.Add(grid.Url))
                {
                    yield return new CoverCandidate(grid.Url, source);
                }
            }

            if (found == 0)
            {
                notes.Add($"SteamGridDB has no vertical covers for \"{match.Game.Name}\"");
            }
        }

        /// <summary>The image's bytes, or null if it couldn't be downloaded.</summary>
        public byte[] TryDownload(string url, CancellationToken cancel)
        {
            try
            {
                using (var response = http.GetAsync(url, cancel).GetAwaiter().GetResult())
                {
                    if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxImageBytes)
                    {
                        return null;
                    }

                    return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                }
            }
            catch (Exception ex) when (IsNetworkError(ex, cancel))
            {
                logger.Warn($"Vertical Cover Fixer: download failed for {url}: {ex.Message}");
                return null;
            }
        }

        private List<SgdbGrid> GetGrids(string url, CancellationToken cancel)
        {
            var json = GetString(url, cancel);
            return json == null
                ? new List<SgdbGrid>()
                : SteamGridDb.PickGrids(SteamGridDb.ParseData<SgdbGrid>(json), settings.AllowNsfw, settings.AllowHumor);
        }

        /// <summary>A SteamGridDB API reply, or null if the game isn't there or the request failed.</summary>
        private string GetString(string url, CancellationToken cancel)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.SteamGridDbApiKey.Trim());
                    using (var response = http.SendAsync(request, cancel).GetAwaiter().GetResult())
                    {
                        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            throw new SteamGridDbKeyException();
                        }

                        // 404 is SteamGridDB's answer for "no such game", which is normal.
                        return response.IsSuccessStatusCode
                            ? response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                            : null;
                    }
                }
            }
            catch (Exception ex) when (IsNetworkError(ex, cancel))
            {
                logger.Warn($"Vertical Cover Fixer: request failed for {url}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Failures worth skipping past. A cancel by the user is not one of them.</summary>
        private static bool IsNetworkError(Exception ex, CancellationToken cancel) =>
            !cancel.IsCancellationRequested
            && (ex is HttpRequestException || ex is OperationCanceledException || ex is System.IO.IOException);
    }
}
