# Vertical Cover Fixer for Playnite

A [Playnite](https://playnite.link) add-on that checks the cover art of every game in your library and replaces
**square**, **horizontal** and **missing** covers with **vertical box art**, so your library grid looks consistent.

## What it does

1. **Checks each cover's shape** by reading its image file: vertical, square (within 10% of 1:1), horizontal,
   missing (no cover, file gone, or an empty/damaged file) or unreadable.
2. **Finds vertical art**, trying these in order:
   - **Steam's official 600×900 library art**, for games from your Steam library. No account or key needed.
   - **[SteamGridDB](https://www.steamgriddb.com)**, looked up by Steam or GOG id where possible, otherwise by name.
     Needs a free API key.
3. **Checks the download really is vertical**, then sets it as the game's cover through Playnite, the same way
   Playnite's own editor does.
4. **Backs up the old cover** and writes a CSV report of everything it did.

Nothing changes until you confirm, and you can stop at any time.

## Install

1. Build it (see below) or download `VerticalCoverFixer_x.y.z.pext` from the Releases page.
2. Double-click the `.pext` file (or drag it onto Playnite), click **Yes**, then restart Playnite.
3. Optional but recommended: get a SteamGridDB API key. Sign in at
   [steamgriddb.com](https://www.steamgriddb.com/profile/preferences/api), copy the key from
   **Preferences → API**, and paste it into Playnite under **Add-ons → Generic → Vertical Cover Fixer**.
   Without a key, only Steam games can be fixed.

## Use

In Playnite, open the **Extensions** menu → **Vertical Cover Fixer**:

| Command | What it does |
|---|---|
| **Check cover shapes (report only, changes nothing)** | Counts your covers by shape and saves a CSV listing every game |
| **Replace non-vertical and missing covers…** | Shows what it will replace, asks you to confirm, then fixes them |
| **Open reports and backups folder** | Reports and old covers are kept here |

To fix just a few games, select them, right-click → **Vertical Cover Fixer** → **Replace with vertical cover art**.

### Settings

**Add-ons → Generic → Vertical Cover Fixer**:

- Which shapes to replace (missing, horizontal, square) and whether to include hidden games
- Whether to try Steam's official art first
- Whether to accept SteamGridDB's closest match when no game has exactly the same name. The report shows each
  match it used, so you can check them.
- Whether to allow covers SteamGridDB marks as adult or joke art (off by default)

### Undoing a change

The old covers are copied to `backups\<date and time>\` in the reports folder, named after each game. The CSV report
for the run lists each game's backup file. To restore one, right-click the game → **Edit** → pick the
backed-up file as its cover.

## Build

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
./scripts/build-release.ps1 -Version 1.0.0   # runs the tests, writes dist\VerticalCoverFixer_1.0.0.pext
```

Layout:

- `src/VerticalCoverFixer.Core`: logic with no Playnite dependency (shape rules, reading image sizes from file
  headers, SteamGridDB requests and matching). Unit-tested in `tests/`.
- `src/VerticalCoverFixer`: the Playnite add-on (.NET Framework 4.6.2, as Playnite 10 requires). It compiles the
  Core files into its own DLL.

It's an add-on rather than a stand-alone program because Playnite keeps its database locked while it runs. Only an
add-on can change covers safely while Playnite is open.

## License

MIT. See [LICENSE](LICENSE).
