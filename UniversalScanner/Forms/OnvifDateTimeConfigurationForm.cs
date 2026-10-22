using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using UniversalScanner.DeviceSDK.ONVIF;

namespace UniversalScanner.Forms
{
    public partial class OnvifDateTimeConfigurationForm : Form
    {
        private readonly string _ipAddress;
        private readonly int _port;
        private OnvifSoapClient _soapClient;
        private OnvifDateTimeService _service;
        private string _username;
        private string _password;
        private bool _isHikvisionWorkaround;

        public OnvifDateTimeConfigurationForm(string ipAddress, int port, bool isHikvisionWorkaround = false)
        {
            InitializeComponent();
            _ipAddress = ipAddress;
            _port = port;
            _isHikvisionWorkaround = isHikvisionWorkaround;
            UniversalScanner.AppVisualTheme.Apply(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                {
                    components.Dispose();
                }
                _soapClient?.Dispose();
            }
            base.Dispose(disposing);
        }

        private async void OnvifDateTimeConfigurationForm_Load(object sender, EventArgs e)
        {
            _soapClient = new OnvifSoapClient();
            _service = new OnvifDateTimeService(_soapClient);

            comboTimeZone.Items.AddRange(OnvifTimeZoneHelper.GetFriendlyNames().ToArray());

            if (!UniversalLoginManager.HasUniversalCredentials())
            {
                using (var loginForm = new UniversalLoginForm())
                {
                    if (loginForm.ShowDialog(this) != DialogResult.OK)
                    {
                        Close();
                        return;
                    }
                }
            }

            UniversalLoginManager.GetUniversalCredentials(out _username, out _password);

            await LoadDataAsync();
        }

        private async Task<bool> LoadDataAsync(bool isPostWriteRefresh = false)
        {
            SetLoadingState(true, "Loading camera date and time...");

            try
            {
                var config = await _service.GetConfigurationAsync(_ipAddress, _port, _username, _password, CancellationToken.None);
                
                if (isPostWriteRefresh && config.FailureType == OnvifDateTimeFailureType.ConnectionFailed && config.ErrorMessage != null && config.ErrorMessage.IndexOf("trust relationship", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    labelStatus.Text = "Refresh pending: Waiting for camera to apply settings...";
                    labelStatus.Refresh();
                    await Task.Delay(3000);
                    config = await _service.GetConfigurationAsync(_ipAddress, _port, _username, _password, CancellationToken.None);
                    
                    if (config.FailureType != OnvifDateTimeFailureType.None || !config.IsSupported)
                    {
                        MessageBox.Show(this, "The configuration was applied successfully, but the camera could not be refreshed immediately.\r\nThis is usually caused by the camera regenerating its internal security certificates after a time change.\r\n\r\nPlease close and reopen this dialog to view the updated time.", "Refresh Pending", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        
                        labelStatus.Text = "Refresh Failed. Please reopen dialog.";
                        labelStatus.ForeColor = Color.Red;
                        
                        buttonApply.Enabled = false;
                        buttonSyncComputer.Enabled = false;
                        groupNTP.Enabled = false;
                        groupManual.Enabled = false;
                        
                        return false;
                    }
                }
                else if (config.FailureType != OnvifDateTimeFailureType.None || !config.IsSupported)
                {
                    string errStr = config.FailureType == OnvifDateTimeFailureType.ActionNotSupported || !config.IsSupported ? 
                        "Not Supported: The camera does not support standard ONVIF Date & Time." : 
                        $"Load Failed: {config.FailureType} - {config.ErrorMessage}";
                    
                    labelStatus.Text = errStr;
                    labelStatus.ForeColor = Color.Red;
                    
                    buttonApply.Enabled = false;
                    buttonSyncComputer.Enabled = false;
                    groupNTP.Enabled = false;
                    groupManual.Enabled = false;
                    
                    return false;
                }

                if (!config.IsWriteSupported)
                {
                    buttonApply.Enabled = false;
                    buttonSyncComputer.Enabled = false;
                    labelStatus.Text = "Read-Only: Camera does not support setting time.";
                    labelStatus.ForeColor = Color.Red;
                }
                else
                {
                    labelStatus.Text = "Ready";
                    labelStatus.ForeColor = SystemColors.ControlText;
                }

                if (!config.NtpSupported)
                {
                    radioNTP.Enabled = false;
                    groupNTP.Enabled = false;
                    if (radioNTP.Checked) radioManual.Checked = true;
                }

                if (!config.NtpPortSupported || !config.NtpIntervalSupported)
                {
                    numericNtpPort.Enabled = false;
                    numericNtpInterval.Enabled = false;
                    
                    numericNtpPort.Value = 123;
                    numericNtpInterval.Value = 600;
                    
                    labelNtpWarning.Visible = true;
                    labelNtpWarning.Text = "This camera does not expose Port or Update Interval through standard ONVIF.\r\nThe displayed values are the standard NTP defaults and cannot be modified.";
                    labelNtpWarning.Location = new System.Drawing.Point(15, 78);
                    labelNtpWarning.AutoSize = true;
                    labelNtpWarning.ForeColor = System.Drawing.Color.DimGray;
                }
                else
                {
                    numericNtpPort.Enabled = true;
                    numericNtpInterval.Enabled = true;
                    
                    labelNtpWarning.Visible = false;
                    
                    if (config.NtpPortSupported) numericNtpPort.Value = config.NtpPort;
                    if (config.NtpIntervalSupported) numericNtpInterval.Value = config.NtpInterval;
                }

                UpdateCurrentTimeLabels(config);

                if (config.DateTimeType == OnvifDateTimeType.NTP)
                    radioNTP.Checked = true;
                else
                    radioManual.Checked = true;

                checkDaylightSavings.Checked = config.DaylightSavings;

                string friendlyTz = OnvifTimeZoneHelper.GetFriendlyFromPosix(config.TimeZone);
                if (OnvifTimeZoneHelper.IsKnownFriendlyName(friendlyTz))
                {
                    comboTimeZone.SelectedItem = friendlyTz;
                }
                else if (!string.IsNullOrEmpty(friendlyTz))
                {
                    if (!comboTimeZone.Items.Contains(friendlyTz))
                        comboTimeZone.Items.Add(friendlyTz);
                    comboTimeZone.SelectedItem = friendlyTz;
                }

                if (config.LocalTime.HasValue)
                {
                    dateTimePickerDate.Value = config.LocalTime.Value.Date;
                    dateTimePickerTime.Value = config.LocalTime.Value;
                }

                if (config.NtpServers.Count > 0)
                {
                    textNtpServer.Text = config.NtpServers[0];
                }

                syncMode_CheckedChanged(null, null);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to retrieve configuration: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                SetLoadingState(false, "Ready");
            }
        }

        private void UpdateCurrentTimeLabels(OnvifDateTimeConfiguration config)
        {
            labelCameraLocalTime.Text = "Camera Local Time: " + (config.LocalTime?.ToString("dd/MM/yyyy HH:mm:ss") ?? "Unknown");
            labelCameraUtcTime.Text = "Camera UTC Time:   " + (config.UtcTime?.ToString("dd/MM/yyyy HH:mm:ss") ?? "Unknown");

            var computerTime = DateTime.Now;
            labelComputerTime.Text = "Computer Time:     " + computerTime.ToString("dd/MM/yyyy HH:mm:ss");

            if (config.UtcTime.HasValue)
            {
                var computerUtc = DateTime.UtcNow;
                TimeSpan diff = config.UtcTime.Value - computerUtc;
                double totalSeconds = Math.Abs(diff.TotalSeconds);

                int days = Math.Abs(diff.Days);
                int hours = Math.Abs(diff.Hours);
                int minutes = Math.Abs(diff.Minutes);
                int seconds = Math.Abs(diff.Seconds);

                var parts = new System.Collections.Generic.List<string>();
                if (days > 0) parts.Add($"{days} day{(days == 1 ? "" : "s")}");
                if (hours > 0) parts.Add($"{hours} hour{(hours == 1 ? "" : "s")}");
                if (minutes > 0) parts.Add($"{minutes} minute{(minutes == 1 ? "" : "s")}");
                if (seconds > 0 || parts.Count == 0) parts.Add($"{seconds} second{(seconds == 1 ? "" : "s")}");

                string timeString = string.Join(" ", parts);

                string diffText = diff.TotalSeconds >= 0 ? 
                    $"Camera is {timeString} ahead" :
                    $"Camera is {timeString} behind";

                if (totalSeconds <= 5)
                {
                    int sec = (int)totalSeconds;
                    diffText = $"Camera synchronized ({sec} second{(sec == 1 ? "" : "s")} difference)";
                    labelDifference.ForeColor = Color.Green;
                }
                else if (totalSeconds <= 60)
                {
                    labelDifference.ForeColor = Color.DarkGoldenrod;
                }
                else
                {
                    labelDifference.ForeColor = Color.Red;
                }

                labelDifference.Text = "Difference:        " + diffText;
            }
            else
            {
                labelDifference.Text = "Difference:        Unknown";
                labelDifference.ForeColor = SystemColors.ControlText;
            }
        }

        private void syncMode_CheckedChanged(object sender, EventArgs e)
        {
            groupManual.Enabled = radioManual.Checked;
            groupNTP.Enabled = radioNTP.Checked;
        }

        private async void buttonRefresh_Click(object sender, EventArgs e)
        {
            await LoadDataAsync();
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private async void buttonSyncComputer_Click(object sender, EventArgs e)
        {
            if (comboTimeZone.SelectedItem == null)
            {
                MessageBox.Show(this, "Please select a Time Zone first.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var pcLocal = DateTime.Now;
            var pcUtc = DateTime.UtcNow;

            dateTimePickerDate.Value = pcLocal.Date;
            dateTimePickerTime.Value = pcLocal;

            await ApplyManualTimeAsync(pcUtc, pcLocal);
        }

        private async void buttonApply_Click(object sender, EventArgs e)
        {
            if (radioManual.Checked)
            {
                if (comboTimeZone.SelectedItem == null)
                {
                    MessageBox.Show(this, "Please select a Time Zone.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var localTime = dateTimePickerDate.Value.Date + dateTimePickerTime.Value.TimeOfDay;
                
                string friendlyTz = comboTimeZone.SelectedItem.ToString();
                string posixTz = OnvifTimeZoneHelper.GetPosixFromFriendly(friendlyTz);
                
                // Convert the selected local date/time correctly to UTC according to the selected time-zone configuration.
                DateTime utcTime;
                try
                {
                    var tzInfo = TimeZoneInfo.FindSystemTimeZoneById(friendlyTz);
                    utcTime = TimeZoneInfo.ConvertTimeToUtc(localTime, tzInfo);
                }
                catch
                {
                    // Fallback approximation using local machine offset if the timezone isn't a known Windows ID
                    var pcOffset = TimeZoneInfo.Local.GetUtcOffset(localTime);
                    utcTime = localTime - pcOffset;
                }

                await ApplyManualTimeAsync(utcTime, localTime);
            }
            else
            {
                await ApplyNtpTimeAsync();
            }
        }

        private async Task ApplyManualTimeAsync(DateTime utcTime, DateTime? requestedLocalTime = null)
        {
            SetLoadingState(true, "Applying configuration...");

            try
            {
                string friendlyTz = comboTimeZone.SelectedItem?.ToString() ?? "";
                string posixTz = OnvifTimeZoneHelper.GetPosixFromFriendly(friendlyTz);
                
                var settings = new OnvifManualDateTimeSettings()
                {
                    UtcTime = utcTime,
                    LocalTime = requestedLocalTime ?? DateTime.Now,
                    TimeZone = posixTz,
                    DaylightSavings = checkDaylightSavings.Checked,
                    IsHikvisionWorkaround = _isHikvisionWorkaround
                };

                try
                {
                    await _service.SetManualDateTimeAsync(_ipAddress, _port, _username, _password, settings, CancellationToken.None);
                }
                catch { }
                
                await RecreateServiceAndVerifyAsync(utcTime, false, "Date and time configuration was applied successfully.");
            }
            finally
            {
                SetLoadingState(false, "Ready");
            }
        }

        private async Task ApplyNtpTimeAsync()
        {
            if (string.IsNullOrWhiteSpace(textNtpServer.Text))
            {
                MessageBox.Show(this, "Please enter an NTP server.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SetLoadingState(true, "Applying configuration...");

            try
            {
                string friendlyTz = comboTimeZone.SelectedItem?.ToString() ?? "";
                string posixTz = OnvifTimeZoneHelper.GetPosixFromFriendly(friendlyTz);

                var settings = new OnvifNtpDateTimeSettings()
                {
                    NtpServers = new System.Collections.Generic.List<string> { textNtpServer.Text.Trim() },
                    TimeZone = posixTz,
                    DaylightSavings = checkDaylightSavings.Checked,
                    IsHikvisionWorkaround = _isHikvisionWorkaround
                };

                try
                {
                    await _service.SetNtpDateTimeAsync(_ipAddress, _port, _username, _password, settings, CancellationToken.None);
                }
                catch { }
                
                await RecreateServiceAndVerifyAsync(null, true, "NTP configuration was applied successfully.");
            }
            finally
            {
                SetLoadingState(false, "Ready");
            }
        }

        private async Task RecreateServiceAndVerifyAsync(DateTime? requestedUtc, bool isNtp, string successMessage)
        {
            await Task.Delay(2000);

            _soapClient?.Dispose();
            _soapClient = new OnvifSoapClient();
            _service = new OnvifDateTimeService(_soapClient);

            var config = await _service.GetConfigurationAsync(_ipAddress, _port, _username, _password, CancellationToken.None);
            
            if (config.FailureType != OnvifDateTimeFailureType.None || !config.IsSupported)
            {
                MessageBox.Show(this, "Configuration could not be verified and the camera time remains incorrect.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                await LoadDataAsync();
                return;
            }

            bool verified = false;

            if (isNtp)
            {
                if (config.DateTimeType == OnvifDateTimeType.NTP)
                {
                    verified = true;
                }
            }
            else if (requestedUtc.HasValue)
            {
                if (config.UtcTime.HasValue)
                {
                    var diff = Math.Abs((config.UtcTime.Value - requestedUtc.Value).TotalSeconds);
                    if (diff <= 10)
                    {
                        verified = true;
                    }
                }
            }

            if (verified)
            {
                MessageBox.Show(this, successMessage, "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, "Configuration could not be verified and the camera time remains incorrect.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            
            await LoadDataAsync();
        }

        private void SetLoadingState(bool isLoading, string statusText)
        {
            buttonApply.Enabled = !isLoading && _service != null;
            buttonRefresh.Enabled = !isLoading;
            buttonSyncComputer.Enabled = !isLoading && radioManual.Checked;
            groupManual.Enabled = !isLoading && radioManual.Checked;
            groupNTP.Enabled = !isLoading && radioNTP.Checked;
            labelStatus.Text = statusText;
            labelStatus.Refresh();
        }
    }
}
