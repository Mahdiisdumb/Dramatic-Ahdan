using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

namespace DramaticAdhan
{
    public partial class MainForm : Form
    {
        private System.Windows.Forms.Timer? uiTimer;
        private NotifyIcon? notifyIcon;

        private readonly Label lblNextPrayer;
        private readonly Label lblCountdown;
        private readonly Button btnRefreshNow;
        private readonly Button btnSettings;

        private readonly Dictionary<string, DateTime> prayerTimes = new(StringComparer.OrdinalIgnoreCase);
        private readonly string[] prayerOrder = new[] { "Fajr", "Dhuhr", "Asr", "Maghrib", "Isha" };

        private readonly List<Image> backgroundImages = new();
        private readonly List<string> wavFiles = new();

        private static readonly HttpClient httpClient = new();
        private readonly TimeSpan refreshInterval = TimeSpan.FromHours(6);
        private CancellationTokenSource? refreshCts;

        private bool allowExit = false;

        private AppConfig config;

        public MainForm()
        {
            InitializeComponent();
            KeyPreview = true;

            config = AppConfig.Load();

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

            btnRefreshNow = new Button
            {
                Text = "Refresh Times",
                Location = new Point(20, 92),
                AutoSize = true
            };
            btnRefreshNow.Click += async (s, e) => await RefreshPrayerTimesAsync().ConfigureAwait(true);
            Controls.Add(btnRefreshNow);

            btnSettings = new Button
            {
                Text = "Location",
                Location = new Point(150, 92),
                AutoSize = true
            };
            btnSettings.Click += (s, e) => ShowLocationDialog();
            Controls.Add(btnSettings);

            SetupNotifyIcon();
            Resize += MainForm_Resize;

            LoadAssets();
            StartBackgroundRefresh();
            StartUiTimer();
            _ = DetectLocationAndRefreshAsync();

            Shown += MainForm_Shown;
        }

        private void MainForm_Shown(object? sender, EventArgs e)
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
        }

        private void SetupNotifyIcon()
        {
            notifyIcon = new NotifyIcon { Visible = true, Text = "Dramatic Adhan" };
            try { if (Icon != null) notifyIcon.Icon = Icon; } catch { }

            var ctx = new ContextMenuStrip();
            var showItem = new ToolStripMenuItem("Show");
            showItem.Click += (s, e) => RestoreFromTray();
            ctx.Items.Add(showItem);

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) =>
            {
                allowExit = true;
                Close();
            };
            ctx.Items.Add(exitItem);

            notifyIcon.ContextMenuStrip = ctx;
            notifyIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private void MinimizeToTray()
        {
            try
            {
                Hide();
                ShowInTaskbar = false;
                WindowState = FormWindowState.Minimized;
            }
            catch { }
        }

        private void RestoreFromTray()
        {
            try
            {
                Show();
                WindowState = FormWindowState.Normal;
                ShowInTaskbar = true;
                Activate();
            }
            catch { }
        }

        private void MainForm_Resize(object? sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
                MinimizeToTray();
        }

        private void LoadAssets()
        {
            try
            {
                string exeDir = AppContext.BaseDirectory;
                string assetsDir = Path.Combine(exeDir, "assets");
                if (!Directory.Exists(assetsDir)) Directory.CreateDirectory(assetsDir);

                foreach (var f in Directory.EnumerateFiles(assetsDir, "*.png"))
                    backgroundImages.Add(Image.FromFile(f));

                foreach (var f in Directory.EnumerateFiles(assetsDir, "*.jpg"))
                    backgroundImages.Add(Image.FromFile(f));

                wavFiles.AddRange(Directory.EnumerateFiles(assetsDir, "*.wav"));
            }
            catch { }
        }

        private void StartUiTimer()
        {
            uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            uiTimer.Tick += async (s, e) => await UpdateCountdownLabelsAsync().ConfigureAwait(true);
            uiTimer.Start();
            _ = UpdateCountdownLabelsAsync();
        }

        private async Task UpdateCountdownLabelsAsync()
        {
            if (prayerTimes.Count == 0)
                await RefreshPrayerTimesAsync().ConfigureAwait(true);

            var now = DateTime.Now;
            var next = GetNextPrayerDateTime(now);
            if (next.Name == null)
            {
                lblNextPrayer.Text = "Next: --";
                lblCountdown.Text = "--:--:--";
                return;
            }

            var remaining = next.Time - now;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            lblNextPrayer.Text = $"Next: {next.Name} ({next.Time:HH:mm})";
            lblCountdown.Text = $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";

            if (remaining <= TimeSpan.FromSeconds(1))
                ShowWarning();
        }

        private (string? Name, DateTime Time) GetNextPrayerDateTime(DateTime now)
        {
            lock (prayerTimes)
            {
                foreach (var name in prayerOrder)
                {
                    if (prayerTimes.TryGetValue(name, out var dt) && dt > now)
                        return (name, dt);
                }

                if (prayerTimes.TryGetValue(prayerOrder[0], out var firstToday))
                    return (prayerOrder[0], firstToday.Date.AddDays(1) + firstToday.TimeOfDay);
            }
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
                    await RefreshPrayerTimesAsync().ConfigureAwait(false);
                    while (!refreshCts!.IsCancellationRequested)
                    {
                        await Task.Delay(refreshInterval, refreshCts.Token).ConfigureAwait(false);
                        await RefreshPrayerTimesAsync().ConfigureAwait(false);
                    }
                }
                catch { }
            }, refreshCts.Token);
        }

        private async Task DetectLocationAndRefreshAsync()
        {
            try
            {
                if (config.Latitude.HasValue && config.Longitude.HasValue)
                {
                    await RefreshPrayerTimesAsync(config.Latitude, config.Longitude);
                    return;
                }

                using var resp = await httpClient.GetAsync("http://ip-api.com/json");
                if (!resp.IsSuccessStatusCode) return;

                await using var s = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(s);

                if (doc.RootElement.TryGetProperty("city", out var cityEl))
                    config.City = cityEl.GetString() ?? config.City;

                if (doc.RootElement.TryGetProperty("country", out var countryEl))
                    config.Country = countryEl.GetString() ?? config.Country;

                if (doc.RootElement.TryGetProperty("lat", out var latEl) &&
                    doc.RootElement.TryGetProperty("lon", out var lonEl))
                {
                    if (latEl.TryGetDouble(out var lat) && lonEl.TryGetDouble(out var lon))
                    {
                        config.Latitude = lat;
                        config.Longitude = lon;
                    }
                }

                config.Save();
            }
            catch { }

            await RefreshPrayerTimesAsync(config.Latitude, config.Longitude);
        }

        private async Task RefreshPrayerTimesAsync(double? lat = null, double? lon = null)
        {
            try
            {
                string url;
                if (lat.HasValue && lon.HasValue)
                    url = $"https://api.aladhan.com/v1/timings?latitude={lat.Value}&longitude={lon.Value}&method=2";
                else
                    url = $"https://api.aladhan.com/v1/timingsByCity?city={Uri.EscapeDataString(config.City)}&country={Uri.EscapeDataString(config.Country)}&method=2";

                using var resp = await httpClient.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return;

                await using var s = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(s);

                if (!doc.RootElement.TryGetProperty("data", out var dataEl)) return;
                if (!dataEl.TryGetProperty("timings", out var timingsEl)) return;

                var today = DateTime.Now.Date;
                var newTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

                foreach (var name in prayerOrder)
                {
                    if (!timingsEl.TryGetProperty(name, out var tVal)) continue;
                    var str = tVal.GetString()?.Split(' ')[0];
                    if (str == null) continue;

                    if (TimeSpan.TryParse(str, out var ts))
                        newTimes[name] = today + ts;
                }

                if (newTimes.Count > 0)
                {
                    lock (prayerTimes)
                    {
                        prayerTimes.Clear();
                        foreach (var kv in newTimes) prayerTimes[kv.Key] = kv.Value;
                    }
                }
            }
            catch { }
        }

        private void ShowLocationDialog()
        {
            using var dlg = new Form
            {
                Text = "Set Location",
                ClientSize = new Size(360, 140),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent
            };

            var lblCity = new Label { Text = "City:", Location = new Point(12, 15), AutoSize = true };
            var txtCity = new TextBox { Text = config.City, Location = new Point(80, 12), Width = 260 };
            var lblCountry = new Label { Text = "Country:", Location = new Point(12, 48), AutoSize = true };
            var txtCountry = new TextBox { Text = config.Country, Location = new Point(80, 45), Width = 260 };

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(170, 90) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(255, 90) };

            dlg.Controls.AddRange(new Control[] { lblCity, txtCity, lblCountry, txtCountry, ok, cancel });
            dlg.AcceptButton = ok;
            dlg.CancelButton = cancel;

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                config.City = txtCity.Text.Trim();
                config.Country = txtCountry.Text.Trim();
                config.Latitude = null;
                config.Longitude = null;
                config.Save();

                _ = RefreshPrayerTimesAsync();
            }
        }

        private void ShowWarning()
        {
            var w = new WarningForm(backgroundImages, wavFiles);
            w.Show();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.Shift && e.KeyCode == Keys.D) ShowWarning();
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
            try { notifyIcon?.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }
}