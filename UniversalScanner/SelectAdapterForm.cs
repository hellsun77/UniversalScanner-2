using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows.Forms;

namespace UniversalScanner
{
    public class SelectAdapterForm : Form
    {
        public string SelectedAdapterName { get; private set; }
        private ComboBox cmbAdapters;
        private Button btnOk;
        private Button btnCancel;

        private class AdapterItem
        {
            public NetworkInterface Adapter { get; set; }
            public override string ToString()
            {
                var ips = new System.Collections.Generic.List<string>();
                foreach (var ip in Adapter.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        ips.Add(ip.Address.ToString());
                    }
                }
                string ipStr = ips.Count > 0 ? string.Join(", ", ips) : "No IPv4";
                return $"{Adapter.Name} - {Adapter.NetworkInterfaceType} - {(Adapter.OperationalStatus == OperationalStatus.Up ? "Up" : "Down")} ({ipStr})";
            }
        }

        public SelectAdapterForm()
        {
            this.Text = "Select Network Adapter";
            this.Size = new Size(500, 150);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            Label lbl = new Label() { Text = "Please select a network adapter to apply the temporary IP:", Location = new Point(15, 15), AutoSize = true };
            cmbAdapters = new ComboBox() { Location = new Point(15, 40), Width = 450, DropDownStyle = ComboBoxStyle.DropDownList };
            
            List<NetworkInterface> adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .OrderBy(ni => ni.OperationalStatus == OperationalStatus.Up ? 0 : 1)
                .ThenBy(ni => NetworkTools.IsLikelyVirtualAdapter(ni) ? 1 : 0)
                .ThenBy(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1)
                .ThenBy(ni => ni.Name)
                .ToList();

            foreach (var ni in adapters)
            {
                cmbAdapters.Items.Add(new AdapterItem { Adapter = ni });
            }
            if (cmbAdapters.Items.Count > 0) cmbAdapters.SelectedIndex = 0;

            btnOk = new Button() { Text = "OK", Location = new Point(300, 75), DialogResult = DialogResult.None };
            btnOk.Click += BtnOk_Click;
            btnCancel = new Button() { Text = "Cancel", Location = new Point(390, 75), DialogResult = DialogResult.Cancel };

            this.Controls.Add(lbl);
            this.Controls.Add(cmbAdapters);
            this.Controls.Add(btnOk);
            this.Controls.Add(btnCancel);
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
            AppVisualTheme.Apply(this);
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (cmbAdapters.SelectedItem is AdapterItem item)
            {
                if (item.Adapter.OperationalStatus != OperationalStatus.Up)
                {
                    var res = MessageBox.Show(this, 
                        "The selected adapter is currently disconnected. Windows may accept a temporary IP address, but traffic cannot reach the camera until the physical link becomes active.\n\nDo you want to continue?", 
                        "Adapter Disconnected", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                    
                    if (res != DialogResult.OK)
                    {
                        return; // Prevent closing
                    }
                }
                SelectedAdapterName = item.Adapter.Name;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                MessageBox.Show(this, "Please select an adapter.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
