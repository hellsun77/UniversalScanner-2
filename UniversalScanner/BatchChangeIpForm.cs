using System;
using System.Drawing;
using System.Net;
using System.Windows.Forms;

namespace UniversalScanner
{
    public class BatchChangeIpForm : Form
    {
        private TextBox startIpBox;
        private TextBox subnetMaskBox;
        private TextBox gatewayBox;
        private TextBox usernameBox;
        private TextBox passwordBox;
        private CheckBox showPasswordBox;
        private Label countLabel;

        public string StartIPAddress => startIpBox.Text.Trim();
        public string SubnetMask => subnetMaskBox.Text.Trim();
        public string Gateway => gatewayBox.Text.Trim();
        public string Username => usernameBox.Text.Trim();
        public string Password => passwordBox.Text;

        public BatchChangeIpForm(int selectedCount, string defaultUsername = "admin")
        {
            Text = "Batch Change IP";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(460, 310);
            BuildUi(selectedCount, defaultUsername);
            AppVisualTheme.Apply(this);
        }

        private void BuildUi(int selectedCount, string defaultUsername)
        {
            int leftLabel = 20;
            int leftBox = 165;
            int top = 20;
            int gap = 36;

            countLabel = new Label();
            countLabel.Left = leftLabel;
            countLabel.Top = top;
            countLabel.Width = 420;
            countLabel.Text = "Selected devices: " + selectedCount;
            Controls.Add(countLabel);

            top += gap;
            AddLabel("Start IP Address:", leftLabel, top);
            startIpBox = AddBox(leftBox, top, false);
            startIpBox.Text = "192.168.1.200";
            startIpBox.TextChanged += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(startIpBox.Text))
                    gatewayBox.Text = SuggestGateway(startIpBox.Text.Trim());
            };

            top += gap;
            AddLabel("Subnet Mask:", leftLabel, top);
            subnetMaskBox = AddBox(leftBox, top, false);
            subnetMaskBox.Text = "255.255.255.0";

            top += gap;
            AddLabel("Gateway:", leftLabel, top);
            gatewayBox = AddBox(leftBox, top, false);
            gatewayBox.Text = "192.168.1.1";

            top += gap;
            AddLabel("Username:", leftLabel, top);
            usernameBox = AddBox(leftBox, top, false);
            usernameBox.Text = string.IsNullOrWhiteSpace(defaultUsername) ? "admin" : defaultUsername;

            top += gap;
            AddLabel("Admin Password:", leftLabel, top);
            passwordBox = AddBox(leftBox, top, false);
            passwordBox.UseSystemPasswordChar = true;
            ApplyUniversalLoginIfAvailable(preserveDefaultUsername: string.Equals(usernameBox.Text, "admin", StringComparison.OrdinalIgnoreCase));

            showPasswordBox = new CheckBox();
            showPasswordBox.Left = leftBox;
            showPasswordBox.Top = top + 30;
            showPasswordBox.Width = 180;
            showPasswordBox.Text = "Show password";
            showPasswordBox.CheckedChanged += (s, e) => passwordBox.UseSystemPasswordChar = !showPasswordBox.Checked;
            Controls.Add(showPasswordBox);

            Button okButton = new Button();
            okButton.Text = "Start";
            okButton.Left = 285;
            okButton.Top = 260;
            okButton.Width = 75;
            okButton.Click += (s, e) =>
            {
                if (!ValidateInputs())
                    return;

                DialogResult = DialogResult.OK;
                Close();
            };

            Button cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Left = 370;
            cancelButton.Top = 260;
            cancelButton.Width = 75;
            cancelButton.DialogResult = DialogResult.Cancel;

            Controls.Add(okButton);
            Controls.Add(cancelButton);
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void ApplyUniversalLoginIfAvailable(bool preserveDefaultUsername = false)
        {
            if (!UniversalLoginManager.HasUniversalCredentials())
                return;

            UniversalLoginManager.GetUniversalCredentials(out string username, out string password);
            if (!preserveDefaultUsername && !string.IsNullOrWhiteSpace(username))
                usernameBox.Text = username.Trim();
            if (!string.IsNullOrWhiteSpace(password))
                passwordBox.Text = password;
        }

        private bool ValidateInputs()
        {
            IPAddress address;

            if (!IPAddress.TryParse(StartIPAddress, out address))
            {
                MessageBox.Show(this, "Start IP address is not valid.", "Batch Change IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!IPAddress.TryParse(SubnetMask, out address))
            {
                MessageBox.Show(this, "Subnet mask is not valid.", "Batch Change IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!IPAddress.TryParse(Gateway, out address))
            {
                MessageBox.Show(this, "Gateway is not valid.", "Batch Change IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(Username))
            {
                MessageBox.Show(this, "Username is required.", "Batch Change IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                MessageBox.Show(this, "Admin password is required.", "Batch Change IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private static string SuggestGateway(string ip)
        {
            string[] parts = ip.Split('.');
            if (parts.Length != 4)
                return "192.168.1.1";
            return parts[0] + "." + parts[1] + "." + parts[2] + ".1";
        }

        private void AddLabel(string text, int left, int top)
        {
            Label label = new Label();
            label.Text = text;
            label.Left = left;
            label.Top = top + 4;
            label.Width = 135;
            Controls.Add(label);
        }

        private TextBox AddBox(int left, int top, bool readOnly)
        {
            TextBox box = new TextBox();
            box.Left = left;
            box.Top = top;
            box.Width = 260;
            box.ReadOnly = readOnly;
            Controls.Add(box);
            return box;
        }
    }
}
