using System.ComponentModel;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace DramaticAdhan
{
    partial class WarningForm
    {
        private IContainer? components;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            components = new Container();
            this.SuspendLayout();
            // 
            // WarningForm
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(800, 450);
            this.Name = "WarningForm";
            this.Text = "Warning";
            this.ResumeLayout(false);
        }

        #endregion
    }
}