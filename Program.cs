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
            string[] registryPaths = {
                @"SOFTWARE\WOW6432Node\qBittorrent",
                @"SOFTWARE\qBittorrent"
            };

            foreach (var path in registryPaths)
            {
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(path))
                    {
                        if (key != null)
                        {
                            var version = key.GetValue("Version") as string;
                            if (!string.IsNullOrEmpty(version))
                                return version;
                        }
                    }
                }
                catch { }
            }

            string[] programPaths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "qBittorrent", "qbittorrent.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "qBittorrent", "qbittorrent.exe")
            };

            foreach (var path in programPaths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var versionInfo = FileVersionInfo.GetVersionInfo(path);
                        return $"{versionInfo.FileMajorPart}.{versionInfo.FileMinorPart}.{versionInfo.FileBuildPart}";
                    }
                    catch { }
                }
            }

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