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

            // Ensure we use ./bg.png (prefer executable dir then working dir)
            var bgCandidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "bg.png"),
                Path.Combine(Environment.CurrentDirectory, "bg.png"),
                Path.GetFullPath("bg.prayng")
            };
            string? foundBg = null;
            foreach (var p in bgCandidates)
            {
                try { if (File.Exists(p)) { foundBg = p; break; } } catch { }
            }

            if (foundBg != null)
            {
                try
                {
                    this.BackgroundImage = Image.FromFile(foundBg);
                    this.BackgroundImageLayout = ImageLayout.Stretch;
                }
                catch { /* tolerate load error */ }
            }
            else if (this.backgrounds.Count > 0)
            {
                // fallback to random loaded background if no ./bg.png
                var img = this.backgrounds[new Random().Next(0, this.backgrounds.Count)];
                this.BackgroundImage = img;
                this.BackgroundImageLayout = ImageLayout.Stretch;
            }

            // message and button created in designer previously are preserved if present;
            // but add fallback UI if not
            AddFallbackControls();

            // Start audio playback: prefer ./bgm.wav, else fall back to wavFiles list
            StartAudioLoop();
        }

        private void AddFallbackControls()
        {
            // If no controls defined in designer, add message + close
            if (this.Controls.Count == 0)
            {
                var msg = new Label
                {
                    Text = "Pray.",
                    Font = new Font("Arial", 48, FontStyle.Bold),
                    ForeColor = Color.Black,
                    BackColor = Color.Red,
                    AutoSize = true,
                    Location = new Point(50, 50)
                };
                Controls.Add(msg);

            
            }
        }

        private void StartAudioLoop()
        {
            // First, prefer './bgm.wav' (check exe dir then working dir then relative)
            var bgmCandidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "bgm.wav"),
                Path.Combine(Environment.CurrentDirectory, "bgm.wav"),
                Path.GetFullPath("bgm.wav")
            };

            string? foundBgm = null;
            foreach (var p in bgmCandidates)
            {
                try { if (File.Exists(p)) { foundBgm = p; break; } } catch { }
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
                    // fall through to fallback playback
                    try { bgmPlayer?.Dispose(); bgmPlayer = null; } catch { }
                }
            }

            // Fallback: if no bgm.wav, play any loaded wav files sequentially on background thread
            if (wavFiles.Count == 0)
                return;

            audioCts = new CancellationTokenSource();
            var token = audioCts.Token;

            audioTask = Task.Run(() =>
            {
                try
                {
                    var idx = 0;
                    while (!token.IsCancellationRequested)
                    {
                        var wav = wavFiles[idx % wavFiles.Count];
                        try
                        {
                            var player = new SoundPlayer(wav);
                            activePlayer = player;
                            // PlaySync blocks current thread — OK because we're on a background thread
                            player.PlaySync();
                            // dispose after finished
                            player.Dispose();
                            activePlayer = null;
                        }
                        catch
                        {
                            // skip bad file
                        }

                        idx++;
                    }
                }
                catch (OperationCanceledException) { }
                catch { /* ignore other playback errors */ }
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
            try
            {
                // Stop any looping bgm player
                try
                {
                    bgmPlayer?.Stop();
                    bgmPlayer?.Dispose();
                    bgmPlayer = null;
                }
                catch { }

                // Stop background-sequence playback
                audioCts?.Cancel();
                if (audioTask != null)
                {
                    try { audioTask.Wait(500); } catch { }
                    audioTask = null;
                }

                try
                {
                    activePlayer?.Stop();
                    activePlayer?.Dispose();
                    activePlayer = null;
                }
                catch { }
            }
            catch { }
            finally
            {
                try { audioCts?.Dispose(); } catch { }
                audioCts = null;
            }

            base.OnFormClosed(e);
        }
    }
}