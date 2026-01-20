using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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

        private readonly Label lblNextPrayer;
        private readonly Label lblCountdown;
        private readonly Button btnRefreshNow;
        private readonly Button btnSettings;

        private readonly Dictionary<string, DateTime> prayerTimes =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly string[] prayerOrder =
            { "Fajr", "Dhuhr", "Asr", "Maghrib", "Isha" };

        private readonly List<Image> backgroundImages = new();
        private readonly List<string> wavFiles = new();

        private static readonly HttpClient httpClient = new();
        private readonly TimeSpan refreshInterval = TimeSpan.FromHours(6);
        private CancellationTokenSource? refreshCts;

        private bool allowExit = false;
        private readonly AppConfig config;

        public MainForm()
        {
            InitializeComponent();
            KeyPreview = true;

            config = AppConfig.Load();

            lblNextPrayer = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                Location = new Point(20, 20),
                Text = "Next: --"
            };

            lblCountdown = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 14F),
                Location = new Point(20, 55),
                Text = "--:--:--"
            };

            btnRefreshNow = new Button
            {
                Text = "Refresh Times",
                Location = new Point(20, 95),
                AutoSize = true
            };
            btnRefreshNow.Click += async (_, _) =>
                await RefreshPrayerTimesAsync().ConfigureAwait(true);

            btnSettings = new Button
            {
                Text = "Location",
                Location = new Point(150, 95),
                AutoSize = true
            };
            btnSettings.Click += (_, _) => ShowLocationDialog();

            Controls.AddRange(new Control[]
            {
                lblNextPrayer,
                lblCountdown,
                btnRefreshNow,
                btnSettings
            });

            SetupNotifyIcon();
            Resize += MainForm_Resize;

            LoadAssets();
            StartBackgroundRefresh();
            StartUiTimer();
            _ = DetectLocationAndRefreshAsync();

            Shown += (_, _) => MinimizeToTray();
        }

        // ===================== TRAY =====================

        private void SetupNotifyIcon()
        {
            notifyIcon = new NotifyIcon
            {
                Visible = true,
                Text = "Dramatic Adhan",
                Icon = Icon
            };

            var ctx = new ContextMenuStrip();
            ctx.Items.Add("Show", null, (_, _) => RestoreFromTray());
            ctx.Items.Add("Exit", null, (_, _) =>
            {
                allowExit = true;
                Close();
            });

            notifyIcon.ContextMenuStrip = ctx;
            notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
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

        private void MainForm_Resize(object? sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
                MinimizeToTray();
        }

        // ===================== ASSETS =====================

        private void LoadAssets()
        {
            try
            {
                string dir = Path.Combine(AppContext.BaseDirectory, "assets");
                Directory.CreateDirectory(dir);

                foreach (var f in Directory.GetFiles(dir, "*.png"))
                    backgroundImages.Add(Image.FromFile(f));

                foreach (var f in Directory.GetFiles(dir, "*.jpg"))
                    backgroundImages.Add(Image.FromFile(f));

                wavFiles.AddRange(Directory.GetFiles(dir, "*.wav"));
            }
            catch { }
        }

        // ===================== TIMERS =====================

        private void StartUiTimer()
        {
            uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            uiTimer.Tick += async (_, _) =>
                await UpdateCountdownLabelsAsync().ConfigureAwait(true);
            uiTimer.Start();
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
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            lblNextPrayer.Text =
                $"Next: {next.Name} ({next.Time:HH:mm})";

            lblCountdown.Text =
                $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";

            if (remaining <= TimeSpan.FromSeconds(1))
                ShowWarning();
        }

        // ===================== SHIA DAY LOGIC =====================

        private (string? Name, DateTime Time)
            GetNextPrayerDateTime(DateTime now)
        {
            DateTime dayBase;

            if (config.IsShia)
            {
                // Shia day begins at sunset (~18:00)
                dayBase = DateTime.Today.AddHours(18);
                if (now < dayBase)
                    dayBase = dayBase.AddDays(-1);
            }
            else
            {
                dayBase = DateTime.Today;
            }

            lock (prayerTimes)
            {
                foreach (var name in prayerOrder)
                {
                    if (prayerTimes.TryGetValue(name, out var t) && t > now)
                        return (name, t);
                }

                if (prayerTimes.TryGetValue(prayerOrder[0], out var first))
                    return (prayerOrder[0],
                        dayBase.AddDays(1) + first.TimeOfDay);
            }

            return (null, DateTime.MaxValue);
        }

        // ===================== NETWORK =====================

        private void StartBackgroundRefresh()
        {
            refreshCts?.Cancel();
            refreshCts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!refreshCts.IsCancellationRequested)
                {
                    await RefreshPrayerTimesAsync();
                    await Task.Delay(refreshInterval, refreshCts.Token);
                }
            });
        }
protected override void OnKeyDown(KeyEventArgs e)
{
    // DEBUG: force adhan warning
    if (e.Control && e.Shift && e.KeyCode == Keys.D)
    {
        ShowWarning();
        e.Handled = true;
        return;
    }

    // Quick exit / minimize
    if (e.KeyCode == Keys.Escape)
    {
        Close();
        e.Handled = true;
        return;
    }

    base.OnKeyDown(e);
}
        private async Task DetectLocationAndRefreshAsync()
        {
            try
            {
                if (config.Latitude.HasValue && config.Longitude.HasValue)
                {
                    await RefreshPrayerTimesAsync(
                        config.Latitude, config.Longitude);
                    return;
                }

                var r = await httpClient.GetAsync("http://ip-api.com/json");
                if (!r.IsSuccessStatusCode) return;

                using var s = await r.Content.ReadAsStreamAsync();
                using var j = await JsonDocument.ParseAsync(s);

                if (j.RootElement.TryGetProperty("lat", out var lat) &&
                    j.RootElement.TryGetProperty("lon", out var lon))
                {
                    config.Latitude = lat.GetDouble();
                    config.Longitude = lon.GetDouble();
                    config.Save();
                }
            }
            catch { }

            await RefreshPrayerTimesAsync(
                config.Latitude, config.Longitude);
        }

        private async Task RefreshPrayerTimesAsync(
            double? lat = null, double? lon = null)
        {
            try
            {
                string url =
                    lat.HasValue && lon.HasValue
                        ? $"https://api.aladhan.com/v1/timings?latitude={lat}&longitude={lon}&method=0"
                        : $"https://api.aladhan.com/v1/timingsByCity?city={config.City}&country={config.Country}&method=0";

                var r = await httpClient.GetAsync(url);
                if (!r.IsSuccessStatusCode) return;

                using var s = await r.Content.ReadAsStreamAsync();
                using var j = await JsonDocument.ParseAsync(s);

                var timings =
                    j.RootElement.GetProperty("data").GetProperty("timings");

                var today = DateTime.Today;
                var newTimes = new Dictionary<string, DateTime>();

                foreach (var p in prayerOrder)
                {
                    if (!timings.TryGetProperty(p, out var v)) continue;
                    if (TimeSpan.TryParse(v.GetString()?.Split(' ')[0], out var ts))
                        newTimes[p] = today + ts;
                }

                lock (prayerTimes)
                {
                    prayerTimes.Clear();
                    foreach (var kv in newTimes)
                        prayerTimes[kv.Key] = kv.Value;
                }
            }
            catch { }
        }

        // ===================== UI =====================

        private void ShowLocationDialog()
        {
            MessageBox.Show(
                "Location changes require restarting setup.\nDelete config.json.",
                "Info",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ShowWarning()
        {
            var w = new WarningForm(backgroundImages, wavFiles);
            w.Show();
        }

        // ===================== EXIT =====================

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
            refreshCts?.Cancel();
            notifyIcon?.Dispose();
            base.OnFormClosed(e);
        }
    }
}