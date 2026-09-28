using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace VerticalCoverFixer.Core
{
    [DataContract]
    public sealed class SgdbGame
    {
        [DataMember(Name = "id")]
        public int Id { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <summary>Unix time in seconds.</summary>
        [DataMember(Name = "release_date")]
        public long? ReleaseDate { get; set; }

        public int? ReleaseYear => ReleaseDate is long seconds && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).Year
            : (int?)null;
    }

    [DataContract]
    public sealed class SgdbGrid
    {
        [DataMember(Name = "id")]
        public int Id { get; set; }

        [DataMember(Name = "url")]
        public string Url { get; set; }

        [DataMember(Name = "width")]
        public int Width { get; set; }

        [DataMember(Name = "height")]
        public int Height { get; set; }

        [DataMember(Name = "mime")]
        public string Mime { get; set; }

        [DataMember(Name = "nsfw")]
        public bool Nsfw { get; set; }

        [DataMember(Name = "humor")]
        public bool Humor { get; set; }

        [DataMember(Name = "epilepsy")]
        public bool Epilepsy { get; set; }
    }

    [DataContract]
    internal sealed class SgdbResponse<T>
    {
        [DataMember(Name = "success")]
        public bool Success { get; set; }

        [DataMember(Name = "data")]
        public List<T> Data { get; set; }
    }

    public sealed class SgdbMatch
    {
        public SgdbMatch(SgdbGame game, bool exact)
        {
            Game = game;
            Exact = exact;
        }

        public SgdbGame Game { get; }

        /// <summary>False when the name didn't match exactly and SteamGridDB's top search result was used.</summary>
        public bool Exact { get; }
    }

    /// <summary>Talking to SteamGridDB's v2 API: building request URLs, reading replies, choosing results.</summary>
    public static class SteamGridDb
    {
        public const string ApiBase = "https://www.steamgriddb.com/api/v2/";

        /// <summary>SteamGridDB's vertical grid sizes. Its other sizes are square (512², 1024²) or wide (460×215, 920×430).</summary>
        public const string VerticalDimensions = "600x900,342x482,660x930";

        public static string SearchUrl(string gameName) =>
            ApiBase + "search/autocomplete/" + Uri.EscapeDataString(CleanSearchTerm(gameName));

        public static string GridsForGameUrl(int sgdbGameId, bool allowNsfw, bool allowHumor) =>
            $"{ApiBase}grids/game/{sgdbGameId}?{GridQuery(allowNsfw, allowHumor)}";

        public static string GridsForPlatformUrl(string platform, string platformGameId, bool allowNsfw, bool allowHumor) =>
            $"{ApiBase}grids/{platform}/{Uri.EscapeDataString(platformGameId)}?{GridQuery(allowNsfw, allowHumor)}";

        // WebP is left out on purpose: Playnite can only show it where Windows has the WebP codec installed.
        private static string GridQuery(bool allowNsfw, bool allowHumor) =>
            $"dimensions={VerticalDimensions}&types=static&mimes=image/png,image/jpeg"
            + $"&nsfw={(allowNsfw ? "any" : "false")}&humor={(allowHumor ? "any" : "false")}";

        /// <summary>Reads the "data" list from a reply, or an empty list if the reply says it failed.</summary>
        public static List<T> ParseData<T>(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(SgdbResponse<T>));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var response = (SgdbResponse<T>)serializer.ReadObject(stream);
                return response?.Success == true && response.Data != null ? response.Data : new List<T>();
            }
        }

        /// <summary>
        /// The usable vertical grids, best first. SteamGridDB already sorts by community score,
        /// so this only drops what we can't or shouldn't use and keeps the order.
        /// </summary>
        public static List<SgdbGrid> PickGrids(IEnumerable<SgdbGrid> grids, bool allowNsfw, bool allowHumor) =>
            grids.Where(g => !string.IsNullOrEmpty(g.Url)
                             && CoverShapes.Classify(g.Width, g.Height) == CoverShape.Vertical
                             && (allowNsfw || !g.Nsfw)
                             && (allowHumor || !g.Humor)
                             && !g.Epilepsy
                             && g.Mime != "image/webp")
                .ToList();

        /// <summary>
        /// Finds the search result for a game: an exact name match (by release year if there are several),
        /// otherwise the top result when <paramref name="allowClosest"/> is on.
        /// </summary>
        public static SgdbMatch PickGame(IReadOnlyList<SgdbGame> results, string gameName, int? releaseYear, bool allowClosest)
        {
            if (results == null || results.Count == 0)
            {
                return null;
            }

            string wanted = NormalizeName(gameName);
            var exact = results.Where(r => NormalizeName(r.Name) == wanted).ToList();
            if (exact.Count > 0)
            {
                var sameYear = releaseYear == null ? null : exact.FirstOrDefault(r => r.ReleaseYear == releaseYear);
                return new SgdbMatch(sameYear ?? exact[0], exact: true);
            }

            return allowClosest ? new SgdbMatch(results[0], exact: false) : null;
        }

        /// <summary>
        /// Lower-case letters and digits only, so "Half-Life™ 2" and "Half Life 2" compare equal.
        /// A leading "The" is dropped, and "&amp;" counts as "and".
        /// </summary>
        public static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var text = name.ToLowerInvariant().Replace("&", " and ");
            text = Regex.Replace(text, @"[^\p{L}\p{Nd}]+", " ").Trim();
            if (text.StartsWith("the "))
            {
                text = text.Substring(4);
            }

            return text;
        }

        /// <summary>Trademark symbols and the like confuse SteamGridDB's search, so they're removed.</summary>
        public static string CleanSearchTerm(string name) =>
            Regex.Replace(name ?? string.Empty, "[™®©]", string.Empty).Trim();
    }
}
