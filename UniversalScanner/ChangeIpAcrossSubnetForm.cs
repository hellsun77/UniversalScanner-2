using System;
using System.Drawing;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using UniversalScanner.DeviceSDK.Common;

namespace UniversalScanner
{
    public class ChangeIpAcrossSubnetForm : Form
    {
        private TextBox currentIpBox;
        private TextBox adapterBox;
        private TextBox tempIpBox;
        private TextBox brandBox;

        private TextBox newIpBox;
        private TextBox subnetMaskBox;
        private TextBox gatewayBox;
        private TextBox usernameBox;
        private TextBox passwordBox;
        private CheckBox dhcpBox;
        private CheckBox showPasswordBox;
        private Label statusLabel;
        private Button applyButton;
        private Button cancelButton;
        private bool gatewayEdited;
        private bool updatingGateway;

        private string _cameraIp;
        private string _brand;
        private string _model;
        private string _adapterAlias;
        private string _subnetMask;

        public ChangeIpAcrossSubnetForm(string cameraIp, string brand, string model, string adapterAlias, string subnetMask)
        {
            _cameraIp = cameraIp;
            _brand = brand;
            _model = model;
            _adapterAlias = adapterAlias;
            _subnetMask = subnetMask;

            Text = "Change IP Across Subnet";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(500, 580);

            BuildUi();
            LoadData();
            AppVisualTheme.Apply(this);
        }

        private void BuildUi()
        {
            int leftLabel = 20;
            int leftBox = 170;
            int top = 20;
            int gap = 34;

            AddLabel("Brand / Method:", leftLabel, top);
            brandBox = AddBox(leftBox, top, true);

            top += gap;
            AddLabel("Current Camera IP:", leftLabel, top);
            currentIpBox = AddBox(leftBox, top, true);

            top += gap;
            AddLabel("Selected Adapter:", leftLabel, top);
            adapterBox = AddBox(leftBox, top, true);

            top += gap;
            AddLabel("Temp PC IP (to add):", leftLabel, top);
            tempIpBox = AddBox(leftBox, top, true);

            top += gap;
            var separator = new Label { Left = 10, Top = top, Width = 480, Height = 2, BorderStyle = BorderStyle.Fixed3D };
            Controls.Add(separator);

            top += 20;
            AddLabel("New IP Address:", leftLabel, top);
            newIpBox = AddBox(leftBox, top, false);
            newIpBox.TextChanged += NewIpBox_TextChanged;

            top += gap;
            AddLabel("New Subnet Mask:", leftLabel, top);
            subnetMaskBox = AddBox(leftBox, top, false);

            top += gap;
            AddLabel("New Gateway:", leftLabel, top);
            gatewayBox = AddBox(leftBox, top, false);
            gatewayBox.TextChanged += GatewayBox_TextChanged;

            top += gap;
            dhcpBox = new CheckBox();
            dhcpBox.Text = "Use DHCP";
            dhcpBox.Left = leftBox;
            dhcpBox.Top = top;
            dhcpBox.Width = 200;
            Controls.Add(dhcpBox);

            top += gap;
            AddLabel("Admin Username:", leftLabel, top);
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

            top += gap + 24;
            statusLabel = new Label();
            statusLabel.Text = "Ready to change IP across subnet.";
            statusLabel.Left = 20;
            statusLabel.Top = top;
            statusLabel.Width = 460;
            statusLabel.Height = 40;
            Controls.Add(statusLabel);

            top += 50;
            applyButton = new Button();
            applyButton.Text = "Apply";
            applyButton.Left = 300;
            applyButton.Top = top;
            applyButton.Width = 85;
            applyButton.Click += ApplyButton_Click;

            cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Left = 395;
            cancelButton.Top = top;
            cancelButton.Width = 85;
            cancelButton.DialogResult = DialogResult.Cancel;

            Controls.Add(applyButton);
            Controls.Add(cancelButton);

            AcceptButton = applyButton;
            CancelButton = cancelButton;
        }

        private void LoadData()
        {
            if (string.IsNullOrWhiteSpace(_adapterAlias))
                _adapterAlias = ResolveAdapterAlias();

            brandBox.Text = _brand;
            currentIpBox.Text = _cameraIp;
            adapterBox.Text = _adapterAlias;

            string mask = TemporarySubnetService.Instance.DetermineSubnetMask(_cameraIp, _subnetMask);
            var foundIp = TemporarySubnetService.Instance.FindAvailableTemporaryIp(System.Net.IPAddress.Parse(_cameraIp), System.Net.IPAddress.Parse(mask));
            tempIpBox.Text = foundIp?.ToString() ?? "";

            updatingGateway = true;
            newIpBox.Text = _cameraIp;
            subnetMaskBox.Text = "255.255.255.0";
            gatewayBox.Text = SuggestGateway(_cameraIp);
            updatingGateway = false;
            gatewayEdited = false;

            usernameBox.Text = "admin";
            passwordBox.Text = "";
        }

        public void PreFill(string newIp, string mask, string gateway, string username, string password, bool isDhcp)
        {
            updatingGateway = true;
            newIpBox.Text = newIp;
            subnetMaskBox.Text = mask;
            gatewayBox.Text = gateway;
            updatingGateway = false;
            
            usernameBox.Text = username;
            passwordBox.Text = password;
            dhcpBox.Checked = isDhcp;
        }

        private void LogAndStatus(string message)
        {
            statusLabel.Text = message;
            Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "[ChangeIpAcrossSubnet] " + message);
            Application.DoEvents();
        }

        private void LogFailure(string reason)
        {
            string msg = $"Failed: {reason}";
            statusLabel.Text = msg + " (Temporary subnet kept for troubleshooting)";
            Logger.getInstance().WriteLine(Logger.DebugLevel.Error, "[ChangeIpAcrossSubnet] " + msg);
            Application.DoEvents();
        }

        private async void ApplyButton_Click(object sender, EventArgs e)
        {
            if (!ValidateInputs())
                return;

            string newIp = newIpBox.Text.Trim();
            applyButton.Enabled = false;

            try
            {
                if (string.IsNullOrWhiteSpace(_adapterAlias))
                {
                    _adapterAlias = ResolveAdapterAlias();
                    adapterBox.Text = _adapterAlias;
                }

                if (string.IsNullOrWhiteSpace(_adapterAlias))
                {
                    MessageBox.Show(this,
                        "Could not choose a network adapter automatically. Please use a connected physical network adapter.",
                        "Change IP Across Subnet",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    applyButton.Enabled = true;
                    return;
                }

                string tempIp = tempIpBox.Text.Trim();
                if (string.IsNullOrEmpty(tempIp))
                {
                    MessageBox.Show(this, "Could not determine a valid temporary IP address.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    applyButton.Enabled = true;
                    return;
                }

                TempSubnetLogger.LogHeader("New Operation");
                TempSubnetLogger.Log($"Selected adapter: {_adapterAlias}");
                TempSubnetLogger.Log($"Camera IP: {_cameraIp}");
                TempSubnetLogger.Log($"Calculated Temporary IP: {tempIp}");
                TempSubnetLogger.Log($"Brand: {brandBox.Text.Trim()}");

                var cameraIpObj = System.Net.IPAddress.Parse(_cameraIp);
                var tempIpObj = System.Net.IPAddress.Parse(tempIp);
                var tempMaskObj = System.Net.IPAddress.Parse(TemporarySubnetService.Instance.DetermineSubnetMask(_cameraIp, _subnetMask));

                LogAndStatus("Adding temporary IP...");
                var addResult = await TemporarySubnetService.Instance.AddTemporaryIpAsync(_adapterAlias, tempIpObj, tempMaskObj, cameraIpObj);

                if (!addResult.Success)
                {
                    LogFailure($"Temporary IP was not added. {addResult.ErrorMessage}");
                    applyButton.Enabled = true;
                    return;
                }

                LogAndStatus("Temporary IP added.");
                await Task.Delay(500);

                LogAndStatus("Validating route...");
                bool routeValid = await TemporarySubnetService.Instance.ValidateAndConfigureRouteAsync(_adapterAlias, tempIpObj, cameraIpObj);
                if (!routeValid)
                {
                    LogFailure("Route was not created.");
                    applyButton.Enabled = true;
                    return;
                }
                
                LogAndStatus("Route verified.");
                await Task.Delay(500);

                LogAndStatus("Testing old camera IP...");
                bool isReachable = false;
                for (int i = 0; i < 15; i++)
                {
                    await Task.Delay(1000);
                    if (await TemporarySubnetService.Instance.IsCameraReachableAsync(cameraIpObj, tempIpObj, _adapterAlias))
                    {
                        isReachable = true;
                        break;
                    }
                }

                if (!isReachable)
                {
                    LogFailure("Old camera IP is not reachable.");
                    MessageBox.Show(this, "Camera is not reachable via the temporary subnet. The IP change cannot proceed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    applyButton.Enabled = true;
                    return;
                }

                LogAndStatus("Old camera reachable.");
                await Task.Delay(500);

                LogAndStatus("Sending Change IP command...");
                TempSubnetLogger.LogSubHeader("Sending Change IP command");
                var sw = System.Diagnostics.Stopwatch.StartNew();

                var changeResult = await ChangeIpForm.ExecuteChangeIpCoreAsync(
                    brandBox.Text.Trim(), _model,
                    _cameraIp, usernameBox.Text.Trim(), passwordBox.Text,
                    newIp, subnetMaskBox.Text.Trim(), gatewayBox.Text.Trim(),
                    dhcpBox.Checked,
                    status =>
                    {
                    });
                
                sw.Stop();
                TempSubnetLogger.Log($"SDK method name: ExecuteChangeIpCoreAsync");
                TempSubnetLogger.Log($"Duration: {sw.ElapsedMilliseconds} ms");
                TempSubnetLogger.Log($"Return value: Success={changeResult.Success}, Message={changeResult.Message}");

                if (changeResult.Success)
                {
                    LogAndStatus("SDK/API result: Success.");
                    DeviceFirmwareProbe.RegisterCredentials(_cameraIp, usernameBox.Text.Trim(), passwordBox.Text);
                    if (!string.IsNullOrWhiteSpace(newIp))
                        DeviceFirmwareProbe.RegisterCredentials(newIp, usernameBox.Text.Trim(), passwordBox.Text);

                    LogAndStatus("Waiting for camera network restart...");
                    
                    bool newIpReachable = false;
                    for (int i = 0; i < 5; i++) await Task.Delay(1000); // Base wait

                    TemporarySubnetService.AddIpResult newIpAddResult = null;
                    string newTempIp = null;
                    
                    string currentSubnet = TemporarySubnetService.Instance.DetermineSubnetMask(_cameraIp, _subnetMask);
                    string targetSubnet = TemporarySubnetService.Instance.DetermineSubnetMask(newIp, subnetMaskBox.Text.Trim());

                    var newIpObj = System.Net.IPAddress.Parse(newIp);

                    if (await TemporarySubnetService.Instance.IsCameraReachableAsync(newIpObj, null, _adapterAlias))
                    {
                        newIpReachable = true;
                    }
                    else
                    {
                        LogAndStatus("Adding temporary subnet for new IP if needed...");
                        string newMask = TemporarySubnetService.Instance.DetermineSubnetMask(newIp, subnetMaskBox.Text.Trim());
                        var newMaskObj = System.Net.IPAddress.Parse(newMask);
                        var foundNewTemp = TemporarySubnetService.Instance.FindAvailableTemporaryIp(newIpObj, newMaskObj);
                        newTempIp = foundNewTemp?.ToString();
                        
                        if (!string.IsNullOrEmpty(newTempIp))
                        {
                            var newTempIpObj = System.Net.IPAddress.Parse(newTempIp);
                            TempSubnetLogger.LogSubHeader("Adding second temporary IP");
                            newIpAddResult = await TemporarySubnetService.Instance.AddTemporaryIpAsync(_adapterAlias, newTempIpObj, newMaskObj, newIpObj);
                        }
                        
                        LogAndStatus("Testing new camera IP...");
                        var loopNewTempObj = string.IsNullOrEmpty(newTempIp) ? null : System.Net.IPAddress.Parse(newTempIp);
                        for (int i = 0; i < 15; i++)
                        {
                            TempSubnetLogger.Log($"Wait loop attempt {i + 1}");
                            await Task.Delay(1000);
                            if (await TemporarySubnetService.Instance.IsCameraReachableAsync(newIpObj, loopNewTempObj, _adapterAlias))
                            {
                                newIpReachable = true;
                                break;
                            }
                        }
                    }

                    if (newIpReachable)
                    {
                        LogAndStatus("New IP verified.");
                        LogAndStatus("Success.");
                        TempSubnetLogger.LogSubHeader("Final result");
                        TempSubnetLogger.Log("SUCCESS");
                        MessageBox.Show(this, changeResult.Message + "\n\nSuccess: The camera successfully answers at the new IP address.", "Change IP Across Subnet", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        
                        // ONLY cleanup on total success
                        if (Config.getInstance().AutoRemoveTempIps)
                        {
                            await TemporarySubnetService.Instance.RemoveTemporaryIpAsync(_adapterAlias, tempIpObj, cameraIpObj, null);
                            if (newIpAddResult != null && newIpAddResult.Success && !string.IsNullOrEmpty(newTempIp))
                            {
                                await TemporarySubnetService.Instance.RemoveTemporaryIpAsync(_adapterAlias, System.Net.IPAddress.Parse(newTempIp), newIpObj, null);
                            }
                        }
                        
                        DialogResult = DialogResult.OK;
                        Close();
                        return;
                    }
                    else
                    {
                        TempSubnetLogger.LogSubHeader("Final result");
                        TempSubnetLogger.Log("FAILED: New camera IP did not respond after reboot.");
                        LogFailure("New camera IP did not respond after reboot.");
                        MessageBox.Show(this, changeResult.Message + "\n\nWarning: Could not automatically verify the new IP address. You may need to manually scan or check connectivity.", "Change IP Across Subnet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        applyButton.Enabled = true;
                        return;
                    }
                }
                else
                {
                    TempSubnetLogger.LogSubHeader("Final result");
                    TempSubnetLogger.Log($"FAILED: Change IP command returned false. {changeResult.Message}");
                    LogFailure($"Change IP command returned false. {changeResult.Message}");
                    MessageBox.Show(this, "Change IP failed.\n\n" + changeResult.Message, "Change IP Across Subnet", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    applyButton.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                TempSubnetLogger.LogSubHeader("Final result");
                TempSubnetLogger.Log($"FAILED: Exception: {ex.Message}");
                LogFailure($"Exception: {ex.Message}");
                MessageBox.Show(this, "An error occurred.\n\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

            if (!dhcpBox.Checked)
            {
                if (string.IsNullOrWhiteSpace(newIpBox.Text) || !IPAddress.TryParse(newIpBox.Text.Trim(), out parsed))
                {
                    MessageBox.Show(this, "New IP address is not valid.", "Change IP Address", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(subnetMaskBox.Text) || !IPAddress.TryParse(subnetMaskBox.Text.Trim(), out parsed))
                {
                    MessageBox.Show(this, "Subnet mask is not valid.", "Change IP Address", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(gatewayBox.Text) || !IPAddress.TryParse(gatewayBox.Text.Trim(), out parsed))
                {
                    MessageBox.Show(this, "Gateway is not valid.", "Change IP Address", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(usernameBox.Text))
            {
                MessageBox.Show(this, "Username is required.", "Change IP Address", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(passwordBox.Text))
            {
                MessageBox.Show(this, "Admin password is required.", "Change IP Address", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private string ResolveAdapterAlias()
        {
            string mask = TemporarySubnetService.Instance.DetermineSubnetMask(_cameraIp, _subnetMask);
            string adapterAlias = NetworkTools.GetPreferredPhysicalAdapterName();

            if (string.IsNullOrWhiteSpace(adapterAlias))
                adapterAlias = NetworkTools.GetBestAdapterNameForTarget(_cameraIp, mask);

            return adapterAlias ?? "";
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
