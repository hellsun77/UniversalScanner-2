using System;
using System.Drawing;
using System.Windows.Forms;

namespace UniversalScanner
{
    public class AxisMotionAppForm : Form
    {
        private readonly TextBox usernameBox;
        private readonly TextBox passwordBox;
        private readonly CheckBox showPasswordBox;

        public string Username { get { return usernameBox.Text.Trim(); } }
        public string Password { get { return passwordBox.Text; } }

        public AxisMotionAppForm(string ipAddress)
            : this(ipAddress, "root", "")
        {
        }

        public AxisMotionAppForm(string ipAddress, string username, string password)
        {
            Text = "Start Axis Motion Detection App";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(430, 210);

            AddLabel("Axis IP:", 20, 24);
            var ipBox = new TextBox
            {
                Left = 150,
                Top = 20,
                Width = 240,
                Text = ipAddress ?? "",
                ReadOnly = true
            };
            Controls.Add(ipBox);

            AddLabel("Username:", 20, 64);
            usernameBox = new TextBox
            {
                Left = 150,
                Top = 60,
                Width = 240,
                Text = string.IsNullOrWhiteSpace(username) ? "root" : username
            };
            Controls.Add(usernameBox);

            AddLabel("Password:", 20, 104);
            passwordBox = new TextBox
            {
                Left = 150,
                Top = 100,
                Width = 240,
                Text = password ?? "",
                UseSystemPasswordChar = true
            };
            Controls.Add(passwordBox);

            showPasswordBox = new CheckBox
            {
                Left = 150,
                Top = 130,
                Width = 160,
                Text = "Show password"
            };
            showPasswordBox.CheckedChanged += (s, e) =>
            {
                passwordBox.UseSystemPasswordChar = !showPasswordBox.Checked;
            };
            Controls.Add(showPasswordBox);

            var startButton = new Button
            {
                Text = "Start App",
                Left = 210,
                Top = 165,
                Width = 85,
                DialogResult = DialogResult.OK
            };
            startButton.Click += startButton_Click;
            Controls.Add(startButton);

            var cancelButton = new Button
            {
                Text = "Cancel",
                Left = 305,
                Top = 165,
                Width = 85,
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(cancelButton);

            AcceptButton = startButton;
            CancelButton = cancelButton;
        }

        private void startButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                MessageBox.Show(this, "Username is required.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                MessageBox.Show(this, "Password is required.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
        }

        private void AddLabel(string text, int left, int top)
        {
            Controls.Add(new Label
            {
                Text = text,
                Left = left,
                Top = top + 4,
                Width = 120,
                AutoSize = false
            });
        }
    }
}
