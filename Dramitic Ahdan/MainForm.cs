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

        // UI for countdown
        private readonly Label lblNextPrayer;
        private readonly Label lblCountdown;
        private readonly Button btnRefreshNow;
        private readonly Button btnSettings;

        // Prayer times fetched from API (per-day DateTime values)
        private readonly Dictionary<string, DateTime> prayerTimes = new(StringComparer.OrdinalIgnoreCase);

        // Asset lists
        private readonly List<Image> backgroundImages = new();
        private readonly List<string> wavFiles = new();

        // Location / settings
        private string city = "Mecca";
        private string country = "Saudi Arabia";
        private double? latitude;
        private double? longitude;

        // Http client reused
        private static readonly HttpClient httpClient = new();

        // Refresh schedule
        private readonly TimeSpan refreshInterval = TimeSpan.FromHours(6);
        private CancellationTokenSource? refreshCts;

        // Prayers in canonical order
        private readonly string[] prayerOrder = new[] { "Fajr", "Dhuhr", "Asr", "Maghrib", "Isha" };

        // When true allow the form to actually close (Exit from tray menu)
        private bool allowExit = false;

        public MainForm()
        {
            InitializeComponent();
            KeyPreview = true;

            // Create and style countdown labels
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

            // Tray icon + menu
            SetupNotifyIcon();

            // Ensure minimize-on-minimize-button behavior
            Resize += MainForm_Resize;

            LoadAssets();

            // Start background tasks
            StartBackgroundRefresh();

            // Start UI timer
            StartUiTimer();

            // Detect location and refresh on startup (fire-and-forget)
            _ = DetectLocationAndRefreshAsync();

            // Show a load message then minimize to tray when form is first shown
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
            catch
            {
                // ignore if balloon tip not supported
            }

            MinimizeToTray();
        }

        private void SetupNotifyIcon()
        {
            notifyIcon = new NotifyIcon
            {
                Visible = true,
                Text = "Dramatic Adhan"
            };

            // Try to use form icon if present
            try
            {
                if (this.Icon != null) notifyIcon.Icon = this.Icon;
            }
            catch { /* ignore */ }

            var ctx = new ContextMenuStrip();
            var showItem = new ToolStripMenuItem("Show")
            {
                Enabled = true
            };
            showItem.Click += (s, e) => RestoreFromTray();
            ctx.Items.Add(showItem);

            var exitItem = new ToolStripMenuItem("Exit");
            // Make Exit set allowExit and close the form so normal shutdown proceeds.
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
                // keep notify icon visible and hide the window
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
            // When the user minimizes the window, move it to tray instead of taskbar
            if (WindowState == FormWindowState.Minimized)
            {
                MinimizeToTray();
            }
        }

        private void LoadAssets()
        {
            try
            {
                string exeDir = AppContext.BaseDirectory;
                string assetsDir = Path.Combine(exeDir, "assets");
                if (!Directory.Exists(assetsDir))
                {
                    Directory.CreateDirectory(assetsDir);
                    return;
                }

                var imageFiles = Directory.EnumerateFiles(assetsDir, "*.*")
                    .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
                foreach (var img in imageFiles)
                {
                    try { backgroundImages.Add(Image.FromFile(img)); } catch { }
                }

                var wavs = Directory.EnumerateFiles(assetsDir, "*.wav", SearchOption.TopDirectoryOnly);
                wavFiles.AddRange(wavs);
            }
            catch { }
        }

        private void StartUiTimer()
        {
            uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            uiTimer.Tick += UiTimer_Tick;
            uiTimer.Start();
            _ = UpdateCountdownLabelsAsync();
        }

        private async void UiTimer_Tick(object? sender, EventArgs e)
        {
            await UpdateCountdownLabelsAsync().ConfigureAwait(true);
        }

        private async Task UpdateCountdownLabelsAsync()
        {
            if (prayerTimes.Count == 0)
            {
                await RefreshPrayerTimesAsync().ConfigureAwait(true);
            }

            var now = DateTime.Now;
            var next = GetNextPrayerDateTime(now);
            if (next.Name == null)
            {
                if (InvokeRequired)
                {
                    Invoke(() =>
                    {
                        lblNextPrayer.Text = "Next: --";
                        lblCountdown.Text = "--:--:--";
                    });
                }
                else
                {
                    lblNextPrayer.Text = "Next: --";
                    lblCountdown.Text = "--:--:--";
                }
                return;
            }

            var remaining = next.Time - now;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            if (InvokeRequired)
            {
                Invoke(() =>
                {
                    lblNextPrayer.Text = $"Next: {next.Name} ({next.Time:yyyy-MM-dd HH:mm:ss})";
                    lblCountdown.Text = $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
                });
            }
            else
            {
                lblNextPrayer.Text = $"Next: {next.Name} ({next.Time:yyyy-MM-dd HH:mm:ss})";
                lblCountdown.Text = $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            }

            // Trigger warning precisely when the next prayer time arrives (within the next second)
            if (next.Time - now <= TimeSpan.FromSeconds(1) && next.Time - now >= TimeSpan.Zero)
            {
                ShowWarning();
            }
        }

        private (string? Name, DateTime Time) GetNextPrayerDateTime(DateTime now)
        {
            lock (prayerTimes)
            {
                foreach (var name in prayerOrder)
                {
                    if (prayerTimes.TryGetValue(name, out var dt))
                    {
                        if (dt > now) return (name, dt);
                    }
                }

                if (prayerTimes.TryGetValue(prayerOrder[0], out var firstToday))
                {
                    var candidate = firstToday.Date.AddDays(1) + firstToday.TimeOfDay;
                    return (prayerOrder[0], candidate);
                }
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
                catch (TaskCanceledException) { }
                catch { }
            }, refreshCts.Token);
        }

        private async Task DetectLocationAndRefreshAsync()
        {
            try
            {
                // Detect location via public IP geolocation (ip-api.com)
                using var resp = await httpClient.GetAsync("http://ip-api.com/json").ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return;

                await using var s = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(s).ConfigureAwait(false);

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
            catch { /* ignore location failures and fallback to city/country */ }

            // Refresh times using detected location (if available)
            await RefreshPrayerTimesAsync(latitude, longitude).ConfigureAwait(false);
        }

        // If lat/lon provided use that endpoint; otherwise fallback to timingsByCity
        private async Task RefreshPrayerTimesAsync(double? lat = null, double? lon = null)
        {
            try
            {
                string url;
                if (lat.HasValue && lon.HasValue)
                {
                    url = $"https://api.aladhan.com/v1/timings?latitude={lat.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}&longitude={lon.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}&method=2";
                }
                else
                {
                    url = $"https://api.aladhan.com/v1/timingsByCity?city={Uri.EscapeDataString(city)}&country={Uri.EscapeDataString(country)}&method=2";
                }

                using var resp = await httpClient.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return;

                await using var s = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(s).ConfigureAwait(false);

                if (!doc.RootElement.TryGetProperty("data", out var dataEl)) return;
                if (!dataEl.TryGetProperty("timings", out var timingsEl)) return;

                var today = DateTime.Now.Date;
                var newTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

                foreach (var name in prayerOrder)
                {
                    if (!timingsEl.TryGetProperty(name, out var tVal)) continue;
                    var str = tVal.GetString();
                    if (string.IsNullOrWhiteSpace(str)) continue;

                    var clean = str.Split(' ')[0].Trim();
                    if (TimeSpan.TryParse(clean, out var ts))
                    {
                        var dt = today + ts;
                        newTimes[name] = dt;
                    }
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
            catch { /* keep previous times on failure */ }
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

        // Debug key: Ctrl+Shift+D -> show warning immediately
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.Shift && e.KeyCode == Keys.D)
            {
                ShowWarning();
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                Close();
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Prevent the form from closing unless the user chose Exit from the tray menu.
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