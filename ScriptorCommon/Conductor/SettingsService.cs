using System;
using System.IO;
using System.Text.Json;

namespace Scripts.Scriptor.Conductor
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

        public string? GetValue(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            return Current.Values.TryGetValue(key, out var value) ? value : null;
        }

        public void SetValue(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (value == null)
            {
                if (Current.Values.Remove(key))
                {
                    Save();
                }

                return;
            }

            if (Current.Values.TryGetValue(key, out var existing) && string.Equals(existing, value, StringComparison.Ordinal))
            {
                return;
            }

            Current.Values[key] = value;
            Save();
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
