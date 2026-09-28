using System;
using System.Collections.Generic;
using System.Linq;

namespace VerticalCoverFixer.Core
{
    /// <summary>Ids of Playnite's built-in library add-ons whose game ids other services understand.</summary>
    public static class LibraryIds
    {
        public static readonly Guid Steam = Guid.Parse("CB91DFC9-B977-43BF-8E70-55F46E410FAB");

        public static readonly Guid Gog = Guid.Parse("AEBE8B7C-6DC3-4A66-AF31-E7375C6B5E9E");

        /// <summary>The platform name SteamGridDB uses for a library's game ids, or null if it has none we can use.</summary>
        public static string SteamGridDbPlatform(Guid pluginId) =>
            pluginId == Steam ? "steam"
            : pluginId == Gog ? "gog"
            : null;
    }

    /// <summary>Steam's own vertical library art (600×900), served from its CDN. No account or key needed.</summary>
    public static class SteamCdn
    {
        public static IEnumerable<string> VerticalCoverUrls(string appId)
        {
            if (string.IsNullOrEmpty(appId) || !appId.All(char.IsDigit))
            {
                return Enumerable.Empty<string>();
            }

            // The high-resolution version first. Games released since 2024 keep their art under a hashed
            // folder that can't be guessed, so for those these miss and SteamGridDB takes over.
            return new[]
            {
                $"https://shared.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900_2x.jpg",
                $"https://shared.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg",
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900_2x.jpg",
            };
        }
    }
}
