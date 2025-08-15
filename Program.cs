using System;
using System.Drawing;
using System.Windows.Forms;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;

namespace QUpdater
{
    public class Program
    {
        private NotifyIcon trayIcon;
        private HttpClient httpClient;
        private bool isChecking = false;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var program = new Program();
            program.Initialize();
            Application.Run();
        }

        public void Initialize()
        {
            httpClient = new HttpClient();
            SetupTrayIcon();
            CheckForUpdates();
        }

        private void SetupTrayIcon()
        {
            // Load the icon from embedded resource
            Icon appIcon;
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("QUpdater.icon.ico"))
                {
                    if (stream != null)
                    {
                        appIcon = new Icon(stream);
                    }
                    else
                    {
                        appIcon = SystemIcons.Application;
                    }
                }
            }
            catch
            {
                appIcon = SystemIcons.Application;
            }

            trayIcon = new NotifyIcon()
            {
                Icon = appIcon,
                Visible = true,
                Text = "qUpdater"
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Check for Updates", null, async (s, e) => await CheckForUpdates());
            contextMenu.Items.Add("About", null, ShowAbout);
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, (s, e) => Application.Exit());

            trayIcon.ContextMenuStrip = contextMenu;
        }

        private void ShowAbout(object sender, EventArgs e)
        {
            MessageBox.Show(
                "qUpdater\n\n" +
                "This tool automatically checks for qBittorrent updates\n" +
                "on startup and helps you install them.\n\n" +
                "You can also manually check for updates using the tray menu.",
                "About qUpdater",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private string GetInstalledVersion()
        {
            // 1) Try direct version from dedicated qBittorrent keys (both 64/32-bit, HKLM/HKCU)
            string directVersion = TryReadQbittorrentVersionFromRegistry();
            if (!string.IsNullOrWhiteSpace(directVersion))
                return directVersion;

            // 2) Try to resolve the executable path from registry (App Paths / Uninstall entries)
            string exePath = TryGetQbittorrentExePathFromRegistry();
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                var versionFromExe = TryGetVersionFromExecutable(exePath);
                if (!string.IsNullOrWhiteSpace(versionFromExe))
                    return versionFromExe;
            }

            // 3) Probe common install locations across ALL fixed drives (e.g., D:\Program Files\...)
            exePath = ProbeCommonInstallLocationsAcrossDrives();
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                var versionFromExe = TryGetVersionFromExecutable(exePath);
                if (!string.IsNullOrWhiteSpace(versionFromExe))
                    return versionFromExe;
            }

            return null;
        }

        private string TryReadQbittorrentVersionFromRegistry()
        {
            string[] keyPaths = new[]
            {
                "SOFTWARE\\qBittorrent",
                "SOFTWARE\\WOW6432Node\\qBittorrent"
            };

            // Check both HKLM and HKCU in both 64-bit and 32-bit views
            RegistryHive[] hives = new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            RegistryView[] views = new[] { RegistryView.Registry64, RegistryView.Registry32 };

            foreach (var hive in hives)
            {
                foreach (var view in views)
                {
                    try
                    {
                        using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                        {
                            foreach (var path in keyPaths)
                            {
                                using (var key = baseKey.OpenSubKey(path))
                                {
                                    if (key == null) continue;
                                    var version = key.GetValue("Version") as string;
                                    if (!string.IsNullOrWhiteSpace(version))
                                        return version;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        private string TryGetQbittorrentExePathFromRegistry()
        {
            // Prefer App Paths which typically holds the absolute path to the executable
            string appPath = TryReadAppPaths("qbittorrent.exe");
            if (!string.IsNullOrWhiteSpace(appPath))
                return appPath;

            // Fallback: search Uninstall entries for DisplayIcon or InstallLocation
            var uninstallProbe = TryReadFromUninstall();
            if (!string.IsNullOrWhiteSpace(uninstallProbe) && File.Exists(uninstallProbe))
                return uninstallProbe;

            return null;
        }

        private string TryReadAppPaths(string exeName)
        {
            string keyPath = $"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\{exeName}";
            RegistryHive[] hives = new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            RegistryView[] views = new[] { RegistryView.Registry64, RegistryView.Registry32 };

            foreach (var hive in hives)
            {
                foreach (var view in views)
                {
                    try
                    {
                        using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                        using (var key = baseKey.OpenSubKey(keyPath))
                        {
                            if (key == null) continue;
                            var defaultValue = key.GetValue(null) as string; // (Default)
                            if (!string.IsNullOrWhiteSpace(defaultValue) && File.Exists(defaultValue))
                                return defaultValue;

                            var pathValue = key.GetValue("Path") as string;
                            if (!string.IsNullOrWhiteSpace(pathValue))
                            {
                                var candidate = Path.Combine(pathValue, exeName);
                                if (File.Exists(candidate))
                                    return candidate;
                            }
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        private string TryReadFromUninstall()
        {
            string[] uninstallRoots = new[]
            {
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall"
            };

            RegistryHive[] hives = new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            RegistryView[] views = new[] { RegistryView.Registry64, RegistryView.Registry32 };

            foreach (var hive in hives)
            {
                foreach (var view in views)
                {
                    foreach (var root in uninstallRoots)
                    {
                        try
                        {
                            using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                            using (var uninstallKey = baseKey.OpenSubKey(root))
                            {
                                if (uninstallKey == null) continue;
                                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                                {
                                    using (var subKey = uninstallKey.OpenSubKey(subKeyName))
                                    {
                                        if (subKey == null) continue;
                                        var displayName = subKey.GetValue("DisplayName") as string;
                                        if (string.IsNullOrWhiteSpace(displayName) || displayName.IndexOf("qbittorrent", StringComparison.OrdinalIgnoreCase) < 0)
                                            continue;

                                        // Found a qBittorrent entry; try to get version if needed
                                        var displayVersion = subKey.GetValue("DisplayVersion") as string;
                                        if (!string.IsNullOrWhiteSpace(displayVersion))
                                        {
                                            // Return version via separate call if needed, but we mainly want path
                                        }

                                        // Prefer InstallLocation when available
                                        var installLocation = subKey.GetValue("InstallLocation") as string;
                                        if (!string.IsNullOrWhiteSpace(installLocation))
                                        {
                                            var candidate = Path.Combine(installLocation, "qbittorrent.exe");
                                            if (File.Exists(candidate)) return candidate;
                                        }

                                        // Next try DisplayIcon which often points directly to exe (may include ,0 suffix)
                                        var displayIcon = subKey.GetValue("DisplayIcon") as string;
                                        if (!string.IsNullOrWhiteSpace(displayIcon))
                                        {
                                            var cleaned = displayIcon;
                                            var commaIdx = cleaned.IndexOf(',');
                                            if (commaIdx >= 0) cleaned = cleaned.Substring(0, commaIdx);
                                            cleaned = cleaned.Trim('"');
                                            if (File.Exists(cleaned)) return cleaned;
                                        }

                                        // As a final hint, UninstallString may live next to the exe
                                        var uninstallString = subKey.GetValue("UninstallString") as string;
                                        if (!string.IsNullOrWhiteSpace(uninstallString))
                                        {
                                            var pathCandidate = uninstallString.Trim('"');
                                            try
                                            {
                                                var directory = Path.GetDirectoryName(pathCandidate);
                                                if (!string.IsNullOrWhiteSpace(directory))
                                                {
                                                    var exeCandidate = Path.Combine(directory, "qbittorrent.exe");
                                                    if (File.Exists(exeCandidate)) return exeCandidate;
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            return null;
        }

        private string ProbeCommonInstallLocationsAcrossDrives()
        {
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                        var root = drive.RootDirectory.FullName;

                        string[] relativePaths = new[]
                        {
                            Path.Combine("Program Files", "qBittorrent", "qbittorrent.exe"),
                            Path.Combine("Program Files (x86)", "qBittorrent", "qbittorrent.exe")
                        };

                        foreach (var rel in relativePaths)
                        {
                            var candidate = Path.Combine(root, rel);
                            if (File.Exists(candidate)) return candidate;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        private string TryGetVersionFromExecutable(string exePath)
        {
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                return $"{versionInfo.FileMajorPart}.{versionInfo.FileMinorPart}.{versionInfo.FileBuildPart}";
            }
            catch { }
            return null;
        }

        private async Task<(string version, string url)> GetLatestVersion()
        {
            try
            {
                var response = await httpClient.GetStringAsync("https://sourceforge.net/projects/qbittorrent/best_release.json");
                using (JsonDocument document = JsonDocument.Parse(response))
                {
                    var root = document.RootElement;
                    var windows = root.GetProperty("platform_releases").GetProperty("windows");
                    var filename = windows.GetProperty("filename").GetString();
                    var url = windows.GetProperty("url").GetString();

                    var match = Regex.Match(filename, @"qbittorrent_(\d+\.\d+\.\d+)_x64_setup\.exe");
                    if (match.Success)
                    {
                        return (match.Groups[1].Value, url);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error checking for updates: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return (null, null);
        }

        private async Task DownloadAndInstall(string version, string url)
        {
            var result = MessageBox.Show(
                $"A new version of qBittorrent ({version}) is available. Would you like to download and install it?",
                "Update Available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result != DialogResult.Yes)
                return;

            var progressForm = new Form
            {
                Text = "Downloading Update",
                Size = new Size(400, 150),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false,
                ControlBox = false,
                TopMost = true
            };

            var label = new Label
            {
                Text = "Downloading qBittorrent update...",
                AutoSize = true,
                Location = new Point(10, 20)
            };

            var progressBar = new ProgressBar
            {
                Location = new Point(10, 50),
                Size = new Size(365, 23),
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous
            };

            var sizeLabel = new Label
            {
                Text = "0 MB / 0 MB",
                AutoSize = true,
                Location = new Point(10, 80)
            };

            progressForm.Controls.AddRange(new Control[] { label, progressBar, sizeLabel });
            progressForm.Show();

            try
            {
                var downloadPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads",
                    $"qbittorrent_{version}_x64_setup.exe"
                );

                using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    var totalMB = totalBytes / 1024.0 / 1024.0;

                    using (var downloadStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(downloadPath, FileMode.Create))
                    {
                        var buffer = new byte[8192];
                        var bytesRead = 0;
                        var totalBytesRead = 0L;

                        while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;

                            var progress = (int)((totalBytesRead * 100) / totalBytes);
                            var downloadedMB = totalBytesRead / 1024.0 / 1024.0;

                            progressBar.Value = progress;
                            sizeLabel.Text = $"{downloadedMB:F1} MB / {totalMB:F1} MB";
                            progressForm.Update();
                        }
                    }
                }

                progressForm.Close();

                // Kill qBittorrent if running
                foreach (var process in Process.GetProcessesByName("qbittorrent"))
                {
                    process.Kill();
                    process.WaitForExit();
                }

                // Start installer
                Process.Start(new ProcessStartInfo(downloadPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                progressForm.Close();
                MessageBox.Show($"Error downloading update: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task CheckForUpdates()
        {
            if (isChecking)
                return;

            isChecking = true;

            try
            {
                var currentVersion = GetInstalledVersion();
                if (string.IsNullOrEmpty(currentVersion))
                {
                    MessageBox.Show("Could not detect qBittorrent installation.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var (latestVersion, downloadUrl) = await GetLatestVersion();
                if (!string.IsNullOrEmpty(latestVersion))
                {
                    Version current = Version.Parse(currentVersion);
                    Version latest = Version.Parse(latestVersion);

                    if (latest > current)
                    {
                        await DownloadAndInstall(latestVersion, downloadUrl);
                    }
                    else
                    {
                        MessageBox.Show("qBittorrent is up to date!", "No Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            finally
            {
                isChecking = false;
            }
        }
    }
}