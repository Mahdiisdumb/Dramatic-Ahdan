using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DramaticAdhan
{
    public partial class MainForm : Form
    {
        private System.Windows.Forms.Timer? uiTimer;
        private NotifyIcon? notifyIcon;

        // UI
        private readonly Label lblNextPrayer;
        private readonly Label lblCountdown;
        private readonly Button btnRefreshNow;
        private readonly Button btnSettings;

        // Prayer times
        private readonly Dictionary<string, DateTime> prayerTimes = new(StringComparer.OrdinalIgnoreCase);
        private readonly string[] prayerOrder = { "Fajr", "Dhuhr", "Asr", "Maghrib", "Isha" };

        // Assets
        private readonly List<Image> backgroundImages = new();
        private readonly List<string> wavFiles = new();

        // Location / settings
        private string city = "Mecca";
        private string country = "Saudi Arabia";
        private double? latitude;
        private double? longitude;

        // Http client
        private static readonly HttpClient httpClient = new();

        // Background refresh
        private readonly TimeSpan refreshInterval = TimeSpan.FromHours(6);
        private CancellationTokenSource? refreshCts;

        private bool allowExit = false;

        public MainForm()
        {
            InitializeComponent();
            KeyPreview = true;

            // Countdown labels
            lblNextPrayer = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.Black,
                Location = new Point(20, 20),
                Text = "Next: --"
            };
            Controls.Add(lblNextPrayer);

            lblCountdown = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 14F, FontStyle.Regular),
                ForeColor = Color.DarkBlue,
                Location = new Point(20, 52),
                Text = "00:00:00"
            };
            Controls.Add(lblCountdown);

            // Buttons
            btnRefreshNow = new Button
            {
                Text = "Refresh Times",
                Location = new Point(20, 92),
                AutoSize = true
            };
            btnRefreshNow.Click += async (_, _) => await RefreshPrayerTimesAsync();
            Controls.Add(btnRefreshNow);

            btnSettings = new Button
            {
                Text = "Location",
                Location = new Point(150, 92),
                AutoSize = true
            };
            btnSettings.Click += (_, _) => ShowLocationDialog();
            Controls.Add(btnSettings);

            // Tray icon
            SetupNotifyIcon();

            // Minimize-on-minimize behavior
            Resize += (_, __) => { if (WindowState == FormWindowState.Minimized) MinimizeToTray(); };

            // Load assets
            LoadAssets();

            // Background tasks
            StartBackgroundRefresh();

            // UI timer
            StartUiTimer();

            // Detect location & refresh on startup
            _ = DetectLocationAndRefreshAsync();

            // Minimize to tray on startup
            Shown += (_, __) =>
            {
                try
                {
                    notifyIcon?.ShowBalloonTip(
                        5000,
                        "Dramatic Adhan",
                        "The app is running in the background. Double-click the tray icon to open.",
                        ToolTipIcon.Info);
                }
                catch { }
                MinimizeToTray();
            };
        }

        private void SetupNotifyIcon()
        {
            notifyIcon = new NotifyIcon
            {
                Visible = true,
                Text = "Dramatic Adhan"
            };
            try { if (Icon != null) notifyIcon.Icon = Icon; } catch { }

            var ctx = new ContextMenuStrip();
            var showItem = new ToolStripMenuItem("Show");
            showItem.Click += (_, __) => RestoreFromTray();
            ctx.Items.Add(showItem);

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (_, __) =>
            {
                allowExit = true;
                Close();
            };
            ctx.Items.Add(exitItem);

            notifyIcon.ContextMenuStrip = ctx;
            notifyIcon.DoubleClick += (_, __) => RestoreFromTray();
        }

        private void MinimizeToTray()
        {
            Hide();
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Activate();
        }

        private void LoadAssets()
        {
            try
            {
                string assetsDir = Path.Combine(AppContext.BaseDirectory, "assets");
                if (!Directory.Exists(assetsDir)) Directory.CreateDirectory(assetsDir);

                var images = Directory.EnumerateFiles(assetsDir, "*.*")
                    .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
                foreach (var img in images)
                {
                    try { backgroundImages.Add(Image.FromFile(img)); } catch { }
                }

                wavFiles.AddRange(Directory.EnumerateFiles(assetsDir, "*.wav", SearchOption.TopDirectoryOnly));
            }
            catch { }
        }

        private void StartUiTimer()
        {
            uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            uiTimer.Tick += async (_, __) => await UpdateCountdownLabelsAsync();
            uiTimer.Start();
        }

        private async Task UpdateCountdownLabelsAsync()
        {
            if (prayerTimes.Count == 0)
            {
                await RefreshPrayerTimesAsync();
            }

            DateTime now = DateTime.Now;
            (string? Name, DateTime Time) next;
            lock (prayerTimes) next = GetNextPrayerDateTime(now);

            if (next.Name == null)
            {
                lblNextPrayer.Text = "Next: --";
                lblCountdown.Text = "--:--:--";
                return;
            }

            TimeSpan remaining = next.Time - now;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            lblNextPrayer.Text = $"Next: {next.Name} ({next.Time:yyyy-MM-dd HH:mm:ss})";
            lblCountdown.Text = $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";

            if (remaining <= TimeSpan.FromSeconds(1))
            {
                ShowWarning();
            }
        }

        private (string? Name, DateTime Time) GetNextPrayerDateTime(DateTime now)
        {
            foreach (var name in prayerOrder)
            {
                if (prayerTimes.TryGetValue(name, out var dt) && dt > now)
                    return (name, dt);
            }

            // Next day
            if (prayerTimes.TryGetValue(prayerOrder[0], out var first))
                return (prayerOrder[0], first.Date.AddDays(1) + first.TimeOfDay);

            return (null, DateTime.MaxValue);
        }

        private void StartBackgroundRefresh()
        {
            refreshCts?.Cancel();
            refreshCts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                try
                {
                    await RefreshPrayerTimesAsync();

                    while (!refreshCts!.IsCancellationRequested)
                    {
                        await Task.Delay(refreshInterval, refreshCts.Token);
                        await RefreshPrayerTimesAsync();
                    }
                }
                catch (TaskCanceledException) { }
                catch { }
            }, refreshCts.Token);
        }

        private async Task DetectLocationAndRefreshAsync()
        {
            try
            {
                using var resp = await httpClient.GetAsync("http://ip-api.com/json");
                if (!resp.IsSuccessStatusCode) return;

                await using var s = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(s);

                if (doc.RootElement.TryGetProperty("city", out var cityEl))
                {
                    var c = cityEl.GetString();
                    if (!string.IsNullOrWhiteSpace(c)) city = c;
                }
                if (doc.RootElement.TryGetProperty("country", out var countryEl))
                {
                    var c = countryEl.GetString();
                    if (!string.IsNullOrWhiteSpace(c)) country = c;
                }
                if (doc.RootElement.TryGetProperty("lat", out var latEl) && doc.RootElement.TryGetProperty("lon", out var lonEl))
                {
                    if (latEl.TryGetDouble(out var lat) && lonEl.TryGetDouble(out var lon))
                    {
                        latitude = lat;
                        longitude = lon;
                    }
                }
            }
            catch { }

            await RefreshPrayerTimesAsync(latitude, longitude);
        }

        private async Task RefreshPrayerTimesAsync(double? lat = null, double? lon = null)
        {
            try
            {
                string url;
                if (lat.HasValue && lon.HasValue)
                {
                    url = $"https://api.aladhan.com/v1/timings?latitude={lat.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}&longitude={lon.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}&method=2";
                    latitude = lat;
                    longitude = lon;
                }
                else if (latitude.HasValue && longitude.HasValue)
                {
                    url = $"https://api.aladhan.com/v1/timings?latitude={latitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}&longitude={longitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}&method=2";
                }
                else
                {
                    url = $"https://api.aladhan.com/v1/timingsByCity?city={Uri.EscapeDataString(city)}&country={Uri.EscapeDataString(country)}&method=2";
                }

                using var resp = await httpClient.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return;

                await using var s = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(s);

                if (!doc.RootElement.TryGetProperty("data", out var dataEl) || !dataEl.TryGetProperty("timings", out var timingsEl)) return;

                var today = DateTime.Now.Date;
                var newTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

                foreach (var name in prayerOrder)
                {
                    if (!timingsEl.TryGetProperty(name, out var tVal)) continue;
                    var str = tVal.GetString();
                    if (string.IsNullOrWhiteSpace(str)) continue;

                    var clean = str.Split(' ')[0].Trim();
                    if (TimeSpan.TryParse(clean, out var ts))
                        newTimes[name] = today + ts;
                }

                if (newTimes.Count > 0)
                {
                    lock (prayerTimes)
                    {
                        prayerTimes.Clear();
                        foreach (var kv in newTimes)
                            prayerTimes[kv.Key] = kv.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to refresh prayer times: {ex}");
            }
        }

        private void ShowLocationDialog()
        {
            using var dlg = new Form
            {
                Text = "Set Location for Prayer Times",
                ClientSize = new Size(360, 140),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent
            };

            var lblCity = new Label { Text = "City:", Location = new Point(12, 15), AutoSize = true };
            var txtCity = new TextBox { Text = city, Location = new Point(80, 12), Width = 260 };
            var lblCountry = new Label { Text = "Country:", Location = new Point(12, 48), AutoSize = true };
            var txtCountry = new TextBox { Text = country, Location = new Point(80, 45), Width = 260 };

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(170, 90) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(255, 90) };

            dlg.Controls.AddRange(new Control[] { lblCity, txtCity, lblCountry, txtCountry, ok, cancel });
            dlg.AcceptButton = ok;
            dlg.CancelButton = cancel;

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                city = txtCity.Text.Trim();
                country = txtCountry.Text.Trim();
                latitude = null;
                longitude = null;
                _ = RefreshPrayerTimesAsync();
            }
        }

        private void ShowWarning()
        {
            var images = backgroundImages.ToList();
            var wavs = wavFiles.ToList();
            var w = new WarningForm(images, wavs);
            w.Show();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.Shift && e.KeyCode == Keys.D)
            {
                ShowWarning();
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Escape) Close();
            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowExit)
            {
                e.Cancel = true;
                MinimizeToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            uiTimer?.Stop();
            uiTimer?.Dispose();
            refreshCts?.Cancel();
            notifyIcon?.Dispose();
            base.OnFormClosed(e);
        }
    }
}