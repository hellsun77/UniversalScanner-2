using System.Drawing;
using System.Windows.Forms;

namespace UniversalScanner
{
    public class BatchActivationForm : Form
    {
        private readonly bool showUsernameField;
        private TextBox usernameBox;
        private TextBox passwordBox;
        private TextBox confirmBox;
        private CheckBox showPasswordBox;

        public string Username => usernameBox == null ? "" : usernameBox.Text.Trim();
        public string Password => passwordBox.Text;

        public BatchActivationForm(int selectedCount, bool showUsernameField = false)
        {
            this.showUsernameField = showUsernameField;
            Text = "Batch Activate Selected Devices";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(440, showUsernameField ? 245 : 210);
            BuildUi(selectedCount);
        }

        private void BuildUi(int selectedCount)
        {
            Label countLabel = new Label();
            countLabel.Left = 20;
            countLabel.Top = 20;
            countLabel.Width = 390;
            countLabel.Text = "Selected devices: " + selectedCount;
            Controls.Add(countLabel);

            int passwordTop = showUsernameField ? 95 : 65;
            int confirmTop = showUsernameField ? 130 : 100;
            int showPasswordTop = showUsernameField ? 158 : 128;
            int buttonTop = showUsernameField ? 200 : 165;

            if (showUsernameField)
            {
                Label usernameLabel = new Label();
                usernameLabel.Left = 20;
                usernameLabel.Top = 65;
                usernameLabel.Width = 130;
                usernameLabel.Text = "Username:";
                Controls.Add(usernameLabel);

                usernameBox = new TextBox();
                usernameBox.Left = 160;
                usernameBox.Top = 60;
                usernameBox.Width = 240;
                usernameBox.Text = "root";
                Controls.Add(usernameBox);
            }

            Label passLabel = new Label();
            passLabel.Left = 20;
            passLabel.Top = passwordTop;
            passLabel.Width = 130;
            passLabel.Text = "New Password:";
            Controls.Add(passLabel);

            passwordBox = new TextBox();
            passwordBox.Left = 160;
            passwordBox.Top = passwordTop - 5;
            passwordBox.Width = 240;
            passwordBox.UseSystemPasswordChar = true;
            Controls.Add(passwordBox);

            Label confirmLabel = new Label();
            confirmLabel.Left = 20;
            confirmLabel.Top = confirmTop;
            confirmLabel.Width = 130;
            confirmLabel.Text = "Confirm Password:";
            Controls.Add(confirmLabel);

            confirmBox = new TextBox();
            confirmBox.Left = 160;
            confirmBox.Top = confirmTop - 5;
            confirmBox.Width = 240;
            confirmBox.UseSystemPasswordChar = true;
            Controls.Add(confirmBox);
            ApplyUniversalLoginIfAvailable();

            showPasswordBox = new CheckBox();
            showPasswordBox.Left = 160;
            showPasswordBox.Top = showPasswordTop;
            showPasswordBox.Width = 180;
            showPasswordBox.Text = "Show password";
            showPasswordBox.CheckedChanged += (s, e) =>
            {
                passwordBox.UseSystemPasswordChar = !showPasswordBox.Checked;
                confirmBox.UseSystemPasswordChar = !showPasswordBox.Checked;
            };
            Controls.Add(showPasswordBox);

            Button okButton = new Button();
            okButton.Text = "Activate";
            okButton.Left = 265;
            okButton.Top = buttonTop;
            okButton.Width = 75;
            okButton.Click += (s, e) =>
            {
                if (showUsernameField && string.IsNullOrWhiteSpace(usernameBox.Text))
                {
                    MessageBox.Show(this, "Username is required.", "Batch Activate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(passwordBox.Text))
                {
                    MessageBox.Show(this, "Password is required.", "Batch Activate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (passwordBox.Text != confirmBox.Text)
                {
                    MessageBox.Show(this, "Passwords do not match.", "Batch Activate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
            };

            Button cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Left = 350;
            cancelButton.Top = buttonTop;
            cancelButton.Width = 75;
            cancelButton.DialogResult = DialogResult.Cancel;

            Controls.Add(okButton);
            Controls.Add(cancelButton);
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void ApplyUniversalLoginIfAvailable()
        {
            if (!UniversalLoginManager.HasUniversalCredentials())
                return;

            UniversalLoginManager.GetUniversalCredentials(out string username, out string password);
            if (usernameBox != null && !string.IsNullOrWhiteSpace(username))
                usernameBox.Text = username.Trim();

            if (!string.IsNullOrWhiteSpace(password))
            {
                passwordBox.Text = password;
                confirmBox.Text = password;
            }
        }
    }
}
