using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using UniversalScanner.DeviceSDK.ONVIF;

namespace UniversalScanner.Forms
{
    public partial class OnvifBatchDateTimeConfigurationForm : Form
    {
        private readonly List<OnvifBatchCameraItem> _cameras;
        private CancellationTokenSource _cts;
        private Stopwatch _batchStopwatch;
        private DateTime _batchStartUtc;
        private DateTime _batchStartLocal;
        private DateTime _batchUiSelectedUtc;
        private DateTime _batchUiSelectedLocal;

        // Persist preferences (static)
        private static bool _prefLoaded = false;
        private static BatchRequestedMode _prefMode = BatchRequestedMode.ManualAdvancing;
        private static string _prefTimeZone = "";
        private static string _prefNtpServer = "";
        private static int _prefNtpType = 0;
        private static bool _prefDst = false;

        public OnvifBatchDateTimeConfigurationForm(List<(string Ip, int Port, string Vendor, string Model)> devices)
        {
            InitializeComponent();
            _cameras = devices.Select(d => new OnvifBatchCameraItem
            {
                IpAddress = d.Ip,
                Port = d.Port,
                Vendor = d.Vendor,
                Model = d.Model
            }).ToList();
            UniversalScanner.AppVisualTheme.Apply(this);
        }

        private void OnvifBatchDateTimeConfigurationForm_Load(object sender, EventArgs e)
        {
            comboTimeZone.Items.AddRange(OnvifTimeZoneHelper.GetFriendlyNames().ToArray());

            if (_prefLoaded)
            {
                if (_prefMode == BatchRequestedMode.SyncToComputer) radioSyncComputer.Checked = true;
                else if (_prefMode == BatchRequestedMode.ManualFixed) radioManualFixed.Checked = true;
                else if (_prefMode == BatchRequestedMode.ManualAdvancing) radioManualAdvancing.Checked = true;
                else if (_prefMode == BatchRequestedMode.NTP) radioNTP.Checked = true;

                if (!string.IsNullOrEmpty(_prefTimeZone) && comboTimeZone.Items.Contains(_prefTimeZone))
                    comboTimeZone.SelectedItem = _prefTimeZone;

                textNtpServer.Text = _prefNtpServer;
                if (_prefNtpType >= 0 && _prefNtpType < comboNtpAddressType.Items.Count)
                    comboNtpAddressType.SelectedIndex = _prefNtpType;
                    
                checkDaylightSavings.Checked = _prefDst;
            }
            else
            {
                comboNtpAddressType.SelectedIndex = 0;
            }

            syncMode_CheckedChanged(null, null);

            foreach (var cam in _cameras)
            {
                dataGridViewCameras.Rows.Add(
                    cam.Status.ToString(),
                    cam.IpAddress,
                    cam.Vendor,
                    cam.Model,
                    "", "", "", "", "", ""
                );
            }

            // Fire and forget initial read
            _ = PerformInitialReadAsync();
        }

        private void syncMode_CheckedChanged(object sender, EventArgs e)
        {
            groupBoxManual.Enabled = radioManualAdvancing.Checked || radioManualFixed.Checked;
            groupBoxNTP.Enabled = radioNTP.Checked;
        }

        private async Task PerformInitialReadAsync()
        {
            SetUiState(true);
            labelProgress.Text = "Reading initial camera times...";

            UniversalLoginManager.GetUniversalCredentials(out string username, out string password);

            using (var semaphore = new SemaphoreSlim(5))
            {
                var tasks = _cameras.Select(async (cam, index) =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        UpdateGrid(index, "Connecting", colName: "ColStatus");
                        using (var client = new OnvifSoapClient())
                        {
                            var service = new OnvifDateTimeService(client);
                            var config = await service.GetConfigurationAsync(cam.IpAddress, cam.Port, username, password, CancellationToken.None);

                            if (!config.IsSupported || !config.IsWriteSupported)
                            {
                                cam.Status = BatchCameraStatus.Unsupported;
                                UpdateGrid(index, "Unsupported", colName: "ColStatus");
                                UpdateGrid(index, "Camera does not support ONVIF Date/Time.", colName: "ColError");
                            }
                            else
                            {
                                cam.Status = BatchCameraStatus.Pending;
                                UpdateGrid(index, "Pending", colName: "ColStatus");
                                
                                string timeStr = config.LocalTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown";
                                if (config.DateTimeType == OnvifDateTimeType.NTP) timeStr += " (NTP)";
                                else timeStr += " (Manual)";
                                
                                UpdateGrid(index, timeStr, colName: "ColCurrentTime");
                            }
                        }
                    }
                    catch
                    {
                        cam.Status = BatchCameraStatus.Pending;
                        UpdateGrid(index, "Pending", colName: "ColStatus");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            }

            UpdateSummary();
            SetUiState(false);
            labelProgress.Text = "Ready to start batch configuration. Select mode and click Start.";
        }

        private void SetUiState(bool isRunning)
        {
            buttonStart.Enabled = !isRunning;
            buttonCancel.Enabled = isRunning;
            buttonSelectFailed.Enabled = !isRunning;
            buttonExport.Enabled = !isRunning;
            groupBoxMode.Enabled = !isRunning;
            groupBoxManual.Enabled = !isRunning && (radioManualAdvancing.Checked || radioManualFixed.Checked);
            groupBoxNTP.Enabled = !isRunning && radioNTP.Checked;
        }

        private BatchRequestedMode GetSelectedMode()
        {
            if (radioSyncComputer.Checked) return BatchRequestedMode.SyncToComputer;
            if (radioManualFixed.Checked) return BatchRequestedMode.ManualFixed;
            if (radioManualAdvancing.Checked) return BatchRequestedMode.ManualAdvancing;
            return BatchRequestedMode.NTP;
        }

        private void SavePreferences()
        {
            _prefMode = GetSelectedMode();
            _prefTimeZone = comboTimeZone.SelectedItem?.ToString() ?? "";
            _prefNtpServer = textNtpServer.Text.Trim();
            _prefNtpType = comboNtpAddressType.SelectedIndex;
            _prefDst = checkDaylightSavings.Checked;
            _prefLoaded = true;
        }

        private async void buttonStart_Click(object sender, EventArgs e)
        {
            var mode = GetSelectedMode();

            if ((mode == BatchRequestedMode.ManualAdvancing || mode == BatchRequestedMode.ManualFixed) && comboTimeZone.SelectedItem == null)
            {
                MessageBox.Show(this, "Please select a Time Zone.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (mode == BatchRequestedMode.NTP && string.IsNullOrWhiteSpace(textNtpServer.Text))
            {
                MessageBox.Show(this, "Please enter an NTP Server.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!UniversalLoginManager.HasUniversalCredentials())
            {
                using (var loginForm = new UniversalLoginForm())
                {
                    if (loginForm.ShowDialog(this) != DialogResult.OK)
                        return;
                }
            }

            SavePreferences();
            UniversalLoginManager.GetUniversalCredentials(out string username, out string password);

            _cts = new CancellationTokenSource();
            SetUiState(true);

            _batchStartUtc = DateTime.UtcNow;
            _batchStartLocal = DateTime.Now;
            
            if (mode == BatchRequestedMode.ManualFixed || mode == BatchRequestedMode.ManualAdvancing)
            {
                var localTime = dateTimePickerDate.Value.Date + dateTimePickerTime.Value.TimeOfDay;
                string friendlyTz = comboTimeZone.SelectedItem.ToString();
                try
                {
                    var tzInfo = TimeZoneInfo.FindSystemTimeZoneById(friendlyTz);
                    _batchUiSelectedUtc = TimeZoneInfo.ConvertTimeToUtc(localTime, tzInfo);
                }
                catch
                {
                    var pcOffset = TimeZoneInfo.Local.GetUtcOffset(localTime);
                    _batchUiSelectedUtc = localTime - pcOffset;
                }
                _batchUiSelectedLocal = localTime;
            }

            _batchStopwatch = Stopwatch.StartNew();

            int completed = 0;
            int total = _cameras.Count(c => c.Status != BatchCameraStatus.Success && c.Status != BatchCameraStatus.Unsupported);
            
            progressBar.Maximum = total > 0 ? total : 1;
            progressBar.Value = 0;

            using (var semaphore = new SemaphoreSlim(5))
            {
                var tasks = _cameras.Select(async (cam, index) =>
                {
                    if (cam.Status == BatchCameraStatus.Success || cam.Status == BatchCameraStatus.Unsupported)
                        return;

                    await semaphore.WaitAsync();
                    try
                    {
                        if (_cts.Token.IsCancellationRequested)
                        {
                            if (cam.Status != BatchCameraStatus.Success && cam.Status != BatchCameraStatus.Failed)
                            {
                                cam.Status = BatchCameraStatus.Cancelled;
                                UpdateGrid(index, "Cancelled", "ColStatus");
                            }
                            return;
                        }

                        cam.RequestedMode = mode;
                        UpdateGrid(index, mode.ToString(), "ColRequestedMode");
                        UpdateGrid(index, "Applying", "ColStatus");
                        cam.Status = BatchCameraStatus.Applying;

                        using (var client = new OnvifSoapClient())
                        {
                            var service = new OnvifDateTimeService(client);
                            
                            string friendlyTz = comboTimeZone.SelectedItem?.ToString() ?? "";
                            string posixTz = OnvifTimeZoneHelper.GetPosixFromFriendly(friendlyTz);
                            bool dst = checkDaylightSavings.Checked;

                            var configBefore = await service.GetConfigurationAsync(cam.IpAddress, cam.Port, username, password, _cts.Token);
                            cam.BeforeUtc = configBefore.UtcTime;

                            if (mode == BatchRequestedMode.NTP)
                            {
                                var ntpSettings = new OnvifNtpDateTimeSettings
                                {
                                    NtpServers = new List<string> { textNtpServer.Text.Trim() },
                                    TimeZone = posixTz,
                                    DaylightSavings = dst,
                                    IsHikvisionWorkaround = !string.IsNullOrEmpty(cam.Vendor) && (cam.Vendor.IndexOf("Hikvision", StringComparison.OrdinalIgnoreCase) >= 0 || cam.Vendor.IndexOf("Honeywell", StringComparison.OrdinalIgnoreCase) >= 0)
                                };
                                await service.SetNtpDateTimeAsync(cam.IpAddress, cam.Port, username, password, ntpSettings, _cts.Token);
                                
                                UpdateGrid(index, "Verifying", "ColStatus");
                                cam.Status = BatchCameraStatus.Verifying;

                                var configAfter = await service.GetConfigurationAsync(cam.IpAddress, cam.Port, username, password, _cts.Token);
                                cam.AfterUtc = configAfter.UtcTime;
                                
                                bool typeVerified = configAfter.DateTimeType == OnvifDateTimeType.NTP;
                                bool serverVerified = configAfter.NtpServers.Any(s => string.Equals(s, ntpSettings.NtpServers[0], StringComparison.OrdinalIgnoreCase));

                                if (typeVerified && serverVerified)
                                {
                                    cam.Status = BatchCameraStatus.Success;
                                    cam.Result = "Success";
                                    UpdateGrid(index, "Success", "ColStatus");
                                    UpdateGrid(index, "Success", "ColResult");
                                }
                                else
                                {
                                    cam.Status = BatchCameraStatus.Failed;
                                    cam.Result = "Verify Failed";
                                    UpdateGrid(index, "Failed", "ColStatus");
                                    UpdateGrid(index, "Verify Failed", "ColResult");
                                    UpdateGrid(index, "NTP read-back mismatched.", "ColError");
                                }
                            }
                            else
                            {
                                DateTime targetUtc;
                                DateTime targetLocal;
                                if (mode == BatchRequestedMode.SyncToComputer)
                                {
                                    targetUtc = _batchStartUtc + _batchStopwatch.Elapsed;
                                    targetLocal = _batchStartLocal + _batchStopwatch.Elapsed;
                                }
                                else if (mode == BatchRequestedMode.ManualAdvancing)
                                {
                                    targetUtc = _batchUiSelectedUtc + _batchStopwatch.Elapsed;
                                    targetLocal = _batchUiSelectedLocal + _batchStopwatch.Elapsed;
                                }
                                else // ManualFixed
                                {
                                    targetUtc = _batchUiSelectedUtc;
                                    targetLocal = _batchUiSelectedLocal;
                                }

                                var manualSettings = new OnvifManualDateTimeSettings
                                {
                                    UtcTime = targetUtc,
                                    LocalTime = targetLocal,
                                    TimeZone = posixTz,
                                    DaylightSavings = dst,
                                    IsHikvisionWorkaround = !string.IsNullOrEmpty(cam.Vendor) && (cam.Vendor.IndexOf("Hikvision", StringComparison.OrdinalIgnoreCase) >= 0 || cam.Vendor.IndexOf("Honeywell", StringComparison.OrdinalIgnoreCase) >= 0)
                                };

                                await service.SetManualDateTimeAsync(cam.IpAddress, cam.Port, username, password, manualSettings, _cts.Token);

                                UpdateGrid(index, "Verifying", "ColStatus");
                                cam.Status = BatchCameraStatus.Verifying;

                                var configAfter = await service.GetConfigurationAsync(cam.IpAddress, cam.Port, username, password, _cts.Token);
                                cam.AfterUtc = configAfter.UtcTime;

                                if (configAfter.UtcTime.HasValue)
                                {
                                    double diff = Math.Abs((configAfter.UtcTime.Value - targetUtc).TotalSeconds);
                                    cam.DifferenceStr = $"{(int)diff}s";
                                    cam.VerifiedTimeStr = configAfter.UtcTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                                    UpdateGrid(index, cam.DifferenceStr, "ColDifference");
                                    UpdateGrid(index, cam.VerifiedTimeStr, "ColVerifiedTime");

                                    if (diff <= 10)
                                    {
                                        cam.Status = BatchCameraStatus.Success;
                                        cam.Result = "Success";
                                        UpdateGrid(index, "Success", "ColStatus");
                                        UpdateGrid(index, "Success", "ColResult");
                                    }
                                    else
                                    {
                                        cam.Status = BatchCameraStatus.Failed;
                                        cam.Result = "Verify Failed";
                                        UpdateGrid(index, "Failed", "ColStatus");
                                        UpdateGrid(index, "Verify Failed", "ColResult");
                                        UpdateGrid(index, $"Difference {(int)diff}s > 10s", "ColError");
                                    }
                                }
                                else
                                {
                                    cam.Status = BatchCameraStatus.Failed;
                                    cam.Result = "Verify Failed";
                                    UpdateGrid(index, "Failed", "ColStatus");
                                    UpdateGrid(index, "Verify Failed", "ColResult");
                                    UpdateGrid(index, "Failed to read UTC time back.", "ColError");
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        cam.Status = BatchCameraStatus.Cancelled;
                        UpdateGrid(index, "Cancelled", "ColStatus");
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized") || ex.Message.Contains("Security"))
                            cam.Status = BatchCameraStatus.AuthenticationFailed;
                        else if (ex.Message.Contains("Timeout"))
                            cam.Status = BatchCameraStatus.Timeout;
                        else
                            cam.Status = BatchCameraStatus.Failed;
                            
                        cam.Error = ex.Message;
                        UpdateGrid(index, cam.Status.ToString(), "ColStatus");
                        UpdateGrid(index, cam.Error, "ColError");
                    }
                    finally
                    {
                        int c = Interlocked.Increment(ref completed);
                        this.BeginInvoke((Action)(() =>
                        {
                            if (c <= progressBar.Maximum) progressBar.Value = c;
                            labelProgress.Text = $"Completed: {c} / {total}";
                            UpdateSummary();
                        }));
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            }

            _batchStopwatch.Stop();
            SetUiState(false);
            labelProgress.Text = "Batch operation completed.";
            UpdateSummary();
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            _cts?.Cancel();
            buttonCancel.Enabled = false;
            labelProgress.Text = "Cancelling... Please wait.";
        }

        private void UpdateGrid(int rowIndex, string value, string colName)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke((Action)(() => UpdateGrid(rowIndex, value, colName)));
                return;
            }
            if (rowIndex >= 0 && rowIndex < dataGridViewCameras.Rows.Count)
            {
                dataGridViewCameras.Rows[rowIndex].Cells[colName].Value = value;
            }
        }

        private void UpdateSummary()
        {
            int total = _cameras.Count;
            int success = _cameras.Count(c => c.Status == BatchCameraStatus.Success);
            int failed = _cameras.Count(c => c.Status == BatchCameraStatus.Failed);
            int authFailed = _cameras.Count(c => c.Status == BatchCameraStatus.AuthenticationFailed);
            int timeout = _cameras.Count(c => c.Status == BatchCameraStatus.Timeout);
            int unsupported = _cameras.Count(c => c.Status == BatchCameraStatus.Unsupported);
            int cancelled = _cameras.Count(c => c.Status == BatchCameraStatus.Cancelled);

            double avgDiff = 0;
            var diffs = _cameras.Where(c => c.Status == BatchCameraStatus.Success && c.DifferenceStr.EndsWith("s")).ToList();
            if (diffs.Count > 0)
            {
                avgDiff = diffs.Average(c => int.Parse(c.DifferenceStr.TrimEnd('s')));
            }

            string duration = _batchStopwatch != null ? $"Duration: {_batchStopwatch.Elapsed.ToString(@"hh\:mm\:ss")} | " : "";

            labelSummary.Text = $"Total: {total}\r\nSuccess: {success}\r\nFailed: {failed}\r\nAuth Failed: {authFailed}\r\nTimeout: {timeout}\r\nUnsupported: {unsupported}\r\nCancelled: {cancelled}\r\n{duration}Avg Diff: {avgDiff:F1}s";
        }

        public List<string> GetFailedIps()
        {
            return _cameras.Where(c => 
                c.Status == BatchCameraStatus.Failed || 
                c.Status == BatchCameraStatus.AuthenticationFailed || 
                c.Status == BatchCameraStatus.Timeout)
                .Select(c => c.IpAddress).ToList();
        }

        private void buttonSelectFailed_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            Close();
        }

        private void buttonClose_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            Close();
        }

        private void buttonExport_Click(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "CSV Files|*.csv";
                sfd.Title = "Export Batch Results";
                sfd.FileName = "ONVIF_DateTime_Batch_Results.csv";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("IP Address,Vendor,Model,Requested Mode,Status,Camera Time Before (UTC),Camera Time After (UTC),Difference,NTP Server,Error");
                        foreach (var c in _cameras)
                        {
                            sb.AppendLine($"{c.IpAddress},{c.Vendor},{c.Model},{c.RequestedMode},{c.Status},{c.BeforeUtc},{c.AfterUtc},{c.DifferenceStr},{c.NtpServer},\"{c.Error?.Replace("\"", "\"\"")}\"");
                        }
                        File.WriteAllText(sfd.FileName, sb.ToString());
                        MessageBox.Show(this, "Export complete.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Export failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
    }
}
