using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace UniversalScanner
{
    public partial class OnvifEventMonitorForm : Form
    {
        private readonly string _ipAddress;
        private readonly int _port;
        private string _username;
        private string _password;
        private readonly string _vendor;
        private readonly string _model;
        private readonly string _deviceName;
        private OnvifEventClient _onvifClient;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _monitoringTask;
        private DataTable _eventTable;
        private BindingSource _bindingSource;
        private readonly object _eventListLock = new object();
        private int _eventCount = 0;
        private bool _isMonitoring = false;

        // Store raw XML per row — indexed by a hidden GUID column
        private readonly Dictionary<string, string> _rawXmlStore = new Dictionary<string, string>();

        // UI Controls
        private Label _deviceInfoLabel;
        private DataGridView _eventsGridView;
        private Button _startMonitorButton;
        private Button _stopMonitorButton;
        private Button _clearButton;
        private Button _exportCsvButton;
        private Button _exportPdfButton;
        private Button _showRawXmlButton;
        private Label _statusLabel;
        private Label _motionIndicatorLabel;
        private Label _motionTextLabel;
        private ProgressBar _progressBar;

        // Constants
        private const int MaxEvents = 1000;
        private const int PollingIntervalMs = 500;

        public OnvifEventMonitorForm(string ipAddress, int port, string username, string password,
                                    string vendor, string model, string deviceName)
        {
            _ipAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
            _port = port;
            _username = username ?? string.Empty;
            _password = password ?? string.Empty;
            _vendor = vendor ?? string.Empty;
            _model = model ?? string.Empty;
            _deviceName = deviceName ?? string.Empty;

            InitializeComponent();
            InitializeEventTable();
            InitializeUI();
            WireUpEvents();
            AppVisualTheme.Apply(this);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            //
            // OnvifEventMonitorForm
            //
            this.ClientSize = new System.Drawing.Size(1100, 650);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "ONVIF Live Event Monitor";
            this.Name = "OnvifEventMonitorForm";
            this.MinimumSize = new Size(900, 500);
            this.ResumeLayout(false);
        }

        private void InitializeEventTable()
        {
            _eventTable = new DataTable();
            _eventTable.Columns.Add("RowId", typeof(string));       // Hidden GUID for raw XML lookup
            _eventTable.Columns.Add("ArrivalTime", typeof(string));
            _eventTable.Columns.Add("Topic", typeof(string));
            _eventTable.Columns.Add("Operation", typeof(string));
            _eventTable.Columns.Add("Source", typeof(string));
            _eventTable.Columns.Add("Key", typeof(string));
            _eventTable.Columns.Add("Data", typeof(string));
            _eventTable.Columns.Add("Message", typeof(string));
            _eventTable.Columns.Add("Details", typeof(string));

            _bindingSource = new BindingSource();
            _bindingSource.DataSource = _eventTable;
        }

        private void InitializeUI()
        {
            this.SuspendLayout();

            // Create controls
            _deviceInfoLabel = new Label();
            _eventsGridView = new DataGridView();
            _startMonitorButton = new Button();
            _stopMonitorButton = new Button();
            _clearButton = new Button();
            _exportCsvButton = new Button();
            _exportPdfButton = new Button();
            _showRawXmlButton = new Button();
            _statusLabel = new Label();
            _motionIndicatorLabel = new Label();
            _motionTextLabel = new Label();
            _progressBar = new ProgressBar();

            //
            // deviceInfoLabel
            //
            _deviceInfoLabel.AutoSize = false;
            _deviceInfoLabel.Location = new Point(12, 9);
            _deviceInfoLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _deviceInfoLabel.Size = new Size(1070, 22);
            _deviceInfoLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _deviceInfoLabel.TextAlign = ContentAlignment.MiddleLeft;

            //
            // eventsGridView
            //
            _eventsGridView.AllowUserToAddRows = false;
            _eventsGridView.AllowUserToDeleteRows = false;
            _eventsGridView.AllowUserToOrderColumns = true;
            _eventsGridView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _eventsGridView.BackgroundColor = SystemColors.Window;
            _eventsGridView.BorderStyle = BorderStyle.Fixed3D;
            _eventsGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            _eventsGridView.Location = new Point(12, 38);
            _eventsGridView.Size = new Size(1070, 460);
            _eventsGridView.TabIndex = 0;
            _eventsGridView.ReadOnly = true;
            _eventsGridView.RowHeadersVisible = false;
            _eventsGridView.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _eventsGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _eventsGridView.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 242, 252);
            _eventsGridView.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(28, 61, 108);
            _eventsGridView.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            _eventsGridView.EnableHeadersVisualStyles = false;
            _eventsGridView.DefaultCellStyle.Font = new Font("Segoe UI", 8.5F);
            _eventsGridView.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 248, 252);
            _eventsGridView.DataSource = _bindingSource;

            // Bind to table first, then configure columns
            // (columns are auto-generated from the DataTable)

            // Button panel anchored to bottom
            int btnY = 510;

            //
            // startMonitorButton
            //
            _startMonitorButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _startMonitorButton.Location = new Point(12, btnY);
            _startMonitorButton.Size = new Size(105, 30);
            _startMonitorButton.Text = "▶ Start Monitor";
            _startMonitorButton.UseVisualStyleBackColor = true;
            _startMonitorButton.Font = new Font("Segoe UI", 8.5F);

            //
            // stopMonitorButton
            //
            _stopMonitorButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _stopMonitorButton.Location = new Point(125, btnY);
            _stopMonitorButton.Size = new Size(105, 30);
            _stopMonitorButton.Text = "■ Stop Monitor";
            _stopMonitorButton.UseVisualStyleBackColor = true;
            _stopMonitorButton.Enabled = false;
            _stopMonitorButton.Font = new Font("Segoe UI", 8.5F);

            //
            // clearButton
            //
            _clearButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _clearButton.Location = new Point(240, btnY);
            _clearButton.Size = new Size(80, 30);
            _clearButton.Text = "Clear";
            _clearButton.UseVisualStyleBackColor = true;
            _clearButton.Font = new Font("Segoe UI", 8.5F);

            //
            // exportCsvButton
            //
            _exportCsvButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _exportCsvButton.Location = new Point(330, btnY);
            _exportCsvButton.Size = new Size(100, 30);
            _exportCsvButton.Text = "Export CSV";
            _exportCsvButton.UseVisualStyleBackColor = true;
            _exportCsvButton.Font = new Font("Segoe UI", 8.5F);

            //
            // exportPdfButton
            //
            _exportPdfButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _exportPdfButton.Location = new Point(440, btnY);
            _exportPdfButton.Size = new Size(100, 30);
            _exportPdfButton.Text = "Export PDF";
            _exportPdfButton.UseVisualStyleBackColor = true;
            _exportPdfButton.Font = new Font("Segoe UI", 8.5F);

            //
            // showRawXmlButton
            //
            _showRawXmlButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _showRawXmlButton.Location = new Point(550, btnY);
            _showRawXmlButton.Size = new Size(120, 30);
            _showRawXmlButton.Text = "Show Raw XML";
            _showRawXmlButton.UseVisualStyleBackColor = true;
            _showRawXmlButton.Enabled = false;
            _showRawXmlButton.Font = new Font("Segoe UI", 8.5F);

            // Bottom status row
            int statusY = btnY + 40;

            //
            // statusLabel
            //
            _statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _statusLabel.Location = new Point(12, statusY);
            _statusLabel.Size = new Size(700, 22);
            _statusLabel.Text = "Ready — Click Start Monitor to begin.";
            _statusLabel.BorderStyle = BorderStyle.FixedSingle;
            _statusLabel.Padding = new Padding(4, 2, 2, 2);
            _statusLabel.Font = new Font("Segoe UI", 8.5F);
            _statusLabel.BackColor = Color.FromArgb(245, 248, 252);

            //
            // motionIndicatorLabel (colored dot)
            //
            _motionIndicatorLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _motionIndicatorLabel.Location = new Point(940, statusY);
            _motionIndicatorLabel.Size = new Size(22, 22);
            _motionIndicatorLabel.Text = "●";
            _motionIndicatorLabel.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            _motionIndicatorLabel.TextAlign = ContentAlignment.MiddleCenter;
            _motionIndicatorLabel.ForeColor = Color.LimeGreen;
            _motionIndicatorLabel.BackColor = Color.Transparent;

            //
            // motionTextLabel
            //
            _motionTextLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _motionTextLabel.Location = new Point(964, statusY);
            _motionTextLabel.Size = new Size(120, 22);
            _motionTextLabel.Text = "No Motion";
            _motionTextLabel.Font = new Font("Segoe UI", 8.5F);
            _motionTextLabel.TextAlign = ContentAlignment.MiddleLeft;

            //
            // progressBar
            //
            _progressBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _progressBar.Location = new Point(830, statusY);
            _progressBar.Size = new Size(100, 22);
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = false;

            // Add controls to form
            this.Controls.Add(_deviceInfoLabel);
            this.Controls.Add(_eventsGridView);
            this.Controls.Add(_startMonitorButton);
            this.Controls.Add(_stopMonitorButton);
            this.Controls.Add(_clearButton);
            this.Controls.Add(_exportCsvButton);
            this.Controls.Add(_exportPdfButton);
            this.Controls.Add(_showRawXmlButton);
            this.Controls.Add(_statusLabel);
            this.Controls.Add(_motionIndicatorLabel);
            this.Controls.Add(_motionTextLabel);
            this.Controls.Add(_progressBar);

            // Update device info
            UpdateDeviceInfo();

            this.ResumeLayout(false);
            this.PerformLayout();

            // Configure grid columns after binding (deferred)
            _eventsGridView.DataBindingComplete += (s, e) => ConfigureGridColumns();
        }

        private void ConfigureGridColumns()
        {
            if (_eventsGridView.Columns.Count == 0) return;

            // Hide the RowId column
            if (_eventsGridView.Columns.Contains("RowId"))
            {
                _eventsGridView.Columns["RowId"].Visible = false;
            }

            // Set column widths and headers
            SetColumnProps("ArrivalTime", "Arrival Time", 135);
            SetColumnProps("Topic",       "Topic",        200);
            SetColumnProps("Operation",   "Operation",     80);
            SetColumnProps("Source",      "Source",        140);
            SetColumnProps("Key",         "Key",           100);
            SetColumnProps("Data",        "Data",          140);
            SetColumnProps("Message",     "Message",       180);
            SetColumnProps("Details",     "Details",       140);
        }

        private void SetColumnProps(string name, string header, int width)
        {
            if (_eventsGridView.Columns.Contains(name))
            {
                _eventsGridView.Columns[name].HeaderText = header;
                _eventsGridView.Columns[name].Width = width;
            }
        }

        private void UpdateDeviceInfo()
        {
            _deviceInfoLabel.Text =
                $"Device: {_deviceName} | " +
                $"IP: {_ipAddress} | " +
                $"Vendor: {_vendor} | " +
                $"Model: {_model} | " +
                $"ONVIF Port: {_port} | " +
                $"User: {(string.IsNullOrEmpty(_username) ? "(none)" : _username)}";
        }

        private void WireUpEvents()
        {
            _startMonitorButton.Click += StartMonitorButton_Click;
            _stopMonitorButton.Click += StopMonitorButton_Click;
            _clearButton.Click += ClearButton_Click;
            _exportCsvButton.Click += ExportCsvButton_Click;
            _exportPdfButton.Click += ExportPdfButton_Click;
            _showRawXmlButton.Click += ShowRawXmlButton_Click;
            _eventsGridView.SelectionChanged += EventsGridView_SelectionChanged;
            _eventsGridView.CellDoubleClick += EventsGridView_CellDoubleClick;
            this.FormClosing += OnvifEventMonitorForm_FormClosing;
        }

        private void EventsGridView_SelectionChanged(object sender, EventArgs e)
        {
            _showRawXmlButton.Enabled = _eventsGridView.SelectedRows.Count > 0;
        }

        private void EventsGridView_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
                ShowRawXmlForSelectedRow();
        }

        private async void StartMonitorButton_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[ONVIF] NEW StartMonitorButton_Click reached");
            System.Diagnostics.Debug.WriteLine("[ONVIF] Executable: " + Application.ExecutablePath);
            System.Diagnostics.Debug.WriteLine("[ONVIF] Assembly: " + typeof(OnvifEventMonitorForm).Assembly.Location);

            try
            {
                // Disable start button
                _startMonitorButton.Enabled = false;
                _showRawXmlButton.Enabled = false;

                bool credentialsValid = false;
                string testUser = _username;
                string testPass = _password;

                if (!string.IsNullOrEmpty(testUser))
                {
                    UpdateStatusAsync("Testing credentials with ONVIF...");
                    _progressBar.Visible = true;
                    
                    try
                    {
                        var testClient = new OnvifEventClient(_ipAddress, _port, testUser, testPass);
                        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        
                        bool hasEvents = await testClient.HasEventsServiceAsync(cts.Token);

                        if (hasEvents)
                        {
                            UpdateStatusAsync("ONVIF credentials accepted.");
                            credentialsValid = true;
                        }
                        else
                        {
                            UpdateStatusAsync("ONVIF Events service was not found.");
                        }
                        
                        testClient.Dispose();
                    }
                    catch (OperationCanceledException)
                    {
                        UpdateStatusAsync("ONVIF authentication timed out. Please verify the username and password.");
                        MessageBox.Show(this,
                            "ONVIF authentication could not be verified. The username or password may be incorrect, or the camera did not respond.",
                            "ONVIF Live Event Monitor",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        UpdateStatusAsync("ONVIF credentials rejected. Please enter the correct username and password.");
                        MessageBox.Show(this,
                            "ONVIF authentication failed. The username or password was rejected.",
                            "ONVIF Live Event Monitor",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                    catch (Exception ex)
                    {
                        if (IsAuthenticationFailure(ex))
                        {
                            UpdateStatusAsync("ONVIF credentials rejected. Please enter the correct username and password.");
                            MessageBox.Show(this,
                                "ONVIF authentication failed. The username or password was rejected.",
                                "ONVIF Live Event Monitor",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }
                        else
                        {
                            UpdateStatusAsync($"ONVIF pre-flight warning: {ex.GetType().Name}");
                            credentialsValid = true;
                            await Task.Delay(1000);
                        }
                    }
                }

                if (!credentialsValid)
                {
                    UpdateStatusAsync("Opening manual credentials dialog.");
                    _progressBar.Visible = false;
                    await Task.Delay(1000);

                    if (!PromptForCredentials(testUser, testPass))
                    {
                        ResetButtons();
                        UpdateStatusAsync("Ready - Click Start Monitor to begin.");
                        return; // User cancelled
                    }
                    UpdateDeviceInfo();
                }

                // Create client with (possibly updated) credentials
                _onvifClient?.Dispose();
                _onvifClient = new OnvifEventClient(_ipAddress, _port, _username, _password);

                // Disable start button, enable stop button
                _startMonitorButton.Enabled = false;
                _stopMonitorButton.Enabled = true;
                _showRawXmlButton.Enabled = false;

                // Update status
                _statusLabel.Text = "Connecting to device...";
                _progressBar.Visible = true;
                _isMonitoring = true;

                // Clear existing events
                ClearEvents();

                // Start monitoring
                _cancellationTokenSource = new CancellationTokenSource();
                _monitoringTask = MonitorEventsAsync(_cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Error: {ex.Message}";
                ResetButtons();
            }
        }

        private static bool IsAuthenticationFailure(Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                string message = current.Message ?? string.Empty;
                if (message.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("NotAuthorized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("not authorized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("authentication", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Shows a simple credentials prompt dialog.
        /// Returns true if user clicked OK, false if cancelled.
        /// </summary>
        private bool PromptForCredentials(string initialUsername = null, string initialPassword = null)
        {
            using (Form credForm = new Form())
            {
                credForm.Text = "ONVIF Credentials";
                credForm.Size = new Size(360, 200);
                credForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                credForm.StartPosition = FormStartPosition.CenterParent;
                credForm.MaximizeBox = false;
                credForm.MinimizeBox = false;

                Label lblInfo = new Label()
                {
                    Text = $"Enter ONVIF credentials for {_ipAddress}:",
                    Location = new Point(15, 15),
                    AutoSize = true,
                    Font = new Font("Segoe UI", 9F)
                };

                Label lblUser = new Label()
                {
                    Text = "Username:",
                    Location = new Point(15, 50),
                    Size = new Size(80, 22),
                    Font = new Font("Segoe UI", 9F),
                    TextAlign = ContentAlignment.MiddleLeft
                };

                TextBox txtUser = new TextBox()
                {
                    Location = new Point(100, 50),
                    Size = new Size(230, 22),
                    Text = !string.IsNullOrEmpty(initialUsername) ? initialUsername : (!string.IsNullOrEmpty(_username) ? _username : "admin")
                };

                Label lblPass = new Label()
                {
                    Text = "Password:",
                    Location = new Point(15, 82),
                    Size = new Size(80, 22),
                    Font = new Font("Segoe UI", 9F),
                    TextAlign = ContentAlignment.MiddleLeft
                };

                TextBox txtPass = new TextBox()
                {
                    Location = new Point(100, 82),
                    Size = new Size(230, 22),
                    UseSystemPasswordChar = true,
                    Text = !string.IsNullOrEmpty(initialPassword) ? initialPassword : _password
                };

                Button btnOk = new Button()
                {
                    Text = "Connect",
                    DialogResult = DialogResult.OK,
                    Location = new Point(155, 120),
                    Size = new Size(85, 30)
                };

                Button btnCancel = new Button()
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(245, 120),
                    Size = new Size(85, 30)
                };

                credForm.AcceptButton = btnOk;
                credForm.CancelButton = btnCancel;
                credForm.Controls.AddRange(new Control[] { lblInfo, lblUser, txtUser, lblPass, txtPass, btnOk, btnCancel });

                if (credForm.ShowDialog(this) == DialogResult.OK)
                {
                    _username = txtUser.Text.Trim();
                    _password = txtPass.Text;
                    UpdateDeviceInfo();
                    return true;
                }

                return false;
            }
        }

        private async void StopMonitorButton_Click(object sender, EventArgs e)
        {
            await StopMonitoringAsync();
        }

        private async Task StopMonitoringAsync()
        {
            _isMonitoring = false;

            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
            }

            if (_monitoringTask != null)
            {
                try
                {
                    await _monitoringTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelled
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error stopping monitor: {ex.Message}");
                }
            }

            // Reset UI
            if (!this.IsDisposed && this.IsHandleCreated)
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        ResetButtons();
                        _statusLabel.Text = "Monitoring stopped.";
                        _progressBar.Visible = false;
                    }));
                }
                else
                {
                    ResetButtons();
                    _statusLabel.Text = "Monitoring stopped.";
                    _progressBar.Visible = false;
                }
            }
        }

        private void ResetButtons()
        {
            _startMonitorButton.Enabled = true;
            _stopMonitorButton.Enabled = false;
            _showRawXmlButton.Enabled = _eventsGridView.SelectedRows.Count > 0;
        }

        private async Task MonitorEventsAsync(CancellationToken cancellationToken)
        {
            try
            {
                UpdateStatusAsync("Checking for Events service...");

                bool hasEventsService = await _onvifClient.HasEventsServiceAsync(cancellationToken);
                if (!hasEventsService)
                {
                    UpdateStatusAsync("Error: Device does not support ONVIF Events service.");
                    return;
                }

                UpdateStatusAsync("Getting event properties...");
                var eventProperties = await _onvifClient.GetEventPropertiesAsync(cancellationToken);

                UpdateStatusAsync("Creating PullPoint subscription (all topics)...");

                string subscriptionEndpoint = await _onvifClient.CreatePullPointSubscriptionAsync(cancellationToken);
                if (string.IsNullOrEmpty(subscriptionEndpoint))
                {
                    UpdateStatusAsync("Error: Failed to create event subscription. Check credentials.");
                    return;
                }

                UpdateStatusAsync("Subscription created. Polling for events...");

                // Start polling for events
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var events = await _onvifClient.PullMessagesAsync(cancellationToken);

                        if (events != null && events.Count > 0)
                        {
                            foreach (var evt in events)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                    break;

                                ProcessEvent(evt);
                            }
                        }

                        // Short delay between polls
                        await Task.Delay(PollingIntervalMs, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        UpdateStatusAsync($"Poll error: {ex.Message} — retrying...");
                        await Task.Delay(PollingIntervalMs * 4, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
            catch (Exception ex)
            {
                UpdateStatusAsync($"Monitoring error: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (_onvifClient != null)
                        await _onvifClient.UnsubscribeAsync(CancellationToken.None);
                }
                catch
                {
                    // Ignore errors during cleanup
                }

                _isMonitoring = false;
                
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    if (this.InvokeRequired)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            ResetButtons();
                            _progressBar.Visible = false;
                        }));
                    }
                    else
                    {
                        ResetButtons();
                        _progressBar.Visible = false;
                    }
                }
            }
        }

        private void ProcessEvent(OnvifEvent evt)
        {
            try
            {
                // Update motion indicator based on Data values
                bool motionActive = OnvifEventDecoder.IsMotionActive(evt);
                bool motionInactive = OnvifEventDecoder.IsMotionInactive(evt);

                if (motionActive)
                    UpdateMotionIndicator(true);
                else if (motionInactive)
                    UpdateMotionIndicator(false);

                // Store raw XML
                string rowId = Guid.NewGuid().ToString("N");
                lock (_eventListLock)
                {
                    _rawXmlStore[rowId] = evt.RawXml ?? string.Empty;
                }

                // Build row data
                string arrivalTime = evt.ArrivalTime.ToString("yyyy-MM-dd HH:mm:ss.fff");

                // Add to DataTable on UI thread
                Action addRow = () =>
                {
                    lock (_eventListLock)
                    {
                        // Trim oldest events
                        while (_eventTable.Rows.Count >= MaxEvents)
                        {
                            // Remove oldest row and its XML
                            string oldId = _eventTable.Rows[_eventTable.Rows.Count - 1]["RowId"]?.ToString();
                            if (!string.IsNullOrEmpty(oldId))
                                _rawXmlStore.Remove(oldId);
                            _eventTable.Rows.RemoveAt(_eventTable.Rows.Count - 1);
                        }

                        DataRow newRow = _eventTable.NewRow();
                        newRow["RowId"] = rowId;
                        newRow["ArrivalTime"] = arrivalTime;
                        newRow["Topic"] = evt.Topic ?? "";
                        newRow["Operation"] = evt.PropertyOperation ?? "";
                        newRow["Source"] = evt.Source ?? "";
                        newRow["Key"] = evt.Key ?? "";
                        newRow["Data"] = evt.Data ?? "";
                        newRow["Message"] = evt.Message ?? "";
                        newRow["Details"] = evt.Details ?? "";

                        _eventTable.Rows.InsertAt(newRow, 0);
                        _eventCount++;
                    }

                    _statusLabel.Text = $"Monitoring — {_eventCount} events received";
                    _showRawXmlButton.Enabled = _eventsGridView.SelectedRows.Count > 0;
                };

                if (this.IsDisposed) return;

                if (this.InvokeRequired)
                    this.BeginInvoke(addRow);
                else
                    addRow();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing event: {ex.Message}");
            }
        }

        private void UpdateMotionIndicator(bool isMotion)
        {
            Action update = () =>
            {
                if (isMotion)
                {
                    _motionIndicatorLabel.Text = "●";
                    _motionIndicatorLabel.ForeColor = Color.Red;
                    _motionTextLabel.Text = "MOTION";
                    _motionTextLabel.ForeColor = Color.Red;
                }
                else
                {
                    _motionIndicatorLabel.Text = "●";
                    _motionIndicatorLabel.ForeColor = Color.LimeGreen;
                    _motionTextLabel.Text = "No Motion";
                    _motionTextLabel.ForeColor = Color.Black;
                }
            };

            if (this.IsDisposed) return;

            if (this.InvokeRequired)
                this.BeginInvoke(update);
            else
                update();
        }

        private void UpdateStatusAsync(string status)
        {
            if (this.IsDisposed) return;

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => _statusLabel.Text = status));
            }
            else
            {
                _statusLabel.Text = status;
            }
        }

        private void ClearEvents()
        {
            lock (_eventListLock)
            {
                _eventTable.Rows.Clear();
                _rawXmlStore.Clear();
                _eventCount = 0;
            }

            _statusLabel.Text = "Event list cleared.";
            _motionIndicatorLabel.ForeColor = Color.LimeGreen;
            _motionTextLabel.Text = "No Motion";
            _motionTextLabel.ForeColor = Color.Black;
            _showRawXmlButton.Enabled = false;
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            if (_isMonitoring)
            {
                var result = MessageBox.Show(
                    "Are you sure you want to clear the event list while monitoring is active?",
                    "Clear Events",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No)
                    return;
            }

            ClearEvents();
        }

        private void ExportCsvButton_Click(object sender, EventArgs e)
        {
            if (_eventTable.Rows.Count == 0)
            {
                MessageBox.Show(this, "No events to export.", "Export Events",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog saveDialog = new SaveFileDialog())
            {
                saveDialog.Filter = "CSV files (*.csv)|*.csv";
                saveDialog.Title = "Export Events to CSV";
                saveDialog.FileName = $"ONVIF_Events_{_ipAddress}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        StringBuilder csvBuilder = new StringBuilder();

                        // Header — skip RowId
                        var exportColumns = _eventTable.Columns.Cast<DataColumn>()
                            .Where(c => c.ColumnName != "RowId")
                            .ToList();

                        string[] columnNames = exportColumns
                            .Select(col => $"\"{col.ColumnName}\"")
                            .ToArray();
                        csvBuilder.AppendLine(string.Join(",", columnNames));

                        // Add "Raw XML" as last column
                        csvBuilder.Clear();
                        var headerParts = exportColumns.Select(c => $"\"{c.ColumnName}\"").ToList();
                        headerParts.Add("\"RawXml\"");
                        csvBuilder.AppendLine(string.Join(",", headerParts));

                        // Rows
                        foreach (DataRow row in _eventTable.Rows)
                        {
                            var fields = new List<string>();
                            foreach (var col in exportColumns)
                            {
                                string value = row[col]?.ToString() ?? "";
                                fields.Add($"\"{value.Replace("\"", "\"\"")}\"");
                            }

                            // Add raw XML
                            string rowId = row["RowId"]?.ToString() ?? "";
                            string rawXml = "";
                            lock (_eventListLock)
                            {
                                if (_rawXmlStore.ContainsKey(rowId))
                                    rawXml = _rawXmlStore[rowId];
                            }
                            fields.Add($"\"{rawXml.Replace("\"", "\"\"")}\"");

                            csvBuilder.AppendLine(string.Join(",", fields));
                        }

                        System.IO.File.WriteAllText(saveDialog.FileName, csvBuilder.ToString(), Encoding.UTF8);

                        MessageBox.Show(this, $"Events exported successfully to:\n{saveDialog.FileName}",
                            "Export Events", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"Error exporting events: {ex.Message}", "Export Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ExportPdfButton_Click(object sender, EventArgs e)
        {
            if (_eventTable.Rows.Count == 0)
            {
                MessageBox.Show(this, "No events to export.", "Export Events",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog saveDialog = new SaveFileDialog())
            {
                saveDialog.Filter = "PDF files (*.pdf)|*.pdf";
                saveDialog.Title = "Export Events to PDF";
                saveDialog.FileName = $"ONVIF_Events_{_ipAddress}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        List<string> headers = new List<string>();
                        foreach (DataGridViewColumn col in _eventsGridView.Columns)
                        {
                            if (col.Visible)
                                headers.Add(col.HeaderText);
                        }

                        List<List<string>> rows = new List<List<string>>();
                        foreach (DataGridViewRow row in _eventsGridView.Rows)
                        {
                            if (row.IsNewRow) continue;
                            
                            List<string> rowData = new List<string>();
                            foreach (DataGridViewColumn col in _eventsGridView.Columns)
                            {
                                if (col.Visible)
                                {
                                    rowData.Add(row.Cells[col.Index].Value?.ToString() ?? "");
                                }
                            }
                            rows.Add(rowData);
                        }

                        SimplePdfTableExporter.ExportFullDeviceList(saveDialog.FileName, headers, rows);

                        MessageBox.Show(this, "Events exported to PDF successfully.", "Export Events",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Failed to export PDF:\n" + ex.Message, "Export Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ShowRawXmlButton_Click(object sender, EventArgs e)
        {
            ShowRawXmlForSelectedRow();
        }

        private void ShowRawXmlForSelectedRow()
        {
            if (_eventsGridView.SelectedRows.Count == 0)
            {
                MessageBox.Show(this, "Please select an event to view its raw XML.", "Show Raw XML",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DataGridViewRow selectedRow = _eventsGridView.SelectedRows[0];

            // Get the RowId from the hidden column
            string rowId = null;
            if (selectedRow.Cells["RowId"] != null)
                rowId = selectedRow.Cells["RowId"].Value?.ToString();

            string rawXml = string.Empty;
            if (!string.IsNullOrEmpty(rowId))
            {
                lock (_eventListLock)
                {
                    if (_rawXmlStore.ContainsKey(rowId))
                        rawXml = _rawXmlStore[rowId];
                }
            }

            if (string.IsNullOrEmpty(rawXml))
            {
                MessageBox.Show(this, "No raw XML available for this event.", "Show Raw XML",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Try to pretty-print the XML
            string prettyXml = rawXml;
            try
            {
                XDocument xdoc = XDocument.Parse(rawXml);
                prettyXml = xdoc.ToString();
            }
            catch
            {
                // Use raw string if parsing fails
            }

            // Show in a resizable dialog
            using (Form xmlForm = new Form())
            {
                xmlForm.Text = $"Raw XML — {selectedRow.Cells["Topic"]?.Value}";
                xmlForm.Size = new Size(800, 600);
                xmlForm.StartPosition = FormStartPosition.CenterParent;
                xmlForm.FormBorderStyle = FormBorderStyle.Sizable;
                xmlForm.MinimizeBox = false;

                TextBox txtXml = new TextBox()
                {
                    Multiline = true,
                    ReadOnly = true,
                    Dock = DockStyle.Fill,
                    ScrollBars = ScrollBars.Both,
                    WordWrap = false,
                    Font = new Font("Consolas", 10F),
                    Text = prettyXml,
                    BackColor = Color.White
                };

                Button btnCopy = new Button()
                {
                    Text = "Copy to Clipboard",
                    Dock = DockStyle.Bottom,
                    Height = 35,
                    Font = new Font("Segoe UI", 9F)
                };
                btnCopy.Click += (s2, e2) =>
                {
                    Clipboard.SetText(prettyXml);
                    MessageBox.Show("XML copied to clipboard.", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
                };

                xmlForm.Controls.Add(txtXml);
                xmlForm.Controls.Add(btnCopy);
                xmlForm.ShowDialog(this);
            }
        }

        private bool _formClosed = false;

        private async void OnvifEventMonitorForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_isMonitoring)
            {
                e.Cancel = true;
                await StopMonitoringAsync();

                // Now actually close the form
                if (!this.IsDisposed)
                    this.Close();
            }
            else if (!_formClosed)
            {
                _formClosed = true;
                try { _onvifClient?.Dispose(); } catch { }
                try { _cancellationTokenSource?.Dispose(); } catch { }
            }
        }

        // Override OnFormClosed to ensure cleanup happens
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (!_formClosed)
            {
                _formClosed = true;
                try { _onvifClient?.Dispose(); } catch { }
                try { _cancellationTokenSource?.Dispose(); } catch { }
            }
        }
    }
}
