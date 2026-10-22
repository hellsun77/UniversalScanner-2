using System;
using System.Drawing;
using System.Windows.Forms;
using UniversalScanner.DeviceSDK.Hikvision;

namespace UniversalScanner.Forms
{
    public class HikvisionOnvifProvisioningForm : Form
    {
        private string _ip;
        private TextBox txtIp;
        private TextBox txtOnvifUser;
        private TextBox txtOnvifPass;
        private TextBox txtStatus;
        private Button btnProvision;
        private Button btnCancel;
        private CheckBox chkShowPassword;

        public HikvisionOnvifProvisioningForm(string ip)
        {
            _ip = ip;
            InitializeComponent();
            UniversalScanner.AppVisualTheme.Apply(this);
        }

        private void InitializeComponent()
        {
            this.Text = "Enable Hikvision ONVIF";
            this.Size = new Size(400, 320);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            Label lblIp = new Label { Text = "Camera IP:", Location = new Point(15, 20), AutoSize = true };
            txtIp = new TextBox { Text = _ip, Location = new Point(120, 15), Width = 200, ReadOnly = true };

            Label lblUser = new Label { Text = "ONVIF User:", Location = new Point(15, 50), AutoSize = true };
            txtOnvifUser = new TextBox { Location = new Point(120, 45), Width = 200, Text = "admin" };

            Label lblPass = new Label { Text = "ONVIF Pass:", Location = new Point(15, 80), AutoSize = true };
            txtOnvifPass = new TextBox { Location = new Point(120, 75), Width = 200, UseSystemPasswordChar = true };

            chkShowPassword = new CheckBox { Text = "Show password", Location = new Point(120, 100), AutoSize = true };
            chkShowPassword.CheckedChanged += (s, ev) => 
            {
                txtOnvifPass.UseSystemPasswordChar = !chkShowPassword.Checked;
            };

            Label lblStatus = new Label { Text = "Status:", Location = new Point(15, 130), AutoSize = true };
            txtStatus = new TextBox { Location = new Point(120, 125), Width = 240, Height = 80, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };

            btnProvision = new Button { Text = "Apply", Location = new Point(200, 220), Width = 80 };
            btnProvision.Click += BtnProvision_Click;

            btnCancel = new Button { Text = "Cancel", Location = new Point(290, 220), Width = 80 };
            btnCancel.Click += (s, e) => this.Close();

            this.Controls.Add(lblIp);
            this.Controls.Add(txtIp);
            this.Controls.Add(lblUser);
            this.Controls.Add(txtOnvifUser);
            this.Controls.Add(lblPass);
            this.Controls.Add(txtOnvifPass);
            this.Controls.Add(chkShowPassword);
            this.Controls.Add(lblStatus);
            this.Controls.Add(txtStatus);
            this.Controls.Add(btnProvision);
            this.Controls.Add(btnCancel);
        }

        private async void BtnProvision_Click(object sender, EventArgs e)
        {
            string onvifUser = txtOnvifUser.Text.Trim();
            string onvifPass = txtOnvifPass.Text;

            if (string.IsNullOrEmpty(onvifUser) || string.IsNullOrEmpty(onvifPass))
            {
                MessageBox.Show(this, "Username and password cannot be empty.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string adminUser, adminPass;
            UniversalLoginManager.GetUniversalCredentials(out adminUser, out adminPass);

            if (string.IsNullOrEmpty(adminUser) || string.IsNullOrEmpty(adminPass))
            {
                // Fallback to manual dialog
                using (var loginForm = new UniversalLoginForm())
                {
                    if (loginForm.ShowDialog(this) == DialogResult.OK)
                    {
                        UniversalLoginManager.GetUniversalCredentials(out adminUser, out adminPass);
                    }
                    else
                    {
                        return; // User cancelled
                    }
                }
            }

            btnProvision.Enabled = false;
            btnProvision.Text = "Applying...";
            txtStatus.Text = "";

            try
            {
                var progress = new Progress<string>(msg => 
                {
                    txtStatus.AppendText(msg);
                });

                var result = await HikvisionOnvifManager.ProvisionAsync(_ip, adminUser, adminPass, onvifUser, onvifPass, progress);

                if (result.Success)
                {
                    MessageBox.Show(this, "ONVIF Provisioning Successful.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (result.IsPartialSuccess)
                {
                    MessageBox.Show(this, "Partial Success: " + result.ErrorMessage, "Partial Success", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(this, $"Failed at {result.Stage}:\n{result.ErrorMessage}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                txtStatus.AppendText($"\r\nException: {ex.Message}\r\n");
                MessageBox.Show(this, $"An error occurred:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnProvision.Enabled = true;
                btnProvision.Text = "Apply";
            }
        }
    }
}
