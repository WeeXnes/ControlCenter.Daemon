using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlCenter.Daemon.Services
{
    public class DaemonSettings
    {
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("port")]
        public int Port { get; set; } = 9100;
    }

    public static class DaemonSettingsManager
    {
        public static string GetSettingsPath()
        {
            string userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userDir, ".ControlCenterDaemon", "settings.json");
        }

        public static DaemonSettings Load()
        {
            string path = GetSettingsPath();
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var loaded = JsonSerializer.Deserialize<DaemonSettings>(json);
                    if (loaded != null) return loaded;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to read settings.json at {path}: {ex.Message}");
                }
            }
            return new DaemonSettings();
        }

        public static void Save(DaemonSettings settings)
        {
            string path = GetSettingsPath();
            string dir = Path.GetDirectoryName(path)!;

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(path, json);
        }

        public static void SetToken(string token)
        {
            var settings = Load();
            settings.ApiKey = token.Trim();
            Save(settings);
            Console.WriteLine($"[+] API token successfully saved to {GetSettingsPath()}");
        }
    }
}
