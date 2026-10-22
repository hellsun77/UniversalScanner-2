using System;
using System.Drawing;
using System.Windows.Forms;

namespace UniversalScanner
{
    public class SpecialThanksForm : Form
    {
        private Bitmap logoBitmap;
        private readonly Color bgDark = AppVisualTheme.Current.WindowBackground;
        private readonly Color textWhite = AppVisualTheme.Current.TextPrimary;
        private readonly Color textLightGray = AppVisualTheme.Current.TextSecondary;
        private readonly Color textGray = AppVisualTheme.Current.TextSecondary;
        private readonly Color accentCyan = AppVisualTheme.Current.AccentText;
        private readonly Color authorRed = AppVisualTheme.Current.Danger;

        public SpecialThanksForm()
        {
            Width = 640;
            Height = 440;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = bgDark;
            Text = "Credits";
            DoubleBuffered = true;
            logoBitmap = LoadApplicationIconBitmap();

            Controls.Add(CreateCenteredLabel("Universal Scanner", new Font("Segoe UI", 21, FontStyle.Bold), textWhite, 85, 40));
            Controls.Add(CreateCenteredLabel("Discover • Analyze • Identify", new Font("Segoe UI", 11, FontStyle.Regular), textLightGray, 125, 25));
            Controls.Add(CreateCenteredLabel("Anan Awwad", new Font("Segoe UI", 15, FontStyle.Bold), authorRed, 150, 30));
            
            Controls.Add(CreateCenteredLabel("SPECIAL THANKS", new Font("Segoe UI", 13, FontStyle.Bold), accentCyan, 200, 30));
            Controls.Add(CreateCenteredLabel("Safety Technology Team, Palestine", new Font("Segoe UI", 12, FontStyle.Bold), textGray, 240, 25));
            Controls.Add(CreateCenteredLabel("Amin N. Direya", new Font("Segoe UI", 17, FontStyle.Bold), textWhite, 275, 35));
            Controls.Add(CreateCenteredLabel("Hamza H. Jaber", new Font("Segoe UI", 17, FontStyle.Bold), textWhite, 315, 35));
            Controls.Add(CreateCenteredLabel("For helping bring this project to life.", new Font("Segoe UI", 11, FontStyle.Regular), textLightGray, 360, 25));

            this.Click += CloseForm_Trigger;
            foreach (Control c in this.Controls)
            {
                c.Click += CloseForm_Trigger;
            }
        }

        private void CloseForm_Trigger(object sender, EventArgs e)
        {
            this.Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape || keyData == Keys.Enter)
            {
                this.Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private Label CreateCenteredLabel(string text, Font font, Color color, int y, int height)
        {
            Label lbl = new Label();
            lbl.AutoSize = false;
            lbl.Text = text;
            lbl.Font = font;
            lbl.ForeColor = color;
            lbl.BackColor = Color.Transparent;
            lbl.TextAlign = ContentAlignment.MiddleCenter;
            lbl.SetBounds(0, y, this.Width, height);
            return lbl;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (logoBitmap != null)
            {
                Rectangle iconRect = new Rectangle((Width - 48) / 2, 30, 48, 48);
                g.DrawImage(logoBitmap, iconRect);
            }

            using (Pen sepPen = new Pen(AppVisualTheme.Current.Border))
            {
                g.DrawLine(sepPen, 120, 190, Width - 120, 190);
            }
        }

        private static Bitmap LoadApplicationIconBitmap()
        {
            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                {
                    return icon == null ? null : icon.ToBitmap();
                }
            }
            catch
            {
                return null;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (logoBitmap != null)
            {
                logoBitmap.Dispose();
                logoBitmap = null;
            }

            base.OnFormClosed(e);
        }
    }
}
