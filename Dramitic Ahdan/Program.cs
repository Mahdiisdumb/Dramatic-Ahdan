using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DramaticAdhan
{
    internal static class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var main = new MainForm();

            // Prefer "./ico.png" (exe dir then current dir). If found, convert PNG -> Icon and set form icon.
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "ico.png"),
                Path.Combine(Environment.CurrentDirectory, "ico.png"),
                "ico.png"
            };

            foreach (var path in candidates)
            {
                try
                {
                    if (!File.Exists(path)) continue;

                    using var bmp = new Bitmap(path);
                    IntPtr hIcon = bmp.GetHicon();
                    using var tempIcon = Icon.FromHandle(hIcon);
                    // Clone so we can destroy the native handle immediately
                    main.Icon = (Icon)tempIcon.Clone();
                    DestroyIcon(hIcon);
                    break;
                }
                catch
                {
                    // ignore and continue to next candidate
                }
            }

            Application.Run(main);
        }
    }
}
