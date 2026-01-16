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

            // Look for ico.png in exe dir or current dir
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory, "ico.png"),
                Path.Combine(Environment.CurrentDirectory, "ico.png")
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;

                try
                {
                    using var bmp = new Bitmap(path);
                    IntPtr hIcon = bmp.GetHicon();

                    // Clone to managed Icon before destroying handle
                    main.Icon = Icon.FromHandle(hIcon).Clone() as Icon;
                    DestroyIcon(hIcon);

                    break; // stop after first successful load
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load icon '{path}': {ex.Message}");
                }
            }

            Application.Run(main);
        }
    }
}