﻿
using SrvSurvey.game;

namespace SrvSurvey
{
    partial class PlotGrounded
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            Game.update -= Game_modeChanged;
            if (game?.status != null)
                game.status.StatusChanged -= Status_StatusChanged;

            if (game?.journals != null)
                game.journals.onJournalEntry -= Journals_onJournalEntry;
            if (game?.nearBody != null)
                game.nearBody.bioScanEvent -= NearBody_bioScanEvent;

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // PlotGrounded
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoValidate = System.Windows.Forms.AutoValidate.Disable;
            this.BackColor = System.Drawing.Color.Black;
            this.CausesValidation = false;
            this.ClientSize = new System.Drawing.Size(320, 400);
            this.ControlBox = false;
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "PlotGrounded";
            this.Opacity = 0.5D;
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "PlotGrounded";
            this.TopMost = true;
            this.Load += new System.EventHandler(this.PlotGrounded_Load);
            this.Paint += new System.Windows.Forms.PaintEventHandler(this.PlotGrounded_Paint);
            this.ResumeLayout(false);

        }

        #endregion
    }
}