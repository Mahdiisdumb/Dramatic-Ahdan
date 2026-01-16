using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DramaticAdhan
{
    public partial class WarningForm : Form
    {
        private readonly List<Image> backgrounds;
        private readonly List<string> wavFiles;
        private CancellationTokenSource? audioCts;
        private Task? audioTask;
        private SoundPlayer? activePlayer;
        private SoundPlayer? bgmPlayer;

        public WarningForm(List<Image> backgrounds, List<string> wavFiles)
        {
            InitializeComponent();

            this.backgrounds = backgrounds ?? new List<Image>();
            this.wavFiles = wavFiles ?? new List<string>();

            // Fullscreen, borderless
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            TopMost = true;
            KeyPreview = true;

            // Set background image: prefer ./bg.png, fallback to random loaded image
            SetBackgroundImage();

            // Add fallback controls if none exist
            AddFallbackControls();

            // Start audio playback
            StartAudioLoop();
        }

        private void SetBackgroundImage()
        {
            var bgCandidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "bg.png"),
                Path.Combine(Environment.CurrentDirectory, "bg.png")
            };

            string? foundBg = null;
            foreach (var p in bgCandidates)
            {
                if (File.Exists(p))
                {
                    foundBg = p;
                    break;
                }
            }

            if (foundBg != null)
            {
                try
                {
                    BackgroundImage = Image.FromFile(foundBg);
                    BackgroundImageLayout = ImageLayout.Stretch;
                    return;
                }
                catch { /* tolerate load error */ }
            }

            // fallback to random background
            if (backgrounds.Count > 0)
            {
                BackgroundImage = backgrounds[new Random().Next(backgrounds.Count)];
                BackgroundImageLayout = ImageLayout.Stretch;
            }
        }

        private void AddFallbackControls()
        {
            if (Controls.Count == 0)
            {
                var msg = new Label
                {
                    Text = "Pray.",
                    Font = new Font("Arial", 48, FontStyle.Bold),
                    ForeColor = Color.White,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(50, 50)
                };
                Controls.Add(msg);
            }
        }

        private void StartAudioLoop()
        {
            // Prefer bgm.wav
            var bgmCandidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "bgm.wav"),
                Path.Combine(Environment.CurrentDirectory, "bgm.wav")
            };

            string? foundBgm = null;
            foreach (var p in bgmCandidates)
            {
                if (File.Exists(p))
                {
                    foundBgm = p;
                    break;
                }
            }

            if (foundBgm != null)
            {
                try
                {
                    bgmPlayer = new SoundPlayer(foundBgm);
                    bgmPlayer.LoadAsync();
                    bgmPlayer.PlayLooping();
                    return;
                }
                catch
                {
                    bgmPlayer?.Dispose();
                    bgmPlayer = null;
                }
            }

            // Fallback: sequential playback of wavFiles on background thread
            if (wavFiles.Count == 0) return;

            audioCts = new CancellationTokenSource();
            var token = audioCts.Token;

            audioTask = Task.Run(() =>
            {
                var idx = 0;
                while (!token.IsCancellationRequested)
                {
                    var wav = wavFiles[idx % wavFiles.Count];
                    try
                    {
                        using var player = new SoundPlayer(wav);
                        activePlayer = player;
                        player.PlaySync();
                        activePlayer = null;
                    }
                    catch { /* skip bad files */ }

                    idx++;
                }
            }, token);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
                Close();

            base.OnKeyDown(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Stop bgm player
            try
            {
                bgmPlayer?.Stop();
                bgmPlayer?.Dispose();
                bgmPlayer = null;
            }
            catch { }

            // Stop background audio task
            try
            {
                audioCts?.Cancel();
                if (audioTask != null)
                {
                    try { audioTask.Wait(500); } catch { }
                    audioTask = null;
                }
            }
            catch { }

            // Stop active player
            try
            {
                activePlayer?.Stop();
                activePlayer?.Dispose();
                activePlayer = null;
            }
            catch { }

            audioCts?.Dispose();
            audioCts = null;

            base.OnFormClosed(e);
        }
    }
}