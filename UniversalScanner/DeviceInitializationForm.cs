using System;
using System.Drawing;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using UniversalScanner.DeviceSDK.Dahua;
using UniversalScanner.DeviceSDK.Hikvision;
using UniversalScanner.DeviceSDK.Sunell;
using UniversalScanner.DeviceSDK.Tiandy;
using UniversalScanner.DeviceSDK.Uniview;
using UniversalScanner.DeviceSDK.Axis;

namespace UniversalScanner
{
    public class DeviceInitializationForm : Form
    {
        private TextBox brandBox;
        private TextBox ipBox;
        private TextBox modelBox;
        private TextBox activationBox;
        private TextBox usernameBox;
        private TextBox passwordBox;
        private TextBox confirmBox;
        private CheckBox showPasswordBox;
        private Label statusLabel;
        private Button initializeButton;
        private Button cancelButton;

        public string DeviceIP => ipBox.Text.Trim();
        public string DeviceBrand => brandBox.Text.Trim();
        public string Username => usernameBox.Text.Trim();
        public string Password => passwordBox.Text;
        public string ActivationStatus => activationBox.Text.Trim();
        public string UpdatedModel => modelBox.Text.Trim();
        public string UpdatedUniqueId { get; private set; }
        public bool ActivationAttempted { get; private set; }

        public DeviceInitializationForm(string brand, string ip, string model, string activation)
        {
            Text = "Initialize / Activate Device";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(460, 420);

            BuildUi();

            brandBox.Text = brand;
            ipBox.Text = ip;
            modelBox.Text = model;
            activationBox.Text = string.IsNullOrWhiteSpace(activation) ? "Unknown" : activation;
            usernameBox.Text = "admin";

            if ((brand ?? "").IndexOf("Bosch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (model ?? "").IndexOf("Bosch", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                usernameBox.Text = "service";
            }

            if ((brand ?? "").IndexOf("Axis", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (model ?? "").IndexOf("Axis", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                usernameBox.Text = "root";
            }

            if ((brand ?? "").IndexOf("Sunell", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (model ?? "").IndexOf("Sunell", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                passwordBox.MaxLength = 20;
                confirmBox.MaxLength = 20;
            }

            if ((brand ?? "").IndexOf("Tiandy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (model ?? "").IndexOf("Tiandy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                passwordBox.MaxLength = 15;
                confirmBox.MaxLength = 15;
            }

            ApplyUniversalLoginIfAvailable();
            AppVisualTheme.Apply(this);
        }

        private void ApplyUniversalLoginIfAvailable()
        {
            if (!UniversalLoginManager.HasUniversalCredentials())
                return;

            UniversalLoginManager.GetUniversalCredentials(out string username, out string password);
            if (!string.IsNullOrWhiteSpace(username))
                usernameBox.Text = username.Trim();
            if (!string.IsNullOrWhiteSpace(password))
            {
                passwordBox.Text = password;
                confirmBox.Text = password;
            }
        }

        private void BuildUi()
        {
            int leftLabel = 20;
            int leftBox = 155;
            int top = 20;
            int gap = 36;

            AddLabel("Brand:", leftLabel, top);
            brandBox = AddBox(leftBox, top, true);

            top += gap;
            AddLabel("IP Address:", leftLabel, top);
            ipBox = AddBox(leftBox, top, false);

            top += gap;
            AddLabel("Model:", leftLabel, top);
            modelBox = AddBox(leftBox, top, true);

            top += gap;
            AddLabel("Activation:", leftLabel, top);
            activationBox = AddBox(leftBox, top, true);

            top += gap;
            AddLabel("Username:", leftLabel, top);
            usernameBox = AddBox(leftBox, top, false);

            top += gap;
            AddLabel("New Password:", leftLabel, top);
            passwordBox = AddBox(leftBox, top, false);
            passwordBox.UseSystemPasswordChar = true;

            top += gap;
            AddLabel("Confirm Password:", leftLabel, top);
            confirmBox = AddBox(leftBox, top, false);
            confirmBox.UseSystemPasswordChar = true;

            showPasswordBox = new CheckBox();
            showPasswordBox.Text = "Show password";
            showPasswordBox.Left = leftBox;
            showPasswordBox.Top = top + 34;
            showPasswordBox.Width = 180;
            showPasswordBox.CheckedChanged += (s, e) =>
            {
                bool show = showPasswordBox.Checked;
                passwordBox.UseSystemPasswordChar = !show;
                confirmBox.UseSystemPasswordChar = !show;
            };

            statusLabel = new Label();
            statusLabel.Left = 20;
            statusLabel.Top = 315;
            statusLabel.Width = 420;
            statusLabel.Height = 40;
            statusLabel.Text = "Ready.";

            initializeButton = new Button();
            initializeButton.Text = "Initialize";
            initializeButton.Left = 260;
            initializeButton.Top = 370;
            initializeButton.Width = 85;
            initializeButton.Click += InitializeButton_Click;

            cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Left = 355;
            cancelButton.Top = 370;
            cancelButton.Width = 85;
            cancelButton.DialogResult = DialogResult.Cancel;

            Controls.Add(showPasswordBox);
            Controls.Add(statusLabel);
            Controls.Add(initializeButton);
            Controls.Add(cancelButton);

            AcceptButton = initializeButton;
            CancelButton = cancelButton;
        }

        private async void InitializeButton_Click(object sender, EventArgs e)
        {
            IPAddress parsedIp;

            if (string.IsNullOrWhiteSpace(ipBox.Text) ||
                !IPAddress.TryParse(ipBox.Text.Trim(), out parsedIp))
            {
                MessageBox.Show(this, "Please enter a valid IP address.", "Device Initialization",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(passwordBox.Text))
            {
                MessageBox.Show(this, "Password is required.", "Device Initialization",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (passwordBox.Text != confirmBox.Text)
            {
                MessageBox.Show(this, "Password and confirm password do not match.", "Device Initialization",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool isHikvision = brandBox.Text.IndexOf("Hikvision", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isHoneywell = brandBox.Text.IndexOf("Honeywell", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isUniview =
                brandBox.Text.IndexOf("Uniview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                modelBox.Text.IndexOf("Uniview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                brandBox.Text.IndexOf("Zhejiang Uniview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                modelBox.Text.IndexOf("Zhejiang Uniview", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isDahua =
                brandBox.Text.IndexOf("Dahua", StringComparison.OrdinalIgnoreCase) >= 0 ||
                modelBox.Text.IndexOf("Dahua", StringComparison.OrdinalIgnoreCase) >= 0 ||
                brandBox.Text.IndexOf("Zhejiang Dahua", StringComparison.OrdinalIgnoreCase) >= 0 ||
                modelBox.Text.IndexOf("Zhejiang Dahua", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isSunell =
                brandBox.Text.IndexOf("Sunell", StringComparison.OrdinalIgnoreCase) >= 0 ||
                modelBox.Text.IndexOf("Sunell", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isTiandy =
                brandBox.Text.IndexOf("Tiandy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                modelBox.Text.IndexOf("Tiandy", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isBosch =
                brandBox.Text.IndexOf("Bosch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                modelBox.Text.IndexOf("Bosch", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isAxis =
                brandBox.Text.IndexOf("Axis", StringComparison.OrdinalIgnoreCase) >= 0 ||
                modelBox.Text.IndexOf("Axis", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isHikvision || isHoneywell || isUniview || isDahua || isSunell || isTiandy || isBosch || isAxis)
            {
                string message;
                bool ok;

                ActivationAttempted = true;
                initializeButton.Enabled = false;
                statusLabel.Text = "Initializing device...";
                Application.DoEvents();

                if (isHoneywell)
                {
                    string msg = "";
                    bool hasHcActivationEndpoint = await DeviceSDK.Honeywell.HoneywellActivationStatusProbe.HasActivationEndpointAsync(ipBox.Text.Trim());

                    if (hasHcActivationEndpoint)
                    {
                        // Honeywell HC30/35-series: port 8000 is typically closed.
                        // Use HTTP/HTTPS ISAPI activation - no SDK required.
                        ok = await Task.Run(() =>
                            DeviceSDK.Honeywell.HoneywellActivator.ActivateDevice(
                                ipBox.Text.Trim(),
                                passwordBox.Text,
                                out msg));
                    }
                    else
                    {
                        // Older Honeywell models use a Dahua/OEM-style DevInit web flow,
                        // but stay isolated from the Dahua activator.
                        ok = await Task.Run(() =>
                            DeviceSDK.Honeywell.HoneywellLegacyActivator.ActivateDevice(
                                ipBox.Text.Trim(),
                                usernameBox.Text.Trim(),
                                passwordBox.Text,
                                out msg));
                    }

                    message = msg;
                }
                  else if (isUniview)
                  {
                      string msg = "";
                      string probeResult = await Task.Run(() => DeviceSDK.Uniview.UniviewActivationStatusProbe.GetStatusAsync(ipBox.Text.Trim()));
                      if (probeResult == "Active")
                      {
                          ok = true;
                          msg = "Already activated — no action required";
                      }
                      else
                      {
                          ok = await Task.Run(() =>
                              UniviewActivator.ActivateDevice(
                                  ipBox.Text.Trim(),
                                  usernameBox.Text.Trim(),
                                  passwordBox.Text,
                                  out msg));
                      }
                      message = msg;
                  }
                else if (isDahua)
                {
                    string msg = "";
                    ok = await Task.Run(() =>
                        DahuaActivator.ActivateDevice(
                            ipBox.Text.Trim(),
                            usernameBox.Text.Trim(),
                            passwordBox.Text,
                            out msg));
                    message = msg;
                }
                else if (isSunell)
                {
                    string msg = "";
                    ok = await Task.Run(() =>
                        SunellActivator.ActivateDevice(
                            ipBox.Text.Trim(),
                            usernameBox.Text.Trim(),
                            passwordBox.Text,
                            out msg));
                    message = msg;
                }
                else if (isTiandy)
                {
                    string msg = "";
                    ok = await Task.Run(() =>
                        TiandyActivator.ActivateDevice(
                            ipBox.Text.Trim(),
                            usernameBox.Text.Trim(),
                            passwordBox.Text,
                            out msg));
                    message = msg;

                    if (ok)
                    {
                        string model = await TiandyInfoProbe.GetModelAsync(
                            ipBox.Text.Trim(),
                            usernameBox.Text.Trim(),
                            passwordBox.Text);
                        UpdatedUniqueId = TiandyInfoProbe.LastUniqueId;
                        if (!string.IsNullOrWhiteSpace(model))
                            modelBox.Text = model;
                    }
                }
                else if (isBosch)
                {
                    string msg = "";
                    ok = await Task.Run(() =>
                        DeviceSDK.Bosch.BoschActivator.ActivateDevice(
                            ipBox.Text.Trim(),
                            usernameBox.Text.Trim(),
                            passwordBox.Text,
                            out msg));
                    message = msg;
                }
                else if (isAxis)
                {
                    string msg = "";
                    ok = await Task.Run(() =>
                        AxisActivator.ActivateDevice(
                            ipBox.Text.Trim(),
                            usernameBox.Text.Trim(),
                            passwordBox.Text,
                            out msg));
                    message = msg;
                }
                else
                {
                    // Genuine Hikvision: SDK activation on port 8000.
                    string msg = "";
                    ok = await Task.Run(() =>
                        HikvisionActivator.ActivateDevice(
                            ipBox.Text.Trim(),
                            passwordBox.Text,
                            out msg));
                    message = msg;
                }

                if (!ok)
                {
                    // Do not override the activator's specific error message with a generic status probe.
                    // The activator provides much better context (e.g. "Privacy agreement required").
                }

                initializeButton.Enabled = true;
                statusLabel.Text = ok ? "Active." : "Failed.";

                MessageBox.Show(this,
                    message,
                    "Device Activation",
                    MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);

                if (ok)
                {
                    activationBox.Text = "Active";
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else if (isDahua)
                {
                    activationBox.Text = await Task.Run(() =>
                        DahuaActivationStatusProbe.GetStatus(ipBox.Text.Trim()));
                }
                else if (isSunell)
                {
                    activationBox.Text = await Task.Run(() =>
                        SunellActivationStatusProbe.GetStatus(ipBox.Text.Trim()));
                }
                else if (isTiandy)
                {
                    activationBox.Text = await Task.Run(() =>
                        TiandyActivationStatusProbe.GetStatus(ipBox.Text.Trim()));
                }
                else if (isBosch)
                {
                    activationBox.Text = await Task.Run(() =>
                        DeviceSDK.Bosch.BoschActivationStatusProbe.GetStatusAsync(ipBox.Text.Trim()));
                }
                else if (isAxis)
                {
                    activationBox.Text = await AxisActivationStatusProbe.GetStatusAsync(ipBox.Text.Trim());
                }
                else
                {
                    activationBox.Text = "Unknown";
                }

                return;
            }

            MessageBox.Show(this,
                "This brand is not supported yet.",
                "Device Initialization",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void AddLabel(string text, int left, int top)
        {
            Label label = new Label();
            label.Text = text;
            label.Left = left;
            label.Top = top + 4;
            label.Width = 125;
            Controls.Add(label);
        }

        private TextBox AddBox(int left, int top, bool readOnly)
        {
            TextBox box = new TextBox();
            box.Left = left;
            box.Top = top;
            box.Width = 285;
            box.ReadOnly = readOnly;
            Controls.Add(box);
            return box;
        }
    }
}
