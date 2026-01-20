using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DramaticAdhan
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var config = AppConfig.Load();

            if (config.IsFirstRun)
            {
                using var setup = new SetupForm();
                if (setup.ShowDialog() != DialogResult.OK)
                    return;

                config.City = setup.City;
                config.Country = setup.Country;
                config.IsShia = setup.IsShia;
                config.IsFirstRun = false;
                config.Save();
            }

            var main = new MainForm();

            string[] iconPaths =
            {
                Path.Combine(AppContext.BaseDirectory, "ico.png"),
                Path.Combine(Environment.CurrentDirectory, "ico.png"),
                "ico.ico"
            };

            foreach (var path in iconPaths)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    using var bmp = new Bitmap(path);
                    IntPtr hIcon = bmp.GetHicon();
                    main.Icon = Icon.FromHandle(hIcon);
                    DestroyIcon(hIcon);
                    break;
                }
                catch { }
            }

            Application.Run(main);
        }
    }
}