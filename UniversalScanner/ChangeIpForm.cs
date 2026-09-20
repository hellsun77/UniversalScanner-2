using System;
using System.Drawing;
using System.Net;
using System.Windows.Forms;
using System.Linq;
using UniversalScanner.DeviceSDK.Hikvision;
using UniversalScanner.DeviceSDK.Honeywell;
using UniversalScanner.DeviceSDK.Uniview;
using UniversalScanner.DeviceSDK.Common;
using UniversalScanner.DeviceSDK.Dahua;

namespace UniversalScanner
{
    public class ChangeIpForm : Form
    {
        private TextBox brandBox;
        private TextBox currentIpBox;
        private TextBox modelBox;
        private TextBox activationBox;
        private TextBox newIpBox;
        private TextBox subnetMaskBox;
        private TextBox gatewayBox;
        private TextBox usernameBox;
        private TextBox passwordBox;
        private CheckBox showPasswordBox;
        private CheckBox dhcpBox;
        private Label statusLabel;
        private Button applyButton;
        private Button cancelButton;
        private bool gatewayEdited;
        private bool updatingGateway;
        private readonly bool isHikvision;
        private readonly bool isHoneywell;
        private readonly bool isUniview;
        private readonly bool isDahua;
        private readonly bool isSunell;
        private readonly bool isTiandy;
        private readonly bool isBosch;
        private readonly bool isAxis;

        public string NewIPAddress => newIpBox.Text.Trim();
        public string SubnetMask => subnetMaskBox.Text.Trim();

        public ChangeIpForm(string brand, string currentIp, string model, string activation)
        {
            isHikvision = IsHikvisionText(brand) || IsHikvisionText(model);
            isHoneywell = IsHoneywellText(brand) || IsHoneywellText(model);
            isUniview = IsUniviewText(brand) || IsUniviewText(model);
            isDahua = IsDahuaText(brand) || IsDahuaText(model);
            isSunell = IsSunellText(brand) || IsSunellText(model);
            isTiandy = IsTiandyText(brand) || IsTiandyText(model);
            isBosch = IsBoschText(brand) || IsBoschText(model);
            isAxis = IsAxisText(brand) || IsAxisText(model);

            Text = isHikvision
                ? "Change Hikvision IP Address"
                : isHoneywell
                ? "Change Honeywell IP Address"
                : isUniview
                    ? "Change Uniview IP Address"
                    : isDahua
                        ? "Change Dahua IP Address"
                        : isSunell
                            ? "Change Sunell IP Address"
                            : isTiandy
                                ? "Change Tiandy IP Address"
                                : isBosch
                                    ? "Change Bosch IP Address"
                                    : isAxis
                                    ? "Change Axis IP Address"
                                    : "Change ONVIF IP Address";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(480, 490);

            BuildUi();

            brandBox.Text = brand;
            currentIpBox.Text = currentIp;
            modelBox.Text = model;
            activationBox.Text = string.IsNullOrWhiteSpace(activation) ? "Unknown" : activation;

            updatingGateway = true;
            newIpBox.Text = currentIp;
            subnetMaskBox.Text = "255.255.255.0";
            gatewayBox.Text = SuggestGateway(currentIp);
            updatingGateway = false;
            gatewayEdited = false;
            usernameBox.Text = isBosch ? "service" : isAxis ? "root" : "admin";
            ApplyUniversalLoginIfAvailable(preserveDefaultUsername: isHoneywell);
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

        private void BuildUi()
        {
            int leftLabel = 20;
            int leftBox = 170;
            int top = 20;
            int gap = 34;

            AddLabel("Brand:", leftLabel, top);
            brandBox = AddBox(leftBox, top, true);

            top += gap;
            AddLabel("Current IP:", leftLabel, top);
            currentIpBox = AddBox(leftBox, top, true);

            top += gap;
            AddLabel("Model:", leftLabel, top);
            modelBox = AddBox(leftBox, top, true);

            top += gap;
            AddLabel("Activation:", leftLabel, top);
            activationBox = AddBox(leftBox, top, true);

            top += gap + 8;
            AddLabel("New IP Address:", leftLabel, top);
            newIpBox = AddBox(leftBox, top, false);
            newIpBox.TextChanged += NewIpBox_TextChanged;

            top += gap;
            AddLabel("Subnet Mask:", leftLabel, top);
            subnetMaskBox = AddBox(leftBox, top, false);

            top += gap;
            AddLabel("Gateway:", leftLabel, top);
            gatewayBox = AddBox(leftBox, top, false);
            gatewayBox.TextChanged += GatewayBox_TextChanged;

            top += gap;
            dhcpBox = new CheckBox();
            dhcpBox.Text = "Enable DHCP";
            dhcpBox.Left = leftBox;
            dhcpBox.Top = top;
            dhcpBox.Width = 180;
            dhcpBox.Enabled = !isHoneywell;
            dhcpBox.CheckedChanged += (s, e) =>
            {
                bool enabled = !dhcpBox.Checked;
                newIpBox.Enabled = enabled;
                subnetMaskBox.Enabled = enabled;
                gatewayBox.Enabled = enabled;
            };
            Controls.Add(dhcpBox);

            top += gap;
            AddLabel("Username:", leftLabel, top);
            usernameBox = AddBox(leftBox, top, false);

            top += gap;
            AddLabel("Admin Password:", leftLabel, top);
            passwordBox = AddBox(leftBox, top, false);
            passwordBox.UseSystemPasswordChar = true;

            showPasswordBox = new CheckBox();
            showPasswordBox.Text = "Show password";
            showPasswordBox.Left = leftBox;
            showPasswordBox.Top = top + 32;
            showPasswordBox.Width = 180;
            showPasswordBox.CheckedChanged += (s, e) =>
            {
                passwordBox.UseSystemPasswordChar = !showPasswordBox.Checked;
            };
            Controls.Add(showPasswordBox);

            statusLabel = new Label();
            statusLabel.Left = 20;
            statusLabel.Top = 405;
            statusLabel.Width = 440;
            statusLabel.Height = 36;
            statusLabel.Text = "Ready.";
            Controls.Add(statusLabel);

            applyButton = new Button();
            applyButton.Text = "Apply";
            applyButton.Left = 290;
            applyButton.Top = 450;
            applyButton.Width = 75;
            applyButton.Click += ApplyButton_Click;

            cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Left = 375;
            cancelButton.Top = 450;
            cancelButton.Width = 75;
            cancelButton.DialogResult = DialogResult.Cancel;

            Controls.Add(applyButton);
            Controls.Add(cancelButton);

            AcceptButton = applyButton;
            CancelButton = cancelButton;
        }

        private async void ApplyButton_Click(object sender, EventArgs e)
        {
            if (!ValidateInputs())
                return;

            string currentIp = currentIpBox.Text.Trim();
            string newIp = newIpBox.Text.Trim();

            applyButton.Enabled = false;

            try
            {
                if (!dhcpBox.Checked &&
                    !newIp.Equals(currentIp, StringComparison.OrdinalIgnoreCase))
                {
                    string reachabilityWarning = NetworkTools.GetReachabilityWarning(newIp);

                    if (!string.IsNullOrWhiteSpace(reachabilityWarning))
                    {
                        DialogResult result = MessageBox.Show(this,
                            reachabilityWarning + "\r\n\r\nApply this IP address anyway?",
                            Text,
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2);

                        if (result != DialogResult.Yes)
                        {
                            statusLabel.Text = "Change cancelled.";
                            applyButton.Enabled = true;
                            return;
                        }
                    }

                    statusLabel.Text = "Checking IP conflict...";
                    Application.DoEvents();

                    bool ipBusy = await NetworkTools.IsOnlineAsync(newIp, 800);

                    if (ipBusy)
                    {
                        MessageBox.Show(this,
                            "IP conflict detected.\n\nThe address " + newIp + " is already in use.\nPlease choose another IP address.",
                            Text,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);

                        statusLabel.Text = "IP conflict detected.";
                        applyButton.Enabled = true;
                        return;
                    }
                }

                statusLabel.Text = "Applying network configuration...";
                Application.DoEvents();

                string message = "";
                bool ok;

                if (isHoneywell)
                {
                    ok = await HoneywellNetwork.ChangeNetworkAddressAsync(
                        currentIp,
                        usernameBox.Text.Trim(),
                        passwordBox.Text,
                        newIp,
                        subnetMaskBox.Text.Trim(),
                        gatewayBox.Text.Trim(),
                        status =>
                        {
                            message = status;
                            statusLabel.Text = status;
                            Application.DoEvents();
                        });

                    if (ok)
                        message = "Honeywell IP address changed. The camera may reboot before it answers at the new address.";
                    else if (string.IsNullOrWhiteSpace(message))
                        message = "Honeywell IP change failed. Check credentials and that ONVIF is enabled on the camera.";
                }
                else if (isUniview)
                {
                    ok = await UniviewNetwork.ChangeNetworkAddressAsync(
                        currentIp,
                        usernameBox.Text.Trim(),
                        passwordBox.Text,
                        newIp,
                        subnetMaskBox.Text.Trim(),
                        gatewayBox.Text.Trim(),
                        dhcpBox.Checked,
                        status =>
                        {
                            message = status;
                            statusLabel.Text = status;
                            Application.DoEvents();
                        });

                    if (ok)
                        message = dhcpBox.Checked
                            ? "DHCP enabled. The Uniview camera may reboot before it answers at the new address."
                            : "Uniview IP address changed. The camera may reboot before it answers at the new address.";
                    else if (string.IsNullOrWhiteSpace(message))
                        message = "Uniview IP change failed. Check credentials and that ONVIF is enabled on the camera.";
                }
                else if (isHikvision)
                {
                    ok = HikvisionNetwork.ChangeNetworkAddress(
                        currentIp,
                        usernameBox.Text.Trim(),
                        passwordBox.Text,
                        newIp,
                        subnetMaskBox.Text.Trim(),
                        gatewayBox.Text.Trim(),
                        dhcpBox.Checked,
                        out message);
                }
                else if (isDahua)
                {
                    ok = await DahuaNetwork.ChangeNetworkAddressAsync(
                        currentIp,
                        usernameBox.Text.Trim(),
                        passwordBox.Text,
                        newIp,
                        subnetMaskBox.Text.Trim(),
                        gatewayBox.Text.Trim(),
                        dhcpBox.Checked,
                        status =>
                        {
                            message = status;
                            statusLabel.Text = status;
                            Application.DoEvents();
                        });

                    if (ok)
                        message = dhcpBox.Checked
                            ? "DHCP enabled. The Dahua camera may reboot before it answers at the new address."
                            : "Dahua IP address changed. The camera may reboot before it answers at the new address.";
                    else if (string.IsNullOrWhiteSpace(message))
                        message = "Dahua IP change failed. Check credentials and that ONVIF or Dahua CGI access is enabled.";
                }
                else if (isBosch)
                {
                    string conflictMessage = await GetBoschIpConflictMessageAsync(currentIp, newIp);
                    if (!string.IsNullOrWhiteSpace(conflictMessage))
                    {
                        ok = false;
                        message = conflictMessage;
                    }
                    else
                    {
                    var boschResult = await DeviceSDK.Bosch.BoschNetwork.ChangeNetworkAddressAsync(
                        currentIp,
                        usernameBox.Text.Trim(),
                        passwordBox.Text,
                        newIp,
                        subnetMaskBox.Text.Trim(),
                        gatewayBox.Text.Trim(),
                        dhcpBox.Checked,
                        status =>
                        {
                            message = status;
                            statusLabel.Text = status;
                            Application.DoEvents();
                        });

                    if (boschResult.State == DeviceSDK.Bosch.BoschReadinessState.ConfigurationAccepted)
                    {
                        DeviceSDK.Bosch.BoschNetwork.BoschIpChangeResult finalResult = null;
                        
                        var mainWindow = this.Owner as ScannerWindow ?? System.Windows.Forms.Application.OpenForms.OfType<ScannerWindow>().FirstOrDefault();
                        
                        using (var verifyForm = new DeviceReadinessProgressForm(async (statusCb, cancelCheck) => 
                        {
                            finalResult = await DeviceSDK.Bosch.BoschNetwork.VerifyReadinessAsync(
                                currentIp, 
                                newIp, 
                                boschResult.OriginalMac, 
                                dhcpBox.Checked, 
                                () => { if (mainWindow != null) mainWindow.Invoke(new Action(() => mainWindow.TriggerInternalDiscovery())); },
                                (targetIp, targetMac) => { 
                                    bool found = false;
                                    if (mainWindow != null) mainWindow.Invoke(new Action(() => { found = mainWindow.IsDeviceDiscovered(targetIp, targetMac); })); 
                                    return found; 
                                },
                                statusCb, 
                                cancelCheck);
                        }))
                        {
                            verifyForm.ShowDialog(this);
                        }

                        if (finalResult != null)
                        {
                            ok = finalResult.Success;
                            message = finalResult.Message;
                        }
                        else
                        {
                            ok = boschResult.Success;
                            message = boschResult.Message;
                        }
                    }
                    else
                    {
                        ok = boschResult.Success;
                        message = boschResult.Message;
                    }
                    }
                }
                else if (isAxis)
                {
                    ok = await DeviceSDK.Axis.AxisNetwork.ChangeNetworkAddressAsync(
                        currentIp,
                        usernameBox.Text.Trim(),
                        passwordBox.Text,
                        newIp,
                        subnetMaskBox.Text.Trim(),
                        gatewayBox.Text.Trim(),
                        dhcpBox.Checked,
                        status =>
                        {
                            message = status;
                            statusLabel.Text = status;
                            Application.DoEvents();
                        });

                    if (ok)
                        message = dhcpBox.Checked
                            ? "DHCP enabled. The Axis camera may restart before it answers at the new address."
                            : "Axis IP address changed. The camera may restart before it answers at the new address.";
                    else if (string.IsNullOrWhiteSpace(message))
                        message = "Axis IP change failed. Check credentials and VAPIX access.";
                }
                else
                {
                    ok = await GenericOnvifNetwork.ChangeNetworkAddressAsync(
                        currentIp,
                        usernameBox.Text.Trim(),
                        passwordBox.Text,
                        newIp,
                        subnetMaskBox.Text.Trim(),
                        gatewayBox.Text.Trim(),
                        dhcpBox.Checked,
                        status =>
                        {
                            message = status;
                            statusLabel.Text = status;
                            Application.DoEvents();
                        });

                    if (ok)
                        message = dhcpBox.Checked
                            ? "DHCP enabled via ONVIF. The device may reboot before it answers at the new address."
                            : "IP address changed via ONVIF. The device may reboot before it answers at the new address.";
                    else if (string.IsNullOrWhiteSpace(message))
                        message = "ONVIF IP change failed. Check credentials and that ONVIF is enabled on the device.";
                }

                MessageBox.Show(this,
                    message,
                    Text,
                    MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);

                if (ok)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }

                statusLabel.Text = "Failed.";
                applyButton.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Change IP failed.\n\n" + ex.Message,
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                statusLabel.Text = "Failed.";
                applyButton.Enabled = true;
            }
        }

        private void NewIpBox_TextChanged(object sender, EventArgs e)
        {
            if (gatewayEdited)
                return;

            updatingGateway = true;
            gatewayBox.Text = SuggestGateway(newIpBox.Text.Trim());
            updatingGateway = false;
        }

        private void GatewayBox_TextChanged(object sender, EventArgs e)
        {
            if (!updatingGateway)
                gatewayEdited = true;
        }

        private bool ValidateInputs()
        {
            IPAddress parsed;

            if (string.IsNullOrWhiteSpace(currentIpBox.Text) ||
                !IPAddress.TryParse(currentIpBox.Text.Trim(), out parsed))
            {
                MessageBox.Show(this, "Current IP address is not valid.", "Change IP Address",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!dhcpBox.Checked)
            {
                if (string.IsNullOrWhiteSpace(newIpBox.Text) ||
                    !IPAddress.TryParse(newIpBox.Text.Trim(), out parsed))
                {
                    MessageBox.Show(this, "New IP address is not valid.", "Change IP Address",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(subnetMaskBox.Text) ||
                    !IPAddress.TryParse(subnetMaskBox.Text.Trim(), out parsed))
                {
                    MessageBox.Show(this, "Subnet mask is not valid.", "Change IP Address",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(gatewayBox.Text) ||
                    !IPAddress.TryParse(gatewayBox.Text.Trim(), out parsed))
                {
                    MessageBox.Show(this, "Gateway is not valid.", "Change IP Address",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(usernameBox.Text))
            {
                MessageBox.Show(this, "Username is required.", "Change IP Address",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(passwordBox.Text))
            {
                MessageBox.Show(this, "Admin password is required.", "Change IP Address",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private static bool IsHoneywellText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("Honeywell", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsHikvisionText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   (value.IndexOf("Hikvision", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("Hangzhou Hikvision", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsUniviewText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   (value.IndexOf("Uniview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("Zhejiang Uniview", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsDahuaText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   (value.IndexOf("Dahua", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("Zhejiang Dahua", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsSunellText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("Sunell", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsTiandyText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("Tiandy", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBoschText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("Bosch", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsAxisText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("Axis", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string SuggestGateway(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return "192.168.1.1";

            string[] parts = ip.Split('.');

            if (parts.Length != 4)
                return "192.168.1.1";

            return parts[0] + "." + parts[1] + "." + parts[2] + ".1";
        }

        private static async System.Threading.Tasks.Task<string> GetBoschIpConflictMessageAsync(string currentIp, string newIp)
        {
            if (string.Equals(currentIp, newIp, StringComparison.OrdinalIgnoreCase))
                return "";

            NetworkTools.FlushArpEntry(newIp);

            bool targetResponds = await NetworkTools.IsOnlineAsync(newIp, 1000);
            string targetMac = await NetworkTools.GetMacAsync(newIp, 2, 250);

            if (!targetResponds && string.IsNullOrWhiteSpace(targetMac))
                return "";

            string currentMac = await NetworkTools.GetMacAsync(currentIp, 2, 250);
            string cleanTargetMac = NormalizeMacText(targetMac);
            string cleanCurrentMac = NormalizeMacText(currentMac);

            if (!string.IsNullOrWhiteSpace(cleanTargetMac) &&
                !string.IsNullOrWhiteSpace(cleanCurrentMac) &&
                cleanTargetMac == cleanCurrentMac)
            {
                return "";
            }

            string macText = string.IsNullOrWhiteSpace(targetMac) ? "Unknown" : targetMac;
            return "IP conflict detected.\r\n\r\n" +
                   "Requested IP:\r\n" +
                   newIp + "\r\n\r\n" +
                   "Found device MAC:\r\n" +
                   macText + "\r\n\r\n" +
                   "Bosch IP change was not started.";
        }

        private static string NormalizeMacText(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac))
                return "";

            return mac.Replace("-", "").Replace(":", "").Trim().ToUpperInvariant();
        }

        public class ChangeIpResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
        }

        public static async System.Threading.Tasks.Task<ChangeIpResult> ExecuteChangeIpCoreAsync(
            string brand, string model, string currentIp, string username, string password,
            string newIp, string subnetMask, string gateway, bool useDhcp, Action<string> statusCallback)
        {
            string message = "";
            bool ok = false;
            try
            {
                bool isHoneywell = IsHoneywellText(brand) || IsHoneywellText(model);
                bool isUniview = IsUniviewText(brand) || IsUniviewText(model);
                bool isHikvision = IsHikvisionText(brand) || IsHikvisionText(model);
                bool isDahua = IsDahuaText(brand) || IsDahuaText(model);
                bool isBosch = IsBoschText(brand) || IsBoschText(model);
                bool isAxis = IsAxisText(brand) || IsAxisText(model);

                if (isHoneywell)
                {
                    ok = await DeviceSDK.Honeywell.HoneywellNetwork.ChangeNetworkAddressAsync(
                        currentIp, username, password, newIp, subnetMask, gateway, statusCallback);

                    if (ok) message = "Honeywell IP address changed. The camera may reboot before it answers at the new address.";
                    else if (string.IsNullOrWhiteSpace(message)) message = "Honeywell IP change failed. Check credentials and that ONVIF is enabled on the camera.";
                }
                else if (isUniview)
                {
                    ok = await DeviceSDK.Uniview.UniviewNetwork.ChangeNetworkAddressAsync(
                        currentIp, username, password, newIp, subnetMask, gateway, useDhcp, statusCallback);

                    if (ok) message = useDhcp ? "DHCP enabled. The Uniview camera may reboot before it answers at the new address." : "Uniview IP address changed. The camera may reboot before it answers at the new address.";
                    else if (string.IsNullOrWhiteSpace(message)) message = "Uniview IP change failed. Check credentials and that ONVIF is enabled on the camera.";
                }
                else if (isHikvision)
                {
                    ok = DeviceSDK.Hikvision.HikvisionNetwork.ChangeNetworkAddress(
                        currentIp, username, password, newIp, subnetMask, gateway, useDhcp, out message);
                }
                else if (isDahua)
                {
                    ok = await DeviceSDK.Dahua.DahuaNetwork.ChangeNetworkAddressAsync(
                        currentIp, username, password, newIp, subnetMask, gateway, useDhcp, statusCallback);

                    if (ok) message = useDhcp ? "DHCP enabled. The Dahua camera may reboot before it answers at the new address." : "Dahua IP address changed. The camera may reboot before it answers at the new address.";
                    else if (string.IsNullOrWhiteSpace(message)) message = "Dahua IP change failed. Check credentials and that ONVIF or Dahua CGI access is enabled.";
                }
                else if (isBosch)
                {
                    var boschResult = await DeviceSDK.Bosch.BoschNetwork.ChangeNetworkAddressAsync(
                        currentIp, username, password, newIp, subnetMask, gateway, useDhcp, statusCallback);

                    ok = boschResult.Success;
                    message = boschResult.Message;
                }
                else if (isAxis)
                {
                    ok = await DeviceSDK.Axis.AxisNetwork.ChangeNetworkAddressAsync(
                        currentIp, username, password, newIp, subnetMask, gateway, useDhcp, statusCallback);

                    if (ok) message = useDhcp ? "DHCP enabled. The Axis camera may restart before it answers at the new address." : "Axis IP address changed. The camera may restart before it answers at the new address.";
                    else if (string.IsNullOrWhiteSpace(message)) message = "Axis IP change failed. Check credentials and VAPIX access.";
                }
                else
                {
                    ok = await DeviceSDK.Common.GenericOnvifNetwork.ChangeNetworkAddressAsync(
                        currentIp, username, password, newIp, subnetMask, gateway, useDhcp, statusCallback);

                    if (ok) message = useDhcp ? "DHCP enabled via ONVIF. The device may reboot before it answers at the new address." : "IP address changed via ONVIF. The device may reboot before it answers at the new address.";
                    else if (string.IsNullOrWhiteSpace(message)) message = "ONVIF IP change failed. Check credentials and that ONVIF is enabled on the device.";
                }
            }
            catch (Exception ex)
            {
                ok = false;
                message = "Change IP failed.\n\n" + ex.Message;
            }

            return new ChangeIpResult { Success = ok, Message = message };
        }

        private void AddLabel(string text, int left, int top)
        {
            Label label = new Label();
            label.Text = text;
            label.Left = left;
            label.Top = top + 4;
            label.Width = 140;
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
