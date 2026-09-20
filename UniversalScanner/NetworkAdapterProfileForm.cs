using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UniversalScanner
{
    public class NetworkAdapterProfileForm : Form
    {
        private readonly NetworkAdapterService adapterService;
        private readonly NetworkProfileManager profileManager;
        private ComboBox adapterBox;
        private TextBox profileNameBox;
        private TextBox profilePathBox;
        private TextBox previewBox;
        private Label adminLabel;
        private Button saveButton;
        private Button dhcpButton;
        private Button restoreButton;
        private Button browseButton;
        private List<NetworkAdapterInfo> adapters;

        public NetworkAdapterProfileForm()
        {
            adapterService = new NetworkAdapterService();
            profileManager = new NetworkProfileManager();
            adapters = new List<NetworkAdapterInfo>();

            Text = "Network Adapter Profiles";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(700, 520);

            BuildUi();
            Shown += async (s, e) => await LoadAdaptersAsync();
        }

        private void BuildUi()
        {
            int labelLeft = 18;
            int boxLeft = 150;
            int top = 18;

            AddLabel("Adapter:", labelLeft, top);
            adapterBox = new ComboBox();
            adapterBox.Left = boxLeft;
            adapterBox.Top = top - 2;
            adapterBox.Width = 500;
            adapterBox.DropDownStyle = ComboBoxStyle.DropDownList;
            adapterBox.SelectedIndexChanged += async (s, e) => await RefreshPreviewAsync();
            Controls.Add(adapterBox);

            top += 38;
            AddLabel("Profile name:", labelLeft, top);
            profileNameBox = new TextBox();
            profileNameBox.Left = boxLeft;
            profileNameBox.Top = top - 2;
            profileNameBox.Width = 500;
            profileNameBox.Text = "Camera Network Profile";
            Controls.Add(profileNameBox);

            top += 38;
            AddLabel("Profile file:", labelLeft, top);
            profilePathBox = new TextBox();
            profilePathBox.Left = boxLeft;
            profilePathBox.Top = top - 2;
            profilePathBox.Width = 410;
            profilePathBox.Text = GetDefaultProfilePath();
            Controls.Add(profilePathBox);

            browseButton = new Button();
            browseButton.Text = "...";
            browseButton.Left = 570;
            browseButton.Top = top - 3;
            browseButton.Width = 80;
            browseButton.Click += browseButton_Click;
            Controls.Add(browseButton);

            top += 38;
            adminLabel = new Label();
            adminLabel.Left = labelLeft;
            adminLabel.Top = top;
            adminLabel.Width = 640;
            adminLabel.Height = 22;
            adminLabel.ForeColor = adapterService.IsAdministrator() ? Color.DarkGreen : Color.DarkRed;
            adminLabel.Text = adapterService.IsAdministrator()
                ? "Administrator mode detected. Adapter changes are allowed."
                : "Not running as Administrator. Saving works, but DHCP/restore changes will fail.";
            Controls.Add(adminLabel);

            top += 34;
            previewBox = new TextBox();
            previewBox.Left = labelLeft;
            previewBox.Top = top;
            previewBox.Width = 650;
            previewBox.Height = 290;
            previewBox.Multiline = true;
            previewBox.ScrollBars = ScrollBars.Vertical;
            previewBox.ReadOnly = true;
            previewBox.Font = new Font("Consolas", 9);
            Controls.Add(previewBox);

            saveButton = new Button();
            saveButton.Text = "Save Current Profile";
            saveButton.Left = 18;
            saveButton.Top = 465;
            saveButton.Width = 160;
            saveButton.Click += saveButton_Click;
            Controls.Add(saveButton);

            dhcpButton = new Button();
            dhcpButton.Text = "Apply DHCP";
            dhcpButton.Left = 190;
            dhcpButton.Top = 465;
            dhcpButton.Width = 120;
            dhcpButton.Click += dhcpButton_Click;
            Controls.Add(dhcpButton);

            restoreButton = new Button();
            restoreButton.Text = "Restore Saved Profile";
            restoreButton.Left = 322;
            restoreButton.Top = 465;
            restoreButton.Width = 170;
            restoreButton.Click += restoreButton_Click;
            Controls.Add(restoreButton);

            Button closeButton = new Button();
            closeButton.Text = "Close";
            closeButton.Left = 580;
            closeButton.Top = 465;
            closeButton.Width = 90;
            closeButton.Click += (s, e) => Close();
            Controls.Add(closeButton);
        }

        private async Task LoadAdaptersAsync()
        {
            try
            {
                SetBusy(true, "Loading Windows network adapters...");
                adapters = await Task.Run(() => adapterService.GetAdapters());
                adapterBox.Items.Clear();

                foreach (NetworkAdapterInfo adapter in adapters)
                    adapterBox.Items.Add(adapter);

                if (adapterBox.Items.Count > 0)
                    adapterBox.SelectedIndex = 0;
                else
                    previewBox.Text = "No Windows network adapters were detected.";
            }
            catch (Exception ex)
            {
                previewBox.Text = "Failed to detect network adapters.\r\n\r\n" + ex.Message;
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async Task RefreshPreviewAsync()
        {
            NetworkAdapterInfo adapter = SelectedAdapter;
            if (adapter == null)
                return;

            try
            {
                SetBusy(true, "Reading adapter profile...");
                string profileName = profileNameBox.Text.Trim();
                NetworkProfile profile = await Task.Run(() => adapterService.CaptureProfile(adapter.InterfaceAlias, profileName));
                previewBox.Text = FormatProfile(profile);
            }
            catch (Exception ex)
            {
                previewBox.Text = "Unable to read current adapter profile:\r\n\r\n" + ex.Message;
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async void saveButton_Click(object sender, EventArgs e)
        {
            NetworkAdapterInfo adapter = SelectedAdapter;
            if (adapter == null)
                return;

            try
            {
                SetBusy(true, "Saving current adapter profile...");
                string profileName = profileNameBox.Text.Trim();
                string profilePath = profilePathBox.Text.Trim();
                NetworkProfile profile = await Task.Run(() => adapterService.CaptureProfile(adapter.InterfaceAlias, profileName));
                profileManager.SaveProfile(profile, profilePathBox.Text.Trim());
                previewBox.Text = FormatProfile(profile);

                MessageBox.Show(this,
                    "Network adapter profile saved successfully.",
                    "Save Current Profile",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Failed to save network adapter profile.\r\n\r\n" + ex.Message,
                    "Save Current Profile",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async void dhcpButton_Click(object sender, EventArgs e)
        {
            NetworkAdapterInfo adapter = SelectedAdapter;
            if (adapter == null || !RequireAdministrator())
                return;

            if (MessageBox.Show(this,
                "Apply DHCP to adapter \"" + adapter.InterfaceAlias + "\"?\r\n\r\nOnly this selected adapter will be modified.",
                "Apply DHCP",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                SetBusy(true, "Applying DHCP...");
                await Task.Run(() => adapterService.ApplyDhcp(adapter.InterfaceAlias));
                MessageBox.Show(this,
                    "DHCP was applied successfully to " + adapter.InterfaceAlias + ".",
                    "Apply DHCP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                await RefreshPreviewAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Failed to apply DHCP.\r\n\r\n" + ex.Message,
                    "Apply DHCP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async void restoreButton_Click(object sender, EventArgs e)
        {
            if (!RequireAdministrator())
                return;

            try
            {
                NetworkProfile profile = profileManager.LoadProfile(profilePathBox.Text.Trim());
                NetworkAdapterInfo selected = SelectedAdapter;

                if (selected == null)
                    return;

                NetworkProfile restoreProfile = CopyProfileForAdapter(profile, selected.InterfaceAlias);

                if (!string.Equals(profile.InterfaceAlias, selected.InterfaceAlias, StringComparison.OrdinalIgnoreCase))
                {
                    if (MessageBox.Show(this,
                        "The saved profile is for adapter \"" + profile.InterfaceAlias + "\", but the selected adapter is \"" + selected.InterfaceAlias + "\".\r\n\r\nRestore the saved settings onto the selected adapter only?",
                        "Restore Saved Profile",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        return;
                    }
                }

                if (MessageBox.Show(this,
                    "Restore saved profile to selected adapter \"" + selected.InterfaceAlias + "\"?\r\n\r\nCurrent IPv4 addresses on that adapter will be removed and replaced.",
                    "Restore Saved Profile",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    return;
                }

                SetBusy(true, "Restoring saved adapter profile...");
                await Task.Run(() => adapterService.RestoreProfile(restoreProfile));
                previewBox.Text = FormatProfile(restoreProfile);
                MessageBox.Show(this,
                    "Network adapter profile restored successfully.",
                    "Restore Saved Profile",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                await LoadAdaptersAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Failed to restore saved profile.\r\n\r\n" + ex.Message,
                    "Restore Saved Profile",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void browseButton_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Network profile JSON";
                dialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                dialog.FileName = Path.GetFileName(profilePathBox.Text);
                dialog.InitialDirectory = Path.GetDirectoryName(profilePathBox.Text);
                dialog.OverwritePrompt = false;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    profilePathBox.Text = dialog.FileName;
            }
        }

        private bool RequireAdministrator()
        {
            if (adapterService.IsAdministrator())
                return true;

            MessageBox.Show(this,
                "Universal Scanner must be run as Administrator to change Windows network adapter settings.",
                "Administrator Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        private NetworkAdapterInfo SelectedAdapter
        {
            get { return adapterBox.SelectedItem as NetworkAdapterInfo; }
        }

        private static string GetDefaultProfilePath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "UniversalScanner");
            return Path.Combine(dir, "Camera Network Profile.json");
        }

        private static string FormatProfile(NetworkProfile profile)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ProfileName: " + profile.ProfileName);
            sb.AppendLine("InterfaceAlias: " + profile.InterfaceAlias);
            sb.AppendLine("DHCP: " + (profile.DhcpEnabled ? "Enabled" : "Disabled"));
            sb.AppendLine("InterfaceMetric: " + (profile.InterfaceMetric.HasValue ? profile.InterfaceMetric.Value.ToString() : "N/A"));
            sb.AppendLine("Gateway: " + (string.IsNullOrWhiteSpace(profile.Gateway) ? "N/A" : profile.Gateway));
            sb.AppendLine("DNS: " + (profile.DnsServers == null || profile.DnsServers.Count == 0 ? "N/A" : string.Join(", ", profile.DnsServers.ToArray())));
            sb.AppendLine();
            sb.AppendLine("IPv4 addresses:");

            if (profile.Addresses == null || profile.Addresses.Count == 0)
            {
                sb.AppendLine("  N/A");
            }
            else
            {
                foreach (NetworkProfileAddress address in profile.Addresses)
                    sb.AppendLine("  " + address.IPAddress + "  " + GetDisplaySubnetMask(address));
            }

            return sb.ToString();
        }

        private static NetworkProfile CopyProfileForAdapter(NetworkProfile profile, string interfaceAlias)
        {
            NetworkProfile copy = new NetworkProfile();
            copy.ProfileName = profile.ProfileName;
            copy.InterfaceAlias = interfaceAlias;
            copy.DhcpEnabled = profile.DhcpEnabled;
            copy.Gateway = profile.Gateway;
            copy.InterfaceMetric = profile.InterfaceMetric;

            if (profile.Addresses != null)
            {
                foreach (NetworkProfileAddress address in profile.Addresses)
                {
                    copy.Addresses.Add(new NetworkProfileAddress
                    {
                        IPAddress = address.IPAddress,
                        SubnetMask = address.SubnetMask,
                        PrefixLength = address.PrefixLength
                    });
                }
            }

            if (profile.DnsServers != null)
                copy.DnsServers.AddRange(profile.DnsServers);

            return copy;
        }

        private static string GetDisplaySubnetMask(NetworkProfileAddress address)
        {
            if (address == null)
                return "";

            if (!string.IsNullOrWhiteSpace(address.SubnetMask))
                return address.SubnetMask;

            return PrefixLengthToSubnetMask(address.PrefixLength);
        }

        private static string PrefixLengthToSubnetMask(int prefixLength)
        {
            if (prefixLength < 0 || prefixLength > 32)
                return "";

            uint mask = prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);
            return ((mask >> 24) & 255) + "." +
                   ((mask >> 16) & 255) + "." +
                   ((mask >> 8) & 255) + "." +
                   (mask & 255);
        }

        private void SetBusy(bool busy, string message)
        {
            adapterBox.Enabled = !busy;
            saveButton.Enabled = !busy;
            dhcpButton.Enabled = !busy;
            restoreButton.Enabled = !busy;
            browseButton.Enabled = !busy;

            if (!string.IsNullOrWhiteSpace(message))
                previewBox.Text = message;
        }

        private void AddLabel(string text, int left, int top)
        {
            Label label = new Label();
            label.Text = text;
            label.Left = left;
            label.Top = top + 3;
            label.Width = 125;
            Controls.Add(label);
        }
    }
}
