using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

        public static string NormalizeServerUrl(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return "";
            string url = rawUrl.Trim();

            // If starts with http:// -> change to ws://
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                url = "ws://" + url.Substring(7);
            }
            // If starts with https:// -> change to wss://
            else if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "wss://" + url.Substring(8);
            }
            // If doesn't start with ws:// or wss://, default to wss://
            else if (!url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                url = "wss://" + url;
            }

            // Ensure path ends with /ws/daemon if not already present
            if (!url.EndsWith("/ws/daemon", StringComparison.OrdinalIgnoreCase))
            {
                url = url.TrimEnd('/') + "/ws/daemon";
            }

            return url;
        }

        public static (string serverUrl, string machineId, string apiKey) EnsureCredentials(string? serverUrl, string? machineId, string? apiKey)
        {
            var settings = Load();

            string finalServer = !string.IsNullOrWhiteSpace(serverUrl) ? NormalizeServerUrl(serverUrl) : settings.ServerUrl;
            string finalId = !string.IsNullOrWhiteSpace(machineId) ? machineId.Trim() : settings.MachineId;
            string finalKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey.Trim() : settings.ApiKey;

            bool updated = false;

            if (string.IsNullOrWhiteSpace(finalId))
            {
                finalId = Guid.NewGuid().ToString();
                updated = true;
            }

            if (string.IsNullOrWhiteSpace(finalKey))
            {
                finalKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(finalServer) && finalServer != settings.ServerUrl)
            {
                settings.ServerUrl = finalServer;
                updated = true;
            }

            if (updated || settings.MachineId != finalId || settings.ApiKey != finalKey)
            {
                settings.MachineId = finalId;
                settings.ApiKey = finalKey;
                if (!string.IsNullOrWhiteSpace(finalServer))
                {
                    settings.ServerUrl = finalServer;
                }
                Save(settings);
            }

            return (finalServer, finalId, finalKey);
        }

        public static void SetServer(string serverUrl)
        {
            var settings = Load();
            settings.ServerUrl = NormalizeServerUrl(serverUrl);
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
