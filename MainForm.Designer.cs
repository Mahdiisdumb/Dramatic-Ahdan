using System.ComponentModel;

using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace DramaticAdhan
{
    partial class MainForm
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
            // MainForm
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(800, 450);
            this.Name = "MainForm";
            this.Text = "Dramatic Adhan";
            this.ResumeLayout(false);
        }

        #endregion
    }
}