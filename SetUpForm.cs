using System;
using System.Drawing;
using System.Windows.Forms;

namespace DramaticAdhan
{
    public class SetupForm : Form
    {
        public string City => txtCity.Text.Trim();
        public string Country => txtCountry.Text.Trim();
        public bool IsShia => rbShia.Checked;

        private TextBox txtCity;
        private TextBox txtCountry;
        private RadioButton rbSunni;
        private RadioButton rbShia;

        public SetupForm()
        {
            Text = "First Time Setup";
            ClientSize = new Size(380, 220);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;

            var lblIntro = new Label
            {
                Text = "Welcome.\nTell the app where you live and how to calculate prayer times.",
                AutoSize = true,
                Location = new Point(12, 12)
            };

            var lblCity = new Label { Text = "City:", Location = new Point(12, 70) };
            txtCity = new TextBox { Location = new Point(100, 66), Width = 250 };

            var lblCountry = new Label { Text = "Country:", Location = new Point(12, 100) };
            txtCountry = new TextBox { Location = new Point(100, 96), Width = 250 };

            rbSunni = new RadioButton
            {
                Text = "Sunni (Fajr → Isha)",
                Location = new Point(100, 130),
                Checked = true
            };

            rbShia = new RadioButton
            {
                Text = "Shia (Sunset → Sunrise)",
                Location = new Point(100, 155)
            };

            var btnOk = new Button
            {
                Text = "Continue",
                Location = new Point(260, 185),
                DialogResult = DialogResult.OK
            };

            Controls.AddRange(new Control[]
            {
                lblIntro,
                lblCity, txtCity,
                lblCountry, txtCountry,
                rbSunni, rbShia,
                btnOk
            });

            AcceptButton = btnOk;
        }
    }
}
