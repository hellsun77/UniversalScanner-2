using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace UniversalScanner
{
    public partial class TemporarySubnetManagerForm : Form
    {
        public TemporarySubnetManagerForm()
        {
            InitializeComponent();
            AppVisualTheme.Apply(this);
        }

        private void TemporarySubnetManagerForm_Load(object sender, EventArgs e)
        {
            chkAutoRemove.Checked = Config.getInstance().AutoRemoveTempIps;
            RefreshList();
        }

        private void RefreshList()
        {
            listView.Items.Clear();
            foreach (var record in TemporarySubnetService.Instance.ActiveRecords)
            {
                var item = new ListViewItem(record.AdapterName);
                item.SubItems.Add(record.TemporaryIp);
                item.SubItems.Add(record.SubnetMask);
                item.SubItems.Add(record.CameraIp);
                item.SubItems.Add(record.Status);
                item.SubItems.Add(record.CreatedTime.ToString("g"));
                item.Tag = record;
                listView.Items.Add(item);
            }
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            RefreshList();
        }

        private async void btnRemoveSelected_Click(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 0) return;

            btnRemoveSelected.Enabled = false;
            btnRemoveAll.Enabled = false;

            foreach (ListViewItem item in listView.SelectedItems)
            {
                var record = (TemporaryIpRecord)item.Tag;
                record.Status = "Removing...";
                item.SubItems[4].Text = record.Status;
                
                if (System.Net.IPAddress.TryParse(record.TemporaryIp, out var rIp))
                {
                    bool success = await TemporarySubnetService.Instance.RemoveTemporaryIpAsync(record.AdapterName, rIp);
                    if (success)
                    {
                        TemporarySubnetService.Instance.ActiveRecords.Remove(record);
                    }
                    else
                    {
                        record.Status = "Remove Failed";
                        item.SubItems[4].Text = record.Status;
                    }
                }
                else
                {
                    record.Status = "Invalid IP";
                    item.SubItems[4].Text = record.Status;
                }
            }

            RefreshList();
            btnRemoveSelected.Enabled = true;
            btnRemoveAll.Enabled = true;
        }

        private async void btnRemoveAll_Click(object sender, EventArgs e)
        {
            if (TemporarySubnetService.Instance.ActiveRecords.Count == 0) return;

            btnRemoveSelected.Enabled = false;
            btnRemoveAll.Enabled = false;

            var records = TemporarySubnetService.Instance.ActiveRecords.ToList();
            foreach (var record in records)
            {
                if (System.Net.IPAddress.TryParse(record.TemporaryIp, out var rIp))
                {
                    bool success = await TemporarySubnetService.Instance.RemoveTemporaryIpAsync(record.AdapterName, rIp);
                    if (success)
                    {
                        TemporarySubnetService.Instance.ActiveRecords.Remove(record);
                    }
                }
            }

            RefreshList();
            btnRemoveSelected.Enabled = true;
            btnRemoveAll.Enabled = true;
        }

        private void chkAutoRemove_CheckedChanged(object sender, EventArgs e)
        {
            Config.getInstance().AutoRemoveTempIps = chkAutoRemove.Checked;
        }
    }
}
