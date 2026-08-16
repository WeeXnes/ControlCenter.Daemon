using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlCenter.Daemon.Services
{
    public class DaemonSettings
    {
        [JsonPropertyName("serverUrl")]
        public string ServerUrl { get; set; } = "";

        [JsonPropertyName("machineId")]
        public string MachineId { get; set; } = "";

        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("certFingerprint")]
        public string CertFingerprint { get; set; } = "";
    }

    public static class DaemonSettingsManager
    {
        public static string GetSettingsPath()
        {
            string userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(userDir))
            {
                userDir = "/root";
            }
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
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    try { File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch { }
                }
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(path, json);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
            }
        }

        public static void SetServer(string serverUrl)
        {
            var settings = Load();
            settings.ServerUrl = serverUrl.Trim();
            Save(settings);
            Console.WriteLine($"[+] Server URL saved: {settings.ServerUrl}");
        }

        public static void SetMachineId(string id)
        {
            var settings = Load();
            settings.MachineId = id.Trim();
            Save(settings);
            Console.WriteLine($"[+] Machine ID saved: {settings.MachineId}");
        }

        public static void SetToken(string token)
        {
            var settings = Load();
            settings.ApiKey = token.Trim();
            Save(settings);
            Console.WriteLine($"[+] API token saved.");
        }

        public static void SetCertFingerprint(string fingerprint)
        {
            var settings = Load();
            settings.CertFingerprint = fingerprint.Replace(":", "").Trim().ToUpperInvariant();
            Save(settings);
            Console.WriteLine($"[+] SSL Certificate Pin SHA-256 saved: {settings.CertFingerprint}");
        }
    }
}

