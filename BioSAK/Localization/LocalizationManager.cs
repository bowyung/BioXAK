using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace BioSAK.Localization
{
    /// <summary>
    /// Runtime UI-language manager.
    /// IMPORTANT: this only affects text shown in the interface.
    /// Analysis results, exported files, chart data and any generated output
    /// are NEVER routed through this class and stay exactly as they were.
    /// </summary>
    public sealed class LocalizationManager : INotifyPropertyChanged
    {
        public const string English = "en";
        public const string ChineseTraditional = "zh-Hant";

        public static LocalizationManager Instance { get; } = new LocalizationManager();

        private readonly Dictionary<string, Dictionary<string, string>> _tables =
            new Dictionary<string, Dictionary<string, string>>
            {
                { English,             LocalizationStrings.En },
                { ChineseTraditional,  LocalizationStrings.ZhHant },
            };

        private string _current = English;

        private LocalizationManager()
        {
            _current = LoadSaved();
        }

        public string CurrentLanguage
        {
            get => _current;
            set
            {
                if (value == _current || !_tables.ContainsKey(value)) return;
                _current = value;
                Save(value);
                Raise(nameof(CurrentLanguage));
                Raise(nameof(IsChinese));
                Raise(nameof(ToggleLabel));
                Raise("Item[]");   // refreshes every {loc:L ...} binding
            }
        }

        public bool IsChinese => _current == ChineseTraditional;

        /// <summary>Caption for the sidebar language button (always shows the *other* language).</summary>
        public string ToggleLabel => IsChinese ? "English" : "繁體中文";

        public void Toggle() =>
            CurrentLanguage = IsChinese ? English : ChineseTraditional;

        /// <summary>Indexer used by the {loc:L Key} markup extension.</summary>
        public string this[string key] => Get(key);

        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (_tables.TryGetValue(_current, out var table) &&
                table.TryGetValue(key, out var value) &&
                !string.IsNullOrEmpty(value))
                return value;
            if (LocalizationStrings.En.TryGetValue(key, out var fallback))
                return fallback;
            return key;
        }

        /// <summary>Convenience helper for code-behind: L.T("Key").</summary>
        public static string T(string key) => Instance.Get(key);

        // ----- persistence -------------------------------------------------
        private static string SettingsPath
        {
            get
            {
                var dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "BioXAK");
                return Path.Combine(dir, "language.txt");
            }
        }

        private static string LoadSaved()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var v = File.ReadAllText(SettingsPath).Trim();
                    if (v == ChineseTraditional || v == English) return v;
                }
            }
            catch { /* ignore */ }
            return English;
        }

        private static void Save(string value)
        {
            try
            {
                var path = SettingsPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, value);
            }
            catch { /* ignore */ }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Short alias so code-behind can write L.T("Key").</summary>
    public static class L
    {
        public static string T(string key) => LocalizationManager.Instance.Get(key);
    }
}
