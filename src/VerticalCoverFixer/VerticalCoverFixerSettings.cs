using Playnite.SDK;
using Playnite.SDK.Data;
using System.Collections.Generic;
using VerticalCoverFixer.Core;

namespace VerticalCoverFixer
{
    public class VerticalCoverFixerSettings
    {
        /// <summary>Free key from steamgriddb.com → Preferences → API. Without it, only Steam games can be fixed.</summary>
        public string SteamGridDbApiKey { get; set; } = string.Empty;

        /// <summary>Try Steam's own 600×900 library art first for games from the Steam library.</summary>
        public bool UseSteamArt { get; set; } = true;

        public bool ReplaceSquare { get; set; } = true;

        public bool ReplaceHorizontal { get; set; } = true;

        public bool FillMissing { get; set; } = true;

        public bool IncludeHidden { get; set; } = true;

        /// <summary>When no SteamGridDB game has exactly the same name, use its top search result.</summary>
        public bool AcceptClosestMatch { get; set; } = true;

        public bool AllowNsfw { get; set; }

        public bool AllowHumor { get; set; }

        [DontSerialize]
        public bool HasSteamGridDbKey => !string.IsNullOrWhiteSpace(SteamGridDbApiKey);

        [DontSerialize]
        public ReplaceRules Rules => new ReplaceRules
        {
            ReplaceSquare = ReplaceSquare,
            ReplaceHorizontal = ReplaceHorizontal,
            FillMissing = FillMissing,
        };
    }

    public class VerticalCoverFixerSettingsViewModel : ObservableObject, ISettings
    {
        private readonly VerticalCoverFixerPlugin plugin;
        private VerticalCoverFixerSettings editingClone;
        private VerticalCoverFixerSettings settings;

        public VerticalCoverFixerSettingsViewModel(VerticalCoverFixerPlugin plugin)
        {
            this.plugin = plugin;
            Settings = plugin.LoadPluginSettings<VerticalCoverFixerSettings>() ?? new VerticalCoverFixerSettings();
        }

        public VerticalCoverFixerSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                OnPropertyChanged();
            }
        }

        public void BeginEdit() => editingClone = Serialization.GetClone(Settings);

        public void CancelEdit() => Settings = editingClone;

        public void EndEdit()
        {
            Settings.SteamGridDbApiKey = Settings.SteamGridDbApiKey?.Trim() ?? string.Empty;
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }
}
