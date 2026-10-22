using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UniversalScanner
{
    public class SplashForm : Form
    {
        private Label titleLabel;
        private Label subtitleLabel;
        private Label statusLabel;
        private Label versionLabel;
        private Label greetingLabel;
        private Timer animationTimer;
        private Bitmap logoBitmap;
        private int animationOffset = 0;
        private string currentStatus = "Loading...";
        private readonly AppVisualPalette palette = AppVisualTheme.Current;
        private readonly Color navy = AppVisualTheme.Current.Header;
        private readonly Color accentBlue = AppVisualTheme.Current.Accent;
        private readonly Color accentGreen = Color.FromArgb(63, 168, 112);

        public SplashForm()
        {
            Width = 580;
            Height = 320;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = palette.WindowBackground;
            Opacity = 1.0;
            TopMost = true;
            DoubleBuffered = true;
            logoBitmap = LoadApplicationIconBitmap();

            titleLabel = new Label();
            titleLabel.Text = "Universal Scanner";
            titleLabel.Font = new Font("Segoe UI", 20, FontStyle.Bold);
            titleLabel.ForeColor = Color.White;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.SetBounds(92, 28, 360, 38);

            subtitleLabel = new Label();
            subtitleLabel.Text = "Discover - Analyze - Identify";
            subtitleLabel.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            subtitleLabel.ForeColor = palette.HeaderSecondaryText;
            subtitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            subtitleLabel.BackColor = Color.Transparent;
            subtitleLabel.SetBounds(94, 62, 360, 20);

            Label authorLogoLabel = new Label();
            authorLogoLabel.Text = "Anan Awwad";
            authorLogoLabel.Font = new Font("Segoe UI", 9, FontStyle.Regular);
            authorLogoLabel.ForeColor = palette.HeaderSecondaryText;
            authorLogoLabel.TextAlign = ContentAlignment.MiddleLeft;
            authorLogoLabel.BackColor = Color.Transparent;
            authorLogoLabel.SetBounds(94, 82, 360, 20);

            statusLabel = new Label();
            statusLabel.Text = currentStatus;
            statusLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Regular);
            statusLabel.ForeColor = palette.TextPrimary;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.BackColor = Color.Transparent;
            statusLabel.SetBounds(52, 186, 476, 30);

            versionLabel = new Label();
            versionLabel.Text = "Version 2.4";
            versionLabel.Font = new Font("Segoe UI", 9, FontStyle.Regular);
            versionLabel.ForeColor = palette.TextSecondary;
            versionLabel.TextAlign = ContentAlignment.MiddleLeft;
            versionLabel.BackColor = Color.Transparent;
            versionLabel.SetBounds(52, 266, 200, 25);

            greetingLabel = new Label();
            greetingLabel.Text = "Greeting from Anan Awwad";
            greetingLabel.Font = new Font("Segoe UI", 9, FontStyle.Italic);
            greetingLabel.ForeColor = palette.TextSecondary;
            greetingLabel.TextAlign = ContentAlignment.MiddleRight;
            greetingLabel.BackColor = Color.Transparent;
            greetingLabel.SetBounds(278, 266, 250, 25);

            Controls.Add(titleLabel);
            Controls.Add(subtitleLabel);
            Controls.Add(authorLogoLabel);
            Controls.Add(statusLabel);
            Controls.Add(versionLabel);
            Controls.Add(greetingLabel);

            animationTimer = new Timer();
            animationTimer.Interval = 25;
            animationTimer.Tick += (s, e) =>
            {
                animationOffset += 8;
                if (animationOffset > 470)
                    animationOffset = -220;

                Invalidate();
            };
            animationTimer.Start();
        }

        public void SetStatus(string text)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => SetStatus(text)));
                return;
            }

            currentStatus = text;
            statusLabel.Text = text;
            Refresh();
        }

        public void SetStatus(string text, int progress)
        {
            SetStatus(text);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            Rectangle borderRect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (Pen borderPen = new Pen(palette.BorderStrong))
            {
                g.DrawRectangle(borderPen, borderRect);
            }

            Rectangle headerRect = new Rectangle(0, 0, Width, 112);
            using (SolidBrush headerBrush = new SolidBrush(navy))
            {
                g.FillRectangle(headerBrush, headerRect);
            }

            using (Pen accentPen = new Pen(accentBlue, 3))
            {
                g.DrawLine(accentPen, 0, 111, Width, 111);
            }

            Rectangle iconBackRect = new Rectangle(28, 29, 46, 46);
            using (SolidBrush iconBackBrush = new SolidBrush(palette.AccentPressed))
            {
                g.FillEllipse(iconBackBrush, iconBackRect);
            }

            Rectangle iconRect = new Rectangle(33, 34, 36, 36);
            if (logoBitmap != null)
            {
                g.DrawImage(logoBitmap, iconRect);
            }

            Rectangle bodyPanel = new Rectangle(40, 146, Width - 80, 102);
            using (GraphicsPath bodyPath = RoundedRect(bodyPanel, 8))
            using (SolidBrush bodyBrush = new SolidBrush(palette.Surface))
            using (Pen bodyPen = new Pen(palette.Border))
            {
                g.FillPath(bodyBrush, bodyPath);
                g.DrawPath(bodyPen, bodyPath);
            }

            Rectangle smallRadar = new Rectangle(478, 164, 32, 32);
            using (SolidBrush radarBackBrush = new SolidBrush(palette.SurfaceMuted))
            using (Pen radarPen = new Pen(accentGreen, 2))
            {
                g.FillEllipse(radarBackBrush, smallRadar);
                g.DrawEllipse(radarPen, smallRadar);
                g.DrawLine(radarPen, smallRadar.Left + 16, smallRadar.Top + 16, smallRadar.Right - 7, smallRadar.Top + 9);
                using (SolidBrush centerBrush = new SolidBrush(palette.SelectionText))
                    g.FillEllipse(centerBrush, smallRadar.Left + 13, smallRadar.Top + 13, 6, 6);
            }

            Rectangle barBack = new Rectangle(52, 224, 476, 10);
            using (GraphicsPath backPath = RoundedRect(barBack, 5))
            using (SolidBrush backBrush = new SolidBrush(palette.SurfacePressed))
            {
                g.FillPath(backBrush, backPath);
            }

            Rectangle movingBar = new Rectangle(52 + animationOffset, 224, 220, 10);
            using (GraphicsPath clipPath = RoundedRect(barBack, 5))
            {
                g.SetClip(clipPath);

                using (LinearGradientBrush barBrush = new LinearGradientBrush(
                    movingBar,
                    accentBlue,
                    accentGreen,
                    LinearGradientMode.Horizontal))
                {
                    g.FillRectangle(barBrush, movingBar);
                }

                g.ResetClip();
            }
        }

        private GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
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
            if (animationTimer != null)
            {
                animationTimer.Stop();
                animationTimer.Dispose();
            }

            if (logoBitmap != null)
            {
                logoBitmap.Dispose();
                logoBitmap = null;
            }

            base.OnFormClosed(e);
        }
    }
}
