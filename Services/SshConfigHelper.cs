using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ControlCenter.Daemon.Services
{
    public static class SshConfigHelper
    {
        public static void EnsureSshdConfig()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;

            try
            {
                bool needsReload = false;
                string sshdDropInDir = "/etc/ssh/sshd_config.d";
                string mainConfig = "/etc/ssh/sshd_config";

                // Ensure /etc/ssh directory exists
                if (Directory.Exists("/etc/ssh"))
                {
                    // Create sshd_config.d directory if it doesn't exist
                    if (!Directory.Exists(sshdDropInDir))
                    {
                        try
                        {
                            Directory.CreateDirectory(sshdDropInDir);
                            File.SetUnixFileMode(sshdDropInDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                        }
                        catch { }
                    }

                    if (Directory.Exists(sshdDropInDir))
                    {
                        string dropInFile = Path.Combine(sshdDropInDir, "99-controlcenter.conf");
                        string requiredConfig = "PubkeyAuthentication yes\nAuthorizedKeysFile .ssh/authorized_keys\n";

                        bool fileMatches = File.Exists(dropInFile) && File.ReadAllText(dropInFile).Trim() == requiredConfig.Trim();
                        if (!fileMatches)
                        {
                            File.WriteAllText(dropInFile, requiredConfig);
                            try
                            {
                                File.SetUnixFileMode(dropInFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                            }
                            catch { }

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [+] Configured PubkeyAuthentication in {dropInFile}");
                            Console.ResetColor();
                            needsReload = true;
                        }
                    }

                    // Check if /etc/ssh/sshd_config has Include or needs PubkeyAuthentication yes
                    if (File.Exists(mainConfig))
                    {
                        try
                        {
                            string content = File.ReadAllText(mainConfig);
                            bool modified = false;

                            if (content.Contains("PubkeyAuthentication no"))
                            {
                                content = content.Replace("PubkeyAuthentication no", "PubkeyAuthentication yes");
                                modified = true;
                            }

                            // Ensure drop-in include is present if sshd_config.d exists
                            if (Directory.Exists(sshdDropInDir) && !content.Contains("sshd_config.d") && !content.Contains("Include /etc/ssh/sshd_config.d/*.conf"))
                            {
                                content = "Include /etc/ssh/sshd_config.d/*.conf\n" + content;
                                modified = true;
                            }

                            if (modified)
                            {
                                File.WriteAllText(mainConfig, content);
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [+] Updated {mainConfig} with PubkeyAuthentication yes & Drop-in Include");
                                Console.ResetColor();
                                needsReload = true;
                            }
                        }
                        catch { }
                    }

                    // Reload SSH daemon immediately if config changed
                    try
                    {
                        if (needsReload)
                        {
                            RunCommand("sh", "-c \"systemctl reload sshd || systemctl reload ssh || systemctl restart sshd || systemctl restart ssh\"");
                            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [+] SSH daemon reloaded with new authentication configuration.");
                        }
                        else
                        {
                            RunCommand("sh", "-c \"systemctl is-active --quiet sshd || systemctl is-active --quiet ssh || systemctl enable --now sshd || systemctl enable --now ssh\"");
                        }
                    }
                    catch { }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Running as non-root, SSH config check silently skipped
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [i] Note on SSH config check: {ex.Message}");
            }
        }

        public static void RunCommand(string fileName, string args)
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                proc?.WaitForExit(3000);
            }
            catch { }
        }
    }
}
