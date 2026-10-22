using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using UniversalScanner.DeviceSDK.Common;

namespace UniversalScanner
{
    public class UniversalLoginForm : Form
    {
        private TextBox usernameBox;
        private TextBox passwordBox;
        private TextBox testIpBox;
        private CheckBox showPasswordBox;
        private Label statusLabel;
        private Button saveButton;
        private Button clearButton;
        private Button testButton;
        private Button closeButton;

        public UniversalLoginForm()
        {
            Text = "Universal Login";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(500, 290);

            BuildUi();
            LoadCredentials();
            AppVisualTheme.Apply(this);
        }

        private void BuildUi()
        {
            Color navy = Color.FromArgb(28, 61, 108);
            Color blue = Color.FromArgb(0, 116, 181);

            Label title = new Label
            {
                Text = "Universal camera login",
                Left = 20,
                Top = 18,
                Width = 440,
                Height = 26,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = navy
            };

            Label description = new Label
            {
                Text = "Used automatically for firmware and release-date retrieval when no device-specific login is known.",
                Left = 20,
                Top = 48,
                Width = 450,
                Height = 36,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(70, 80, 95)
            };

            AddLabel("Username:", 20, 98);
            usernameBox = AddBox(145, 94, false);

            AddLabel("Password:", 20, 132);
            passwordBox = AddBox(145, 128, false);
            passwordBox.UseSystemPasswordChar = true;

            showPasswordBox = new CheckBox
            {
                Text = "Show password",
                Left = 145,
                Top = 158,
                Width = 160
            };
            showPasswordBox.CheckedChanged += (s, e) => passwordBox.UseSystemPasswordChar = !showPasswordBox.Checked;

            AddLabel("Test IP:", 20, 194);
            testIpBox = AddBox(145, 190, false);

            statusLabel = new Label
            {
                Text = "Ready.",
                Left = 20,
                Top = 225,
                Width = 450,
                Height = 22,
                ForeColor = Color.FromArgb(70, 80, 95)
            };

            saveButton = new Button { Text = "Save Credentials", Left = 20, Top = 252, Width = 120, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            clearButton = new Button { Text = "Remove Saved", Left = 150, Top = 252, Width = 95, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            testButton = new Button { Text = "Test", Left = 255, Top = 252, Width = 55, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            closeButton = new Button { Text = "Close", Left = 320, Top = 252, Width = 60, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };

            saveButton.BackColor = blue;
            saveButton.ForeColor = Color.White;
            saveButton.FlatStyle = FlatStyle.Flat;
            saveButton.FlatAppearance.BorderSize = 0;

            saveButton.Click += SaveButton_Click;
            clearButton.Click += ClearButton_Click;
            testButton.Click += TestButton_Click;
            closeButton.Click += (s, e) => Close();

            Controls.Add(title);
            Controls.Add(description);
            Controls.Add(showPasswordBox);
            Controls.Add(statusLabel);
            Controls.Add(saveButton);
            Controls.Add(clearButton);
            Controls.Add(testButton);
            Controls.Add(closeButton);

            AcceptButton = saveButton;
            CancelButton = closeButton;
        }

        private void LoadCredentials()
        {
            string username;
            string password;
            UniversalLoginManager.GetUniversalCredentials(out username, out password);
            usernameBox.Text = username;
            passwordBox.Text = password;
            statusLabel.Text = UniversalLoginManager.HasUniversalCredentials()
                ? "Universal credentials are saved."
                : "No universal credentials saved.";
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(usernameBox.Text) || string.IsNullOrWhiteSpace(passwordBox.Text))
            {
                MessageBox.Show(this, "Username and password are required.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            UniversalLoginManager.SetUniversalCredentials(usernameBox.Text, passwordBox.Text);
            statusLabel.Text = "Universal credentials saved. Rescan to retrieve firmware.";
            MessageBox.Show(this, "Universal credentials saved.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(this,
                "Are you sure you want to remove the saved Universal Login credentials?",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (result != DialogResult.Yes)
                return;

            UniversalLoginManager.ClearUniversalCredentials();
            usernameBox.Text = "";
            passwordBox.Text = "";
            statusLabel.Text = "Universal Login credentials have been removed.";
        }

        private async void TestButton_Click(object sender, EventArgs e)
        {
            string ip = testIpBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.Show(this, "Enter a device IP address to test.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(usernameBox.Text) || string.IsNullOrWhiteSpace(passwordBox.Text))
            {
                MessageBox.Show(this, "Username and password are required for the test.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true, "Testing firmware lookup...");
            try
            {
                DeviceFirmwareProbe.FirmwareResult firmware = await Task.Run(() =>
                    DeviceFirmwareProbe.GetFirmwareAsync(ip, "", usernameBox.Text.Trim(), passwordBox.Text));

                if (firmware != null && !string.IsNullOrWhiteSpace(firmware.FirmwareVersion))
                {
                    statusLabel.Text = "Test succeeded: " + firmware.FirmwareVersion;
                    MessageBox.Show(this,
                        "Firmware: " + firmware.FirmwareVersion +
                        (string.IsNullOrWhiteSpace(firmware.ReleaseDate) ? "" : "\r\nRelease Date: " + firmware.ReleaseDate),
                        Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    statusLabel.Text = "Test completed, but no firmware was returned.";
                    MessageBox.Show(this, "The device answered, but no firmware was returned with these credentials.", Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Test failed.";
                MessageBox.Show(this, "Firmware test failed.\r\n\r\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, statusLabel.Text);
            }
        }

        private void SetBusy(bool busy, string status)
        {
            saveButton.Enabled = !busy;
            clearButton.Enabled = !busy;
            testButton.Enabled = !busy;
            closeButton.Enabled = !busy;
            statusLabel.Text = status;
            Application.DoEvents();
        }

        private void AddLabel(string text, int left, int top)
        {
            Controls.Add(new Label
            {
                Text = text,
                Left = left,
                Top = top,
                Width = 115,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft
            });
        }

        private TextBox AddBox(int left, int top, bool readOnly)
        {
            TextBox box = new TextBox
            {
                Left = left,
                Top = top,
                Width = 320,
                ReadOnly = readOnly
            };
            Controls.Add(box);
            return box;
        }
    }
}
