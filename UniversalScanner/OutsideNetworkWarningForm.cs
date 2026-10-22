using System;
using System.Drawing;
using System.Windows.Forms;

namespace UniversalScanner
{
    public class OutsideNetworkWarningForm : Form
    {
        private Button useAcrossSubnetButton;
        private Button continueAnywayButton;
        private Button cancelButton;
        private Label messageLabel;
        private Label recommendedLabel;
        private PictureBox iconBox;

        public OutsideNetworkWarningForm()
        {
            Text = "IP Address Outside Local Network";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(500, 240);
            BackColor = Color.White;

            BuildUi();
            AppVisualTheme.Apply(this);
        }

        private void BuildUi()
        {
            iconBox = new PictureBox
            {
                Image = SystemIcons.Warning.ToBitmap(),
                Location = new Point(20, 25),
                Size = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.StretchImage
            };

            messageLabel = new Label
            {
                Text = "The target IP address is outside all networks currently assigned to this PC.\n\nThe camera may be changed successfully, but after the change it may not be reachable from this PC.",
                Location = new Point(70, 25),
                Size = new Size(400, 60),
                Font = new Font("Segoe UI", 9F)
            };

            recommendedLabel = new Label
            {
                Text = "Recommended: Use \"Change IP Across Subnet\" so Universal Scanner can add a temporary subnet and verify the camera after the change.",
                Location = new Point(70, 95),
                Size = new Size(400, 60),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.DarkBlue
            };

            useAcrossSubnetButton = new Button
            {
                Text = "Use Change IP Across Subnet",
                Location = new Point(70, 175),
                Size = new Size(200, 35),
                DialogResult = DialogResult.Retry,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            useAcrossSubnetButton.FlatAppearance.BorderSize = 0;

            continueAnywayButton = new Button
            {
                Text = "Continue Anyway",
                Location = new Point(280, 175),
                Size = new Size(110, 35),
                DialogResult = DialogResult.Yes,
                Font = new Font("Segoe UI", 9F)
            };

            cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(400, 175),
                Size = new Size(80, 35),
                DialogResult = DialogResult.Cancel,
                Font = new Font("Segoe UI", 9F)
            };

            Controls.Add(iconBox);
            Controls.Add(messageLabel);
            Controls.Add(recommendedLabel);
            Controls.Add(useAcrossSubnetButton);
            Controls.Add(continueAnywayButton);
            Controls.Add(cancelButton);

            AcceptButton = useAcrossSubnetButton;
            CancelButton = cancelButton;
        }
    }
}
