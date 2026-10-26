using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UniversalScanner
{
    public partial class ScannerWindow
    {
        private class CameraBatchItem
        {
            public DataGridViewRow Row { get; set; }
            public DataRow DataRow { get; set; }
            public string OldIp { get; set; }
            public string NewIp { get; set; }
            public string OldSubnetMask { get; set; }
            public string NewSubnetMask { get; set; }
            public string OldSubnetKey { get; set; }
            public string NewSubnetKey { get; set; }
            public string BrandCategory { get; set; }
            public string Brand { get; set; }
            public string Model { get; set; }
            public bool IsSuccess { get; set; }
            public bool IsVerified { get; set; }
        }

        private async void batchChangeIpAcrossSubnetsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<DataGridViewRow> rows = GetSelectedDeviceRows();

            if (rows.Count == 0)
            {
                MessageBox.Show(this, "Select one or more devices first.", "Batch Change IP Across Subnets", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ApplyAiBrandOverridesToSelectedRowsForAction();
            rows = GetSelectedDeviceRows();
            if (rows.Count == 0)
                return;

            if (rows.Any(row => IsDahuaSwitchRow(GetDataRowFromGridRow(row))))
                return;

            bool batchIsBoschOnly = rows.All(row => IsBoschRow(GetDataRowFromGridRow(row)));
            bool batchIsAxisOnly = rows.All(row => IsAxisRow(GetDataRowFromGridRow(row)));
            string defaultUsername = batchIsBoschOnly ? "service" : batchIsAxisOnly ? "root" : "admin";

            using (BatchChangeIpForm form = new BatchChangeIpForm(rows.Count, defaultUsername))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                    return;

                if (!IsValidIPv4Text(form.StartIPAddress) ||
                    !IsValidIPv4Text(form.SubnetMask) ||
                    !IsValidIPv4Text(form.Gateway))
                {
                    MessageBox.Show(this, "Start IP, subnet mask, or gateway is not valid.", "Batch Change IP Across Subnets", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                DialogResult confirm = MessageBox.Show(this,
                    "Batch change IP across subnets for " + rows.Count + " selected device(s)?\r\n\r\n" +
                    "First IP: " + form.StartIPAddress + "\r\n" +
                    "Last IP:  " + IncrementIPv4(form.StartIPAddress, rows.Count - 1),
                    "Batch Change IP Across Subnets",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (confirm != DialogResult.Yes)
                    return;

                string adapterAlias = "";
                using (var adapterForm = new SelectAdapterForm())
                {
                    if (adapterForm.ShowDialog(this) == DialogResult.OK)
                        adapterAlias = adapterForm.SelectedAdapterName;
                    else
                        return;
                }

                var progressForm = new BatchChangeIpAcrossSubnetProgressForm();
                progressForm.Show(this);

                await RunBatchChangeIpAcrossSubnetsAsync(rows, form, adapterAlias, progressForm);
                
                if (!progressForm.IsDisposed)
                    progressForm.Finish();
            }
        }

        private async Task RunBatchChangeIpAcrossSubnetsAsync(List<DataGridViewRow> rows, BatchChangeIpForm form, string adapterAlias, BatchChangeIpAcrossSubnetProgressForm progressForm)
        {
            try
            {
                var items = new List<CameraBatchItem>();
                int totalSuccess = 0;
                int totalFailed = 0;
                int totalSkipped = 0;

                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    var dataRow = GetDataRowFromGridRow(row);
                    string oldIp = row.Cells[(int)Columns.IPAddress].Value?.ToString() ?? "";
                    string newIp = IncrementIPv4(form.StartIPAddress, i);
                    string oldMask = "255.255.255.0";
                    string brand = row.Cells[(int)Columns.Vendor].Value?.ToString() ?? "";
                    string model = row.Cells[(int)Columns.Type].Value?.ToString() ?? "";

                    oldMask = TemporarySubnetService.Instance.DetermineSubnetMask(oldIp, oldMask);
                    
                    string oldSubnetKey = GetNetworkAddress(oldIp, oldMask) + "/" + oldMask;
                    string newSubnetKey = GetNetworkAddress(newIp, form.SubnetMask) + "/" + form.SubnetMask;

                    string brandCategory = GetBrandCategory(dataRow);

                    items.Add(new CameraBatchItem
                    {
                        Row = row,
                        DataRow = dataRow,
                        OldIp = oldIp,
                        NewIp = newIp,
                        OldSubnetMask = oldMask,
                        NewSubnetMask = form.SubnetMask,
                        OldSubnetKey = oldSubnetKey,
                        NewSubnetKey = newSubnetKey,
                        BrandCategory = brandCategory,
                        Brand = brand,
                        Model = model
                    });
                }

                var subnetGroups = items.GroupBy(x => x.OldSubnetKey).ToList();

                foreach (var subnetGroup in subnetGroups)
                {
                    if (progressForm.IsCancelled) break;

                    string oldSubnetKey = subnetGroup.Key;
                    var firstItem = subnetGroup.First();
                    
                    progressForm.UpdateProgress(oldSubnetKey, "-", "-", totalSuccess, totalFailed, totalSkipped, "Injecting temporary IP for source subnet...");

                    var oldIpObj = IPAddress.Parse(firstItem.OldIp);
                    var oldMaskObj = IPAddress.Parse(firstItem.OldSubnetMask);
                    
                    var oldTempIp = TemporarySubnetService.Instance.FindAvailableTemporaryIp(oldIpObj, oldMaskObj);
                    bool addSuccess = false;
                    if (oldTempIp != null)
                    {
                        var addResult = await TemporarySubnetService.Instance.AddTemporaryIpAsync(adapterAlias, oldTempIp, oldMaskObj, oldIpObj);
                        addSuccess = addResult.Success;
                        if (addSuccess)
                        {
                            await Task.Delay(500);
                            await TemporarySubnetService.Instance.ValidateAndConfigureRouteAsync(adapterAlias, oldTempIp, oldIpObj);
                            await Task.Delay(500);
                        }
                    }

                    if (!addSuccess)
                    {
                        progressForm.UpdateProgress(oldSubnetKey, "-", "-", totalSuccess, totalFailed, totalSkipped, "Failed to inject temporary IP. Skipping subnet group.");
                        foreach (var item in subnetGroup)
                        {
                            item.Row.Cells[(int)Columns.Ping].Value = "Temp IP failed";
                            totalSkipped++;
                        }
                        continue;
                    }

                    bool subnetHasFailure = false;

                    var brandGroups = subnetGroup.GroupBy(x => x.BrandCategory).ToList();
                    foreach (var brandGroup in brandGroups)
                    {
                        if (progressForm.IsCancelled) break;

                        string brandCategory = brandGroup.Key;
                        progressForm.UpdateProgress(oldSubnetKey, brandCategory, "-", totalSuccess, totalFailed, totalSkipped, "Processing cameras...");

                        foreach (var item in brandGroup)
                        {
                            if (progressForm.IsCancelled) break;

                            progressForm.UpdateProgress(oldSubnetKey, brandCategory, item.OldIp, totalSuccess, totalFailed, totalSkipped, "Testing reachability...");
                            
                            bool reachable = await TemporarySubnetService.Instance.IsCameraReachableAsync(IPAddress.Parse(item.OldIp), oldTempIp, adapterAlias);
                            if (!reachable)
                            {
                                item.Row.Cells[(int)Columns.Ping].Value = "Not reachable";
                                totalFailed++;
                                subnetHasFailure = true;
                                continue;
                            }

                            progressForm.UpdateProgress(oldSubnetKey, brandCategory, item.OldIp, totalSuccess, totalFailed, totalSkipped, "Changing IP...");
                            item.Row.Cells[(int)Columns.Ping].Value = "Changing IP...";
                            
                            var changeResult = await ChangeIpForm.ExecuteChangeIpCoreAsync(
                                item.Brand, item.Model,
                                item.OldIp, form.Username, form.Password,
                                item.NewIp, form.SubnetMask, form.Gateway,
                                false,
                                status => { }
                            );

                            if (changeResult.Success)
                            {
                                item.IsSuccess = true;
                                item.Row.Cells[(int)Columns.Ping].Value = "Rebooting...";
                            }
                            else
                            {
                                item.Row.Cells[(int)Columns.Ping].Value = "Change IP failed";
                                totalFailed++;
                                subnetHasFailure = true;
                            }
                        }
                    }

                    if (progressForm.IsCancelled) break;

                    var newSubnetGroups = subnetGroup.Where(x => x.IsSuccess).GroupBy(x => x.NewSubnetKey).ToList();
                    
                    if (newSubnetGroups.Count > 0)
                    {
                        await Task.Delay(5000); // Give cameras a few seconds to start rebooting
                    }

                    foreach (var newSubnetGroup in newSubnetGroups)
                    {
                        if (progressForm.IsCancelled) break;

                        string newSubnetKey = newSubnetGroup.Key;
                        var firstNewItem = newSubnetGroup.First();
                        
                        progressForm.UpdateProgress(oldSubnetKey, "Verification", newSubnetKey, totalSuccess, totalFailed, totalSkipped, "Injecting temporary IP for target subnet...");

                        var newIpObj = IPAddress.Parse(firstNewItem.NewIp);
                        var newMaskObj = IPAddress.Parse(firstNewItem.NewSubnetMask);
                        
                        var newTempIp = TemporarySubnetService.Instance.FindAvailableTemporaryIp(newIpObj, newMaskObj);
                        TemporarySubnetService.AddIpResult newAddResult = null;
                        
                        if (newTempIp != null)
                        {
                            newAddResult = await TemporarySubnetService.Instance.AddTemporaryIpAsync(adapterAlias, newTempIp, newMaskObj, newIpObj);
                            if (newAddResult.Success)
                            {
                                await Task.Delay(500);
                            }
                        }

                        foreach (var item in newSubnetGroup)
                        {
                            if (progressForm.IsCancelled) break;
                            
                            progressForm.UpdateProgress(oldSubnetKey, "Verification", item.NewIp, totalSuccess, totalFailed, totalSkipped, "Verifying new IP...");
                            
                            bool verified = false;
                            var targetIpObj = IPAddress.Parse(item.NewIp);
                            for (int tryIndex = 0; tryIndex < 15; tryIndex++)
                            {
                                await Task.Delay(1000);
                                if (await TemporarySubnetService.Instance.IsCameraReachableAsync(targetIpObj, newTempIp, adapterAlias))
                                {
                                    verified = true;
                                    break;
                                }
                            }

                            if (verified)
                            {
                                item.IsVerified = true;
                                item.Row.Cells[(int)Columns.Ping].Value = "Success";
                                totalSuccess++;
                            }
                            else
                            {
                                item.Row.Cells[(int)Columns.Ping].Value = "Verification failed";
                                totalFailed++;
                                subnetHasFailure = true;
                            }
                        }

                        if (Config.getInstance().AutoRemoveTempIps && !subnetHasFailure && newAddResult != null && newAddResult.Success && newTempIp != null)
                        {
                            progressForm.UpdateProgress(oldSubnetKey, "Verification", "-", totalSuccess, totalFailed, totalSkipped, "Cleaning up target subnet temp IP...");
                            await TemporarySubnetService.Instance.RemoveTemporaryIpAsync(adapterAlias, newTempIp, newIpObj, null);
                        }
                    }

                    if (Config.getInstance().AutoRemoveTempIps && !subnetHasFailure && oldTempIp != null)
                    {
                        progressForm.UpdateProgress(oldSubnetKey, "Cleanup", "-", totalSuccess, totalFailed, totalSkipped, "Cleaning up source subnet temp IP...");
                        await TemporarySubnetService.Instance.RemoveTemporaryIpAsync(adapterAlias, oldTempIp, oldIpObj, null);
                    }
                }

                progressForm.UpdateProgress("-", "-", "-", totalSuccess, totalFailed, totalSkipped, progressForm.IsCancelled ? "Cancelled by user." : "Finished.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "An error occurred during batch operation:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                progressForm.UpdateProgress("-", "-", "-", 0, 0, 0, "Error: " + ex.Message);
            }
        }

        private string GetBrandCategory(DataRow dataRow)
        {
            if (IsHikvisionRow(dataRow)) return "Hikvision";
            if (IsDahuaRow(dataRow)) return "Dahua";
            if (IsUniviewRow(dataRow)) return "Uniview";
            if (IsHoneywellRow(dataRow)) return "Honeywell";
            
            string protocol = dataRow[(int)Columns.Protocol]?.ToString() ?? "";
            if (protocol.IndexOf("ONVIF", StringComparison.OrdinalIgnoreCase) >= 0) return "Generic ONVIF";
            
            return "Unknown";
        }

        private string GetNetworkAddress(string ipAddress, string subnetMask)
        {
            if (!IPAddress.TryParse(ipAddress, out IPAddress ip) || !IPAddress.TryParse(subnetMask, out IPAddress mask))
                return "0.0.0.0";

            byte[] ipBytes = ip.GetAddressBytes();
            byte[] maskBytes = mask.GetAddressBytes();

            if (ipBytes.Length != maskBytes.Length)
                return "0.0.0.0";

            byte[] networkBytes = new byte[ipBytes.Length];
            for (int i = 0; i < networkBytes.Length; i++)
            {
                networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
            }
            
            return new IPAddress(networkBytes).ToString();
        }
    }
}
