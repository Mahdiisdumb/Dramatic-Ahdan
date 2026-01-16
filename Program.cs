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

            // Load application icon
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory, "ico.png"),
                Path.Combine(Environment.CurrentDirectory, "ico.png"),
                "ico.ico"
            };

            foreach (var path in candidates)
            {
                try
                {
                    if (!File.Exists(path)) continue;

                    using var bmp = new Bitmap(path);
                    IntPtr hIcon = bmp.GetHicon();
                    main.Icon = Icon.FromHandle(hIcon).Clone() as Icon;
                    DestroyIcon(hIcon);
                    break;
                }
                catch { }
            }

            Application.Run(main);
        }
    }
}