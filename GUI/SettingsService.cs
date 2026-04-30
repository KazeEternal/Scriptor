using System;
using System.IO;
using System.Text.Json;

namespace GUI
{
    public sealed class SettingsService
    {
        private readonly string _settingsPath;

        public SettingsService(string settingsPath)
        {
            _settingsPath = settingsPath;
            Current = LoadFromDisk();
        }

        public AppSettings Current { get; private set; }

        public string? ScriptsRoot
        {
            get => Current.ScriptsRoot;
            set
            {
                if (string.Equals(Current.ScriptsRoot, value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Current.ScriptsRoot = value;
                Save();
            }
        }

        public event EventHandler<AppSettings>? SettingsChanged;

        public static string GetDefaultSettingsPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "Scriptor", "scriptor-settings.json");
        }

        public void Save()
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
            SettingsChanged?.Invoke(this, Current);
        }

        private AppSettings LoadFromDisk()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return new AppSettings();
                }

                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }
    }
}
