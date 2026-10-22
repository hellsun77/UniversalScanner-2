using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using UniversalScanner.DeviceSDK.Hikvision;

namespace UniversalScanner
{
    public partial class ScannerWindow : Form, ScannerViewer
    {
        public event scan scanEvent;

        internal void TriggerInternalDiscovery()
        {
            scanEvent?.Invoke();
        }

        internal bool IsDeviceDiscovered(string targetIp, string targetMac)
        {
            if (foundDeviceList == null) return false;
            string expectedMac = DeviceSDK.Bosch.BoschNetwork.NormalizeMac(targetMac);
            foreach (System.Data.DataRow row in foundDeviceList.Rows)
            {
                string ip = row[(int)Columns.IPAddress]?.ToString();
                if (!string.Equals(ip, targetIp, StringComparison.OrdinalIgnoreCase))
                    continue;

                string mac = DeviceSDK.Bosch.BoschNetwork.NormalizeMac(row[(int)Columns.MAC]?.ToString());
                string uniqueId = DeviceSDK.Bosch.BoschNetwork.NormalizeMac(row[(int)Columns.UniqueId]?.ToString());

                if (!string.IsNullOrWhiteSpace(expectedMac))
                    return mac == expectedMac || uniqueId == expectedMac;

                return IsBoschRow(row);
            }
            return false;
        }

        private bool stopSubnetScan = false;
        private Task activeSubnetScanTask = Task.CompletedTask;
        private CancellationTokenSource activeSubnetScanCancellation;
        private int subnetStopRequestVersion;
        private int currentSubnetScanIndex;
        private string currentSubnetScanIp = "";
        private int statusMessageVersion;
        private bool _normalScanFilterActive = false;

        public bool IsNormalScanExecuting { get; set; } = false;

        private DataTable foundDeviceList;
        private BindingSource binding;
        private Dictionary<string, int> protocolFormat;
        private VersionManager localVersion;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;
        private ToolStripStatusLabel deviceCountLabel;
        private ToolStripStatusLabel selectedCountLabel;
        private ToolStripStatusLabel statusRightSpacerLabel;
        private ToolStripMenuItem viewMenu;
        private ToolStripMenuItem showLiveByVlcMenuItem;
        private ToolStripMenuItem showSelectedByVlcMenuItem;
        private ToolStripMenuItem pingSelectedOnceMenuItem;
        private ToolStripMenuItem showUniqueIdColumnMenuItem;
        private ToolStripProfessionalRenderer modernMenuRenderer;
        private CancellationTokenSource pingCancellationTokenSource;
        private PingMonitorForm activePingMonitor;
        private bool axisDeepDiscoveryActive;
        private readonly object macLookupLock = new object();
        private readonly HashSet<string> pendingMacLookups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object onvifProfileLookupLock = new object();
        private readonly HashSet<string> pendingOnvifProfileLookups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool tiandyAssistScanRunning;

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);
        private Panel headerPanel;
        private Panel toolbarPanel;
        private PictureBox headerIconBox;
        private Label headerTitleLabel;
        private Label headerSubtitleLabel;
        private Label headerTimeLabel;
        private Label headerDateLabel;
        private System.Windows.Forms.Timer headerClockTimer;
        private bool mainDesignApplied;
        private const int MainHeaderHeight = 64;
        private const int MainToolbarHeight = 72;

        // IP change guard:
        // When a Hikvision camera is changed from oldIp -> newIp, background scans may still be probing oldIp.
        // Keep oldIp blocked for a short time so those late probe results cannot create a stale duplicate row.
        private readonly object ipChangeGuardLock = new object();
        private string CurrentBrandFilter = "All";

        private Guid currentScanSessionId = Guid.Empty;

        // Startup performance optimizations
        private readonly Dictionary<string, DateTime> suppressedOldIpAddresses = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> recentChangedIpAddresses = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        enum Columns
        {
            Vendor = 0,
            Protocol = 1,
            Version = 2,
            IPAddress = 3,
            Ping = 4,
            Activation = 5,
            MAC = 6,
            MACVendor = 7,
            Type = 8,
            OnvifProfile = 9,
            UniqueId = 10
        };

        private readonly string[] ColumnNames =
{
    "Vendor",
    "Protocol",
    "Version",
    "IP address",
    "Ping",
    "Activation",
    "MAC Address",
    "OUI Vendor",
    "Model",
    "ONVIF Profile",
    "Unique ID"
};


        public ScannerWindow()
        {
            InitializeComponent();
            RelocateSettingsMenu();
            AddNetworkAdapterProfilesMenuItem();
            
            this.WindowState = FormWindowState.Maximized;
            this.Shown += ScannerWindow_Shown;

            foundDeviceList = new DataTable();

            foundDeviceList.Columns.Add(new DataColumn(ColumnNames[(int)Columns.Vendor], typeof(string)));
            foundDeviceList.Columns.Add(new DataColumn(ColumnNames[(int)Columns.Protocol], typeof(string)));
            foundDeviceList.Columns.Add(new DataColumn(ColumnNames[(int)Columns.Version], typeof(int)));
            foundDeviceList.Columns.Add(new DataColumn(ColumnNames[(int)Columns.IPAddress], typeof(string)));
            foundDeviceList.Columns.Add(new DataColumn(ColumnNames[(int)Columns.Ping], typeof(string)));
            foundDeviceList.Columns.Add(new DataColumn(ColumnNames[(int)Columns.Activation], typeof(string)));
            foundDeviceList.Columns.Add(new DataColumn(ColumnNames[(int)Columns.MAC], typeof(string)));
            foundDeviceList.Columns.Add(new DataColumn(ColumnNames[(int)Columns.MACVendor], typeof(string)));
            foundDeviceList.Columns.Add(new DataColumn(ColumnNames[(int)Columns.Type], typeof(string)));
            foundDeviceList.Columns.Add(new DataColumn(ColumnNames[(int)Columns.OnvifProfile], typeof(string)));
            foundDeviceList.Columns.Add(new DataColumn(ColumnNames[(int)Columns.UniqueId], typeof(string)));

            binding = new BindingSource();
            binding.DataSource = foundDeviceList;
            binding.ListChanged += (s, ev) => UpdateDeviceCountLabel();
            ApplyUnconfiguredDevicesFilter();

            dataGridView1.DataSource = binding;
            dataGridView1.AutoGenerateColumns = true;
            dataGridView1.SelectionChanged += (s, ev) =>
            {
                UpdateSelectedCountLabel();
                UpdateAdvancedSelectedDevicePanel();
            };
            dataGridView1.Refresh();
            BuildStatusBar();
            BuildRightClickMenu();
            InitializeAdvancedArchitecture();
            rightClickMenu.Opening += rightClickMenu_Opening;


            if (dataGridView1.Columns.Contains(ColumnNames[(int)Columns.IPAddress]))
                dataGridView1.Columns[ColumnNames[(int)Columns.IPAddress]].SortMode = DataGridViewColumnSortMode.Programmatic;

            if (dataGridView1.Columns.Contains(ColumnNames[(int)Columns.Version]))
                dataGridView1.Columns[ColumnNames[(int)Columns.Version]].Visible = false;

            if (dataGridView1.Columns.Contains(ColumnNames[(int)Columns.UniqueId]))
                dataGridView1.Columns[ColumnNames[(int)Columns.UniqueId]].Visible = false;

            protocolFormat = new Dictionary<string, int>();
            protocolFormat["Uniview"] = Color.Purple.ToArgb();
            protocolFormat["Sunell"] = Color.DarkCyan.ToArgb();
            localVersion = new VersionManager();
            ApplyMainDesign();

            if (Config.getInstance().DebugMode)
            {
                Logger.getInstance().setLevel(Logger.DebugLevel.Debug);
            }
        }

        private void RelocateSettingsMenu()
        {
            rightClickMenu.Items.Remove(toolStripMenuItem2);
            rightClickMenu.Items.Remove(settingsToolStripMenuItem);

            toolsToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            toolsToolStripMenuItem.DropDownItems.Add(settingsToolStripMenuItem);
        }

        private void AddNetworkAdapterProfilesMenuItem()
        {
            ToolStripMenuItem networkProfilesItem = new ToolStripMenuItem("Network Adapter Profiles...");
            networkProfilesItem.Click += networkAdapterProfilesToolStripMenuItem_Click;

            toolsToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            toolsToolStripMenuItem.DropDownItems.Add(networkProfilesItem);
            AddLoginMenuItem();
        }

        private void AddLoginMenuItem()
        {
            loginToolStripMenuItem = new ToolStripMenuItem("Login");
            loginToolStripMenuItem.Click += (s, ev) =>
            {
                using (UniversalLoginForm form = new UniversalLoginForm())
                {
                    form.ShowDialog(this);
                }
            };

            int toolsIndex = menuStrip1.Items.IndexOf(toolsToolStripMenuItem);
            int insertIndex = toolsIndex >= 0 ? toolsIndex + 1 : menuStrip1.Items.Count;
            menuStrip1.Items.Insert(insertIndex, loginToolStripMenuItem);
        }

        private void singleEnableItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count == 1)
            {
                var row = dataGridView1.SelectedRows[0];
                string ip = row.Cells[(int)Columns.IPAddress].Value?.ToString();
                if (!string.IsNullOrEmpty(ip) && IsHikvisionCompatibleRow(GetDataRowFromGridRow(row)))
                {
                    using (var form = new UniversalScanner.Forms.HikvisionOnvifProvisioningForm(ip))
                    {
                        form.ShowDialog(this);
                    }
                }
                else
                {
                    MessageBox.Show("Please select a single Hikvision device.", "Invalid Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                MessageBox.Show("Please select exactly one device.", "Invalid Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void batchEnableItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count > 0)
            {
                var ips = new System.Collections.Generic.List<string>();
                foreach (DataGridViewRow row in dataGridView1.SelectedRows)
                {
                    string ip = row.Cells[(int)Columns.IPAddress].Value?.ToString();
                    if (!string.IsNullOrEmpty(ip) && IsHikvisionCompatibleRow(GetDataRowFromGridRow(row)))
                    {
                        ips.Add(ip);
                    }
                }
                if (ips.Count > 0)
                {
                    using (var form = new UniversalScanner.Forms.BatchHikvisionOnvifForm(ips))
                    {
                        form.ShowDialog(this);
                    }
                }
                else
                {
                    MessageBox.Show("No valid Hikvision devices selected.", "Invalid Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                MessageBox.Show("Please select at least one Hikvision device.", "Invalid Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void networkAdapterProfilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (NetworkAdapterProfileForm form = new NetworkAdapterProfileForm())
                form.ShowDialog(this);
        }

        private async void axisDeepDiscoveryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!TemporarySubnetService.Instance.IsAdministrator())
            {
                MessageBox.Show(this,
                    "Axis Deep Discovery needs administrator permission so it can temporarily add a 169.254.x.x address to the network adapter.\r\n\r\nRun Universal Scanner as administrator, then try again.",
                    "Axis Deep Discovery",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            TemporarySubnetRefreshContext tempContext = null;
            bool addedTemporaryIp = false;

            try
            {
                currentScanSessionId = Guid.NewGuid();
                axisDeepDiscoveryActive = true;
                await ShowStatusAsync("Axis Deep Discovery: preparing 169.254.x.x discovery...");

                string adapterHintIp = GetSubnetTextAdapterHintIp();
                string adapterAlias = !string.IsNullOrWhiteSpace(adapterHintIp)
                    ? NetworkTools.GetBestAdapterNameForTarget(adapterHintIp, "255.255.255.0")
                    : "";
                if (string.IsNullOrWhiteSpace(adapterAlias))
                    adapterAlias = NetworkTools.GetBestAdapterNameForTarget("169.254.1.1", "255.255.0.0", adapterHintIp);

                if (!NetworkTools.IsIpOnLocalSubnetForAdapter(adapterAlias, "169.254.1.1", "255.255.0.0"))
                {
                    IPAddress target = IPAddress.Parse("169.254.1.1");
                    IPAddress mask = IPAddress.Parse("255.255.0.0");
                    IPAddress tempIp = TemporarySubnetService.Instance.FindAvailableTemporaryIp(target, mask);

                    if (tempIp == null || string.IsNullOrWhiteSpace(adapterAlias))
                    {
                        MessageBox.Show(this,
                            "Axis Deep Discovery could not find a usable temporary 169.254.x.x address or adapter.",
                            "Axis Deep Discovery",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    await ShowStatusAsync("Axis Deep Discovery: adding temporary " + tempIp + " / 255.255.0.0...");
                    TemporarySubnetService.AddIpResult addResult = await TemporarySubnetService.Instance.AddTemporaryIpAsync(adapterAlias, tempIp, mask, null);
                    if (addResult == null || !addResult.Success)
                    {
                        MessageBox.Show(this,
                            "Axis Deep Discovery could not add the temporary 169.254.x.x address.",
                            "Axis Deep Discovery",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    tempContext = new TemporarySubnetRefreshContext
                    {
                        AdapterAlias = adapterAlias,
                        TemporaryIp = tempIp,
                        CameraIp = null
                    };
                    addedTemporaryIp = true;
                    await Task.Delay(1200);
                }

                await ShowStatusAsync("Axis Deep Discovery: scanning for Axis cameras...");
                RunAxisDiscoveryEngines();
                await RunAxisDeepDiscoveryProbeAsync(tempContext?.TemporaryIp);
                await Task.Delay(90000);
                await ShowStatusAsync("Axis Deep Discovery finished.", 2500);
            }
            finally
            {
                axisDeepDiscoveryActive = false;
                if (addedTemporaryIp && tempContext != null)
                    await CleanupAxisTemporarySubnetForRefreshAsync(tempContext);
            }
        }

        private void RunAxisDiscoveryEngines()
        {
            if (Program.Engines == null)
                return;

            foreach (ScanEngine engine in Program.Engines)
            {
                if (engine == null)
                    continue;

                if (string.Equals(engine.name, "Axis", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(engine.name, "WSDiscovery", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        engine.scan();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private string GetSubnetTextAdapterHintIp()
        {
            string subnet = subnetTextBox?.Text?.Trim() ?? "";
            if (IPAddress.TryParse(subnet, out IPAddress parsed))
                return parsed.ToString();

            string[] parts = subnet.Split('.');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out int a) &&
                int.TryParse(parts[1], out int b) &&
                int.TryParse(parts[2], out int c) &&
                a >= 0 && a <= 255 &&
                b >= 0 && b <= 255 &&
                c >= 0 && c <= 255)
            {
                return a + "." + b + "." + c + ".1";
            }

            return "";
        }

        private async Task RunAxisDeepDiscoveryProbeAsync(IPAddress sourceIp)
        {
            byte[] probeBytes = Encoding.UTF8.GetBytes(CreateAxisDeepDiscoveryProbeXml());
            List<IPEndPoint> targets = new List<IPEndPoint>
            {
                new IPEndPoint(IPAddress.Parse("239.255.255.250"), 3702),
                new IPEndPoint(IPAddress.Broadcast, 3702)
            };

            using (UdpClient udp = new UdpClient(AddressFamily.InterNetwork))
            {
                udp.EnableBroadcast = true;
                udp.Client.ReceiveTimeout = 1000;

                IPEndPoint bindPoint = sourceIp == null
                    ? new IPEndPoint(IPAddress.Any, 0)
                    : new IPEndPoint(sourceIp, 0);

                udp.Client.Bind(bindPoint);

                foreach (IPEndPoint target in targets)
                {
                    try
                    {
                        await udp.SendAsync(probeBytes, probeBytes.Length, target);
                    }
                    catch
                    {
                    }
                }

                DateTime until = DateTime.Now.AddSeconds(30);
                while (DateTime.Now < until)
                {
                    try
                    {
                        UdpReceiveResult result = await udp.ReceiveAsync();
                        HandleAxisDeepDiscoveryResponse(result.RemoteEndPoint, result.Buffer);
                    }
                    catch (SocketException)
                    {
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void HandleAxisDeepDiscoveryResponse(IPEndPoint from, byte[] data)
        {
            if (from == null || data == null || data.Length == 0)
                return;

            string xml;
            try
            {
                xml = Encoding.UTF8.GetString(data);
            }
            catch
            {
                return;
            }

            if (xml.IndexOf("ProbeMatch", StringComparison.OrdinalIgnoreCase) < 0 ||
                (xml.IndexOf("axis.com", StringComparison.OrdinalIgnoreCase) < 0 &&
                 xml.IndexOf("Axis Communications", StringComparison.OrdinalIgnoreCase) < 0 &&
                 xml.IndexOf("/manufacturer/Axis", StringComparison.OrdinalIgnoreCase) < 0))
                return;

            string model = ExtractAxisScopeValue(xml, "hardware");
            if (string.IsNullOrWhiteSpace(model))
                model = ExtractAxisScopeValue(xml, "name");
            if (string.IsNullOrWhiteSpace(model))
                model = "Axis";

            string serial = ExtractAxisScopeValue(xml, "serial");
            if (string.IsNullOrWhiteSpace(serial))
                serial = ExtractAxisScopeValue(xml, "mac");
            if (string.IsNullOrWhiteSpace(serial))
                serial = "unknown";

            deviceFound("Axis", 0, from.Address, model, serial);
        }

        private string ExtractAxisScopeValue(string xml, string key)
        {
            if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(key))
                return "";

            Match match = Regex.Match(
                xml,
                @"onvif://www\.onvif\.org/[^/\s<>]*/" + Regex.Escape(key) + @"/([^/\s<>]+)",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return "";

            return Uri.UnescapeDataString(match.Groups[1].Value)
                .Replace('_', ' ')
                .Trim();
        }

        private string CreateAxisDeepDiscoveryProbeXml()
        {
            return "<s:Envelope" +
                   " xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"" +
                   " xmlns:a=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\"" +
                   " xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\"" +
                   " xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\">" +
                   "<s:Header>" +
                   "<a:Action s:mustUnderstand=\"1\">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</a:Action>" +
                   "<a:MessageID>urn:uuid:" + Guid.NewGuid() + "</a:MessageID>" +
                   "<a:ReplyTo><a:Address>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</a:Address></a:ReplyTo>" +
                   "<a:To s:mustUnderstand=\"1\">urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To>" +
                   "</s:Header>" +
                   "<s:Body>" +
                   "<d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe>" +
                   "</s:Body>" +
                   "</s:Envelope>";
        }

        private async void scanButton_Click(object sender, EventArgs e)
        {
            await StartAutoScanAsync();
        }

        private async Task StartAutoScanAsync()
        {
            await StopActiveSubnetScanAsync();

            currentScanSessionId = Guid.NewGuid();
            Logger.getInstance().WriteLine(Logger.DebugLevel.Info, $"[Normal Scan] StartAutoScanAsync entered. SessionID: {currentScanSessionId}");

            ClearDeviceListPreservingRecentIpChanges();

            if (scanProgressBar != null)
                scanProgressBar.Value = 0;

            this.Text = "Universal Scanner";
            SetStatusMessage("Normal scan started");

            _normalScanFilterActive = true;

            Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "[Normal Scan] scanEvent fired");
            if (scanEvent != null)
                scanEvent.Invoke();
        }

        private async Task StopActiveSubnetScanAsync()
        {
            while (true)
            {
                Task subnetScanTask = activeSubnetScanTask;
                if (subnetScanTask == null || subnetScanTask.IsCompleted)
                    return;

                RequestSubnetScanStop();

                try
                {
                    await subnetScanTask;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        private bool RequestSubnetScanStop()
        {
            Task subnetScanTask = activeSubnetScanTask;
            if (subnetScanTask == null || subnetScanTask.IsCompleted)
                return false;

            stopSubnetScan = true;

            CancellationTokenSource cancellation = activeSubnetScanCancellation;
            if (cancellation == null || cancellation.IsCancellationRequested)
                return true;

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (AggregateException ex)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Warn, "Subnet scan cancellation callback failed: " + ex.Message);
            }

            return true;
        }

        private async Task RunTrackedSubnetScanAsync(Func<CancellationToken, Task> scanAction)
        {
            Task currentTask = activeSubnetScanTask;
            if (currentTask != null && !currentTask.IsCompleted)
            {
                int stopRequestVersion = subnetStopRequestVersion;
                RequestSubnetScanStop();
                await StopActiveSubnetScanAsync();

                if (stopRequestVersion != subnetStopRequestVersion)
                    return;
            }

            stopSubnetScan = false;
            currentSubnetScanIndex = 0;
            currentSubnetScanIp = "";
            int stopRequestVersionAtStart = subnetStopRequestVersion;
            CancellationTokenSource cancellation = new CancellationTokenSource();
            activeSubnetScanCancellation = cancellation;
            Task subnetScanTask = null;
            bool scanFailed = false;

            try
            {
                subnetScanTask = scanAction(cancellation.Token);
                activeSubnetScanTask = subnetScanTask;
                await subnetScanTask;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                scanFailed = true;
                Logger.getInstance().WriteLine(Logger.DebugLevel.Error, "Subnet scan failed: " + ex);
                SetSubnetScanStatus("Subnet scan failed");
            }
            finally
            {
                if (subnetScanTask != null && object.ReferenceEquals(activeSubnetScanTask, subnetScanTask))
                    activeSubnetScanTask = Task.CompletedTask;

                if (object.ReferenceEquals(activeSubnetScanCancellation, cancellation))
                    activeSubnetScanCancellation = null;

                cancellation.Dispose();
            }

            if (!scanFailed && stopRequestVersionAtStart != subnetStopRequestVersion)
                SetSubnetScanStatus(BuildSubnetScanStoppedStatus());
        }

        private void stopSubnetButton_Click(object sender, EventArgs e)
        {
            subnetStopRequestVersion++;
            RequestSubnetScanStop();
        }

        public void deviceFound(string protocol, int version, IPAddress deviceIP, string deviceType, string deviceUUID)
        {
            if (IsDisposed)
                return;

            if (deviceIP.AddressFamily == AddressFamily.InterNetwork && !Config.getInstance().EnableIPv4)
                return;

            if (deviceIP.AddressFamily == AddressFamily.InterNetworkV6 && !Config.getInstance().EnableIPv6)
                return;

            if (axisDeepDiscoveryActive &&
                !IsAxisText(protocol) &&
                !IsAxisText(deviceType) &&
                !IsAxisText(deviceUUID))
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => addDevice(protocol, version, deviceIP.ToString(), deviceType, deviceUUID)));
            }
            else
            {
                addDevice(protocol, version, deviceIP.ToString(), deviceType, deviceUUID);
            }
        }

        private void addDevice(string protocol, int version, string deviceIP, string deviceType, string deviceUUID)
        {
            if (IsOldIpSuppressed(deviceIP))
                return;

            bool isDahuaVms = IsDahuaVmsType(deviceType);
            bool isDahuaDiscovery = !isDahuaVms &&
                                    (IsDahuaText(protocol) || IsDahuaText(deviceType) || IsDahuaText(deviceUUID));
            bool isDahuaSwitch = isDahuaDiscovery && IsDahuaSwitchType(deviceType);
            UniviewDiscoveryInfo univiewDiscoveryInfo;
            // Uniview discovery supplies the MAC or serial as deviceUUID. Use the
            // matching identity index before falling back to IP; several factory
            // default cameras can legitimately answer from the same IP address.
            bool hasUniviewDiscoveryInfo = IsMacAddressText(deviceUUID)
                ? UniviewDiscoveryCache.TryGet(deviceIP, deviceUUID, "", out univiewDiscoveryInfo)
                : UniviewDiscoveryCache.TryGet(deviceIP, "", deviceUUID, out univiewDiscoveryInfo);
            string discoveredMac = hasUniviewDiscoveryInfo ? NormalizeMac(univiewDiscoveryInfo.Mac) : "";
            if (string.IsNullOrWhiteSpace(discoveredMac) &&
                (IsBoschText(protocol) || IsBoschText(deviceType) || isDahuaDiscovery) &&
                IsMacAddressText(deviceUUID))
            {
                discoveredMac = NormalizeMac(deviceUUID);
            }
            string discoveredUniqueId = hasUniviewDiscoveryInfo
                ? FirstNonEmpty(univiewDiscoveryInfo.Mac, univiewDiscoveryInfo.Serial, deviceUUID)
                : deviceUUID;

            DataRow existingRow = hasUniviewDiscoveryInfo
                ? FindDeviceRow(deviceIP, discoveredMac, univiewDiscoveryInfo.Serial)
                : null;
            if (existingRow == null)
            {
                existingRow = isDahuaDiscovery && IsDahuaFactoryDefaultIp(deviceIP)
                    ? FindDahuaFactoryDefaultDiscoveryRow(deviceIP, discoveredMac, discoveredUniqueId)
                    : FindDeviceRow(deviceIP, discoveredMac, discoveredUniqueId);
            }
            string displayDeviceUuid = NormalizeUniqueIdForDisplay(deviceUUID);
            if (hasUniviewDiscoveryInfo)
                displayDeviceUuid = NormalizeUniqueIdForDisplay(discoveredUniqueId);

            if (existingRow == null)
            {
                if (_normalScanFilterActive)
                {
                    string allowed = Config.getInstance().EnabledDiscoveryEngines;
                    if (!string.IsNullOrEmpty(allowed))
                    {
                        var selectedBrands = new HashSet<string>(allowed.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);

                        string normVendorFromProtocol = BrandEngineMapper.NormalizeVendor(protocol);
                        string normVendorFromDeviceType = BrandEngineMapper.NormalizeVendor(deviceType);
                        
                        bool accepted = selectedBrands.Contains(normVendorFromProtocol) ||
                                        selectedBrands.Contains(normVendorFromDeviceType) ||
                                        IsGenericSharedDiscoveryResult(protocol, deviceType, deviceUUID);
                        
                        if (!accepted && !string.IsNullOrWhiteSpace(deviceUUID))
                        {
                            string normVendorFromUUID = BrandEngineMapper.NormalizeVendor(deviceUUID);
                            if (selectedBrands.Contains(normVendorFromUUID))
                                accepted = true;
                        }
                        
                        if (!accepted)
                        {
                            return; // drop result
                        }
                    }
                }

                // Do not start automatic ping / activation refresh when the scanner opens.
                // The light startup scan should only list discovered devices.
                // Full refresh is done only after Change IP, or when the user runs Scan Subnet.
                string dahuaDiscoveryStatus = isDahuaDiscovery
                    ? DeviceSDK.Dahua.DahuaDiscoveryState.GetActivationStatus(deviceIP, deviceUUID)
                    : null;
                string dahuaModel = isDahuaDiscovery
                    ? DeviceSDK.Dahua.DahuaDiscoveryState.GetDeviceModel(deviceIP, "", deviceUUID)
                    : null;
                bool isUnconfirmedDahuaUpnpSdk = isDahuaDiscovery &&
                    IsDahuaPrivateUpnpText(deviceType) &&
                    string.IsNullOrWhiteSpace(dahuaModel);

                bool isBoschDiscovery = IsBoschText(protocol) || IsBoschText(deviceType) || IsBoschText(deviceUUID);
                bool isAxisDiscovery = IsAxisText(protocol) || IsAxisText(deviceType) || IsAxisText(deviceUUID);

                string initialActivation = isDahuaSwitch
                    ? "N/A (Switch)"
                    : !string.IsNullOrWhiteSpace(dahuaDiscoveryStatus)
                    ? dahuaDiscoveryStatus
                    : IsHikvisionText(protocol) || IsHikvisionText(deviceType) || IsHikvisionText(deviceUUID)
                    ? "Unknown"
                    : isBoschDiscovery
                    ? "Active"
                    : isAxisDiscovery
                    ? "Unknown"
                    : "N/A";

                foundDeviceList.Rows.Add(
                    isDahuaVms ? "VMS/Windows" : protocol,
                    isDahuaVms ? "Discovery" : protocol,
                    version,
                    deviceIP,
                    "Not checked",
                    initialActivation,
                    discoveredMac,
                    "",
                    hasUniviewDiscoveryInfo && !string.IsNullOrWhiteSpace(univiewDiscoveryInfo.Model)
                        ? univiewDiscoveryInfo.Model
                        : isUnconfirmedDahuaUpnpSdk ? "Dahua UPnP response (model unknown)"
                        : !string.IsNullOrWhiteSpace(dahuaModel) && IsGenericDahuaType(deviceType) ? dahuaModel : deviceType,
                    hasUniviewDiscoveryInfo && !string.IsNullOrWhiteSpace(univiewDiscoveryInfo.OnvifProfiles)
                        ? NormalizeOnvifProfileDisplay(univiewDiscoveryInfo.OnvifProfiles)
                        : "Unknown",
                    displayDeviceUuid);

                QueueMacLookup(deviceIP, displayDeviceUuid);
                if (!hasUniviewDiscoveryInfo || string.IsNullOrWhiteSpace(univiewDiscoveryInfo.OnvifProfiles) || univiewDiscoveryInfo.OnvifProfiles == "Unknown")
                    QueueOnvifProfileLookup(deviceIP, displayDeviceUuid);
                return;
            }

            string oldIp = existingRow[(int)Columns.IPAddress]?.ToString();
            bool ipChanged = !string.Equals(oldIp, deviceIP, StringComparison.OrdinalIgnoreCase);

            string currentMac = existingRow[(int)Columns.MAC]?.ToString();
            bool needsMac = string.IsNullOrWhiteSpace(currentMac);

            string currentOnvif = existingRow[(int)Columns.OnvifProfile]?.ToString();
            bool needsOnvif = string.IsNullOrWhiteSpace(currentOnvif) || currentOnvif == "Unknown";

            string currentAct = existingRow[(int)Columns.Activation]?.ToString();
            bool needsAct =
                string.IsNullOrWhiteSpace(currentAct) ||
                currentAct == "Unknown" ||
                currentAct == "N/A";

            existingRow.BeginEdit();

            int existingVersion = 0;
            try
            {
                existingVersion = existingRow.Field<int>((int)Columns.Version);
            }
            catch
            {
                existingVersion = 0;
            }

            // Important: update the existing row IP instead of adding a new row.
            // This is what prevents duplicates during IP changes.
            existingRow[(int)Columns.IPAddress] = deviceIP;
            if (!string.IsNullOrWhiteSpace(discoveredMac))
                existingRow[(int)Columns.MAC] = discoveredMac;

            bool isExistingDahuaVms = IsDahuaVmsType(existingRow[(int)Columns.Type]?.ToString());
            bool isDahuaVmsUpdate = isDahuaVms || isExistingDahuaVms;
            bool preserveExistingIdentity = ShouldPreserveExistingIdentity(existingRow, protocol, deviceType);

            if (!preserveExistingIdentity && !string.IsNullOrWhiteSpace(protocol))
            {
                UpdateDataRowField(deviceIP, displayDeviceUuid, (int)Columns.Vendor, isDahuaVmsUpdate ? "VMS/Windows" : protocol, this.currentScanSessionId, discoveredMac);
                UpdateDataRowField(deviceIP, displayDeviceUuid, (int)Columns.Protocol, isDahuaVmsUpdate ? "Discovery" : protocol, this.currentScanSessionId, discoveredMac);
            }

            if (version > existingVersion)
                existingRow[(int)Columns.Version] = version;

            if (hasUniviewDiscoveryInfo && !string.IsNullOrWhiteSpace(univiewDiscoveryInfo.Model))
                UpdateDataRowField(deviceIP, displayDeviceUuid, (int)Columns.Type, univiewDiscoveryInfo.Model, this.currentScanSessionId, discoveredMac);
            else if (!preserveExistingIdentity && !string.IsNullOrWhiteSpace(deviceType))
                UpdateDataRowField(deviceIP, displayDeviceUuid, (int)Columns.Type, deviceType, this.currentScanSessionId, discoveredMac);

            if (hasUniviewDiscoveryInfo && !string.IsNullOrWhiteSpace(univiewDiscoveryInfo.OnvifProfiles))
                existingRow[(int)Columns.OnvifProfile] = NormalizeOnvifProfileDisplay(univiewDiscoveryInfo.OnvifProfiles);

            if (!string.IsNullOrWhiteSpace(discoveredUniqueId))
                existingRow[(int)Columns.UniqueId] = displayDeviceUuid;

            if (IsDahuaRow(existingRow))
                ApplyDahuaIdentity(existingRow);
            else if (IsBoschText(protocol) || IsBoschText(deviceType))
                ApplyBoschIdentity(existingRow);

            if (IsDahuaSwitchRow(existingRow))
                existingRow[(int)Columns.Activation] = "N/A (Switch)";

            string existingDahuaStatus = IsDahuaRow(existingRow)
                ? DeviceSDK.Dahua.DahuaDiscoveryState.GetActivationStatus(deviceIP, existingRow[(int)Columns.UniqueId]?.ToString())
                : null;
            if (!string.IsNullOrWhiteSpace(existingDahuaStatus))
                existingRow[(int)Columns.Activation] = existingDahuaStatus;

            existingRow.EndEdit();

            if (!(isDahuaDiscovery && IsDahuaFactoryDefaultIp(deviceIP)))
                RemoveDuplicateRowsForIpChange("", deviceIP, existingRow);

            Guid capturedSessionId = this.currentScanSessionId;

            if (ipChanged || needsMac)
                QueueMacLookup(deviceIP, deviceUUID);
            else if (needsAct && (IsHikvisionCompatibleRow(existingRow) || IsUniviewRow(existingRow) || IsDahuaRow(existingRow) || IsSunellRow(existingRow) || IsTiandyRow(existingRow) || IsBoschRow(existingRow) || IsAxisRow(existingRow)))
                UpdateActivationStatus(deviceIP, capturedSessionId);

            if (ipChanged || needsOnvif)
                QueueOnvifProfileLookup(deviceIP, deviceUUID);

            // Do not start background refresh from normal discovery.
            // This prevents the app from keeping devices in Refreshing/Checking state after startup.
        }

        private void QueueMacLookup(string ip, string uniqueId)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return;

            string lookupKey = ip.Trim();
            Guid capturedSessionId = this.currentScanSessionId;

            lock (macLookupLock)
            {
                if (!pendingMacLookups.Add(lookupKey))
                    return;
            }

            Task.Run(async () =>
            {
                // NetworkTools.GetMacAsync actively sends ARP requests via SendARP (P/Invoke)
                // instead of reading the Windows ARP cache with "arp -a".
                // The ARP cache is empty for devices discovered via UDP multicast because
                // no TCP/ICMP contact has been made yet, so the old shell-based approach
                // always returned empty for freshly found devices.
                var sw_mac = System.Diagnostics.Stopwatch.StartNew(); string mac = await NetworkTools.GetMacAsync(lookupKey, 3, 300); sw_mac.Stop(); StartupDiagnostics.Increment("MacProbes"); StartupDiagnostics.AddTimer("MacProbes", sw_mac.ElapsedMilliseconds); StartupDiagnostics.LogSlowOperation("MAC", lookupKey, "Unknown", sw_mac.ElapsedMilliseconds, !string.IsNullOrEmpty(mac));
                string macVendor = NetworkTools.GetVendor(mac);

                if (IsDisposed)
                    return;

                try
                {
                    BeginInvoke(new MethodInvoker(() => ApplyMacLookupResult(lookupKey, uniqueId, mac, macVendor, capturedSessionId)));
                }
                catch
                {
                    lock (macLookupLock)
                    {
                        pendingMacLookups.Remove(lookupKey);
                    }
                }
            });
        }

        private void QueueOnvifProfileLookup(string ip, string uniqueId = "")
        {
            if (string.IsNullOrWhiteSpace(ip))
                return;

            string lookupKey = ip.Trim();
            Guid capturedSessionId = this.currentScanSessionId;

            lock (onvifProfileLookupLock)
            {
                if (!pendingOnvifProfileLookups.Add(lookupKey))
                    return;
            }

            Task.Run(async () =>
            {
                string profiles = await DeviceSDK.Common.OnvifProfileProbe.GetProfilesAsync(lookupKey);

                if (IsDisposed)
                    return;

                try
                {
                    BeginInvoke(new MethodInvoker(() => ApplyOnvifProfileLookupResult(lookupKey, uniqueId, profiles, capturedSessionId)));
                }
                catch
                {
                    lock (onvifProfileLookupLock)
                    {
                        pendingOnvifProfileLookups.Remove(lookupKey);
                    }
                }
            });
        }

        private void ApplyOnvifProfileLookupResult(string ip, string uniqueId, string profiles, Guid capturedSessionId)
        {
            lock (onvifProfileLookupLock)
            {
                pendingOnvifProfileLookups.Remove(ip);
            }

            if (capturedSessionId != Guid.Empty && capturedSessionId != this.currentScanSessionId)
                return;

            DataRow targetRow = FindDeviceRow(ip, "", uniqueId);
            if (targetRow == null || targetRow.RowState == DataRowState.Deleted)
                return;

            targetRow[(int)Columns.OnvifProfile] =
                NormalizeOnvifProfileDisplay(profiles);
        }

        private void ApplyMacLookupResult(string ip, string uniqueId, string mac, string macVendor, Guid capturedSessionId)
        {
            lock (macLookupLock)
            {
                pendingMacLookups.Remove(ip);
            }
            
            if (capturedSessionId != Guid.Empty && capturedSessionId != this.currentScanSessionId)
            {
                return;
            }

            // Resolve the target row even when MAC is empty so we can still
            // trigger the activation status check for Hikvision devices below.
            DataRow targetRow = FindDeviceRow(ip, "", uniqueId);

            if (!string.IsNullOrWhiteSpace(mac))
            {
                DataRow macRow = FindDeviceRow("", mac, "");

                if (targetRow == null)
                {
                    targetRow = macRow;
                }
                else if (macRow != null && !object.ReferenceEquals(targetRow, macRow))
                {
                    string targetVendor = targetRow[(int)Columns.Vendor]?.ToString() ?? "";
                    string macVendorRow = macRow[(int)Columns.Vendor]?.ToString() ?? "";
                    string targetMac = NormalizeMac(targetRow[(int)Columns.MAC]?.ToString());
                    string macRowMac = NormalizeMac(macRow[(int)Columns.MAC]?.ToString());
                    string targetUniqueId = targetRow[(int)Columns.UniqueId]?.ToString()?.Trim() ?? "";
                    string macRowUniqueId = macRow[(int)Columns.UniqueId]?.ToString()?.Trim() ?? "";
                    
                    bool safeToMerge = true;
                    if (!string.IsNullOrWhiteSpace(targetMac) && !string.IsNullOrWhiteSpace(macRowMac) && targetMac != macRowMac) safeToMerge = false;
                    if (!string.IsNullOrWhiteSpace(targetVendor) && targetVendor != "Unknown" && !string.IsNullOrWhiteSpace(macVendorRow) && macVendorRow != "Unknown" && targetVendor != macVendorRow) safeToMerge = false;
                    if (!string.IsNullOrWhiteSpace(targetUniqueId) && !string.IsNullOrWhiteSpace(macRowUniqueId) && targetUniqueId != macRowUniqueId) safeToMerge = false;
                    
                    if (safeToMerge)
                    {
                        MergeDeviceRows(targetRow, macRow);
                    }
                    else
                    {
                        // Conflict. Prefer the row found by MAC as the true identity, and update its IP.
                        targetRow = macRow;
                        targetRow[(int)Columns.IPAddress] = ip;
                    }
                }
            }

            if (targetRow == null || targetRow.RowState == DataRowState.Deleted)
                return;

            if (IsStaleHoneywellIdentityForIncomingMac(targetRow, mac, macVendor, null))
                ResetRowToCurrentGenericIdentity(targetRow, null, macVendor);

            if (!string.IsNullOrWhiteSpace(mac))
                targetRow[(int)Columns.MAC] = mac;

            // Populate OUI vendor now that we have the MAC.
            if (!string.IsNullOrWhiteSpace(macVendor))
                targetRow[(int)Columns.MACVendor] = macVendor;

            // If the MAC/OUI identifies it as a known vendor, restore the vendor identity.
            if (IsHoneywellOuiVendor(macVendor))
            {
                ApplyHoneywellIdentity(targetRow);
            }
            else if (IsGenericDiscoveryRow(targetRow) && IsUniviewOuiVendor(macVendor))
            {
                ApplyUniviewIdentity(targetRow);
            }
            else if (IsGenericDiscoveryRow(targetRow) && IsDahuaText(macVendor))
            {
                ApplyDahuaIdentity(targetRow);
            }
            else if (IsGenericDiscoveryRow(targetRow) && IsSunellOuiVendor(macVendor))
            {
                ApplySunellIdentity(targetRow);
            }

            // Kick off an activation status check for supported rows.
            // This is done here (after MAC/ARP resolution) rather than at discovery time
            // because the device must be reachable on the network before the HTTP probe works.
            // It runs regardless of whether the MAC resolved, since a device can respond to
            // HTTP even when it doesn't answer ARP.
            string rowIp = targetRow[(int)Columns.IPAddress]?.ToString() ?? ip;
            if (IsHikvisionCompatibleRow(targetRow) || IsUniviewRow(targetRow) || IsDahuaRow(targetRow) || IsSunellRow(targetRow) || IsTiandyRow(targetRow) || IsBoschRow(targetRow) || IsAxisRow(targetRow))
                UpdateActivationStatus(rowIp, capturedSessionId);
        }

        private void MergeDeviceRows(DataRow keepRow, DataRow removeRow)
        {
            if (keepRow == null || removeRow == null || object.ReferenceEquals(keepRow, removeRow))
                return;

            for (int i = 0; i < foundDeviceList.Columns.Count; i++)
            {
                string keepValue = keepRow[i]?.ToString();
                string removeValue = removeRow[i]?.ToString();

                if (string.IsNullOrWhiteSpace(keepValue) && !string.IsNullOrWhiteSpace(removeValue))
                    keepRow[i] = removeRow[i];
            }

            foundDeviceList.Rows.Remove(removeRow);
        }

        private void RegisterIpChangeGuard(string oldIp)
        {
            if (string.IsNullOrWhiteSpace(oldIp))
                return;

            lock (ipChangeGuardLock)
            {
                suppressedOldIpAddresses[oldIp.Trim()] = DateTime.Now.AddMinutes(5);
            }
        }

        private void RegisterRecentChangedIp(string newIp)
        {
            if (string.IsNullOrWhiteSpace(newIp))
                return;

            lock (ipChangeGuardLock)
            {
                recentChangedIpAddresses[newIp.Trim()] = DateTime.Now.AddMinutes(5);
            }
        }

        private bool IsOldIpSuppressed(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            lock (ipChangeGuardLock)
            {
                string cleanIp = ip.Trim();

                if (!suppressedOldIpAddresses.TryGetValue(cleanIp, out DateTime until))
                    return false;

                if (DateTime.Now <= until)
                    return true;

                suppressedOldIpAddresses.Remove(cleanIp);
                return false;
            }
        }

        private bool IsRecentChangedIp(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            lock (ipChangeGuardLock)
            {
                string cleanIp = ip.Trim();

                if (!recentChangedIpAddresses.TryGetValue(cleanIp, out DateTime until))
                    return false;

                if (DateTime.Now <= until)
                    return true;

                recentChangedIpAddresses.Remove(cleanIp);
                return false;
            }
        }

        private void ClearDeviceListPreservingRecentIpChanges()
        {
            if (foundDeviceList == null)
                return;

            List<DataRow> rowsToRemove = new List<DataRow>();

            foreach (DataRow row in foundDeviceList.Rows)
            {
                if (row == null || row.RowState == DataRowState.Deleted)
                    continue;

                string ip = row[(int)Columns.IPAddress]?.ToString();
                if (!IsRecentChangedIp(ip))
                    rowsToRemove.Add(row);
            }

            foreach (DataRow row in rowsToRemove)
                foundDeviceList.Rows.Remove(row);
        }

        private bool IsHikvisionText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("Hikvision", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsHikvisionRow(DataRow row)
        {
            if (row == null)
                return false;

            return IsHikvisionText(row[(int)Columns.Vendor]?.ToString()) ||
                   IsHikvisionText(row[(int)Columns.Protocol]?.ToString()) ||
                   IsHikvisionText(row[(int)Columns.Type]?.ToString()) ||
                   IsHikvisionText(row[(int)Columns.UniqueId]?.ToString());
        }

        private bool IsHoneywellText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("Honeywell", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsBoschText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   (value.IndexOf("Bosch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("VCS Video", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("Video Communication Systems", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private bool IsAxisText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("Axis", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsAxisRow(DataRow row)
        {
            if (row == null)
                return false;

            return IsAxisText(row[(int)Columns.Vendor]?.ToString()) ||
                   IsAxisText(row[(int)Columns.Protocol]?.ToString()) ||
                   IsAxisText(row[(int)Columns.Type]?.ToString()) ||
                   IsAxisOuiVendor(row[(int)Columns.MACVendor]?.ToString());
        }

        private bool IsAxisOuiVendor(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("Axis Communications", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsBoschRow(DataRow row)
        {
            if (row == null)
                return false;

            return IsBoschText(row[(int)Columns.Vendor]?.ToString()) ||
                   IsBoschText(row[(int)Columns.Protocol]?.ToString()) ||
                   IsBoschText(row[(int)Columns.MACVendor]?.ToString()) ||
                   IsBoschText(row[(int)Columns.Type]?.ToString());
        }

        private bool IsHoneywellRow(DataRow row)
        {
            if (row == null)
                return false;

            return IsHoneywellText(row[(int)Columns.Vendor]?.ToString()) ||
                   IsHoneywellText(row[(int)Columns.Protocol]?.ToString()) ||
                   IsHoneywellOuiVendor(row[(int)Columns.MACVendor]?.ToString());
        }

        private bool IsHoneywellHc35Row(DataRow row)
        {
            if (row == null || !IsHoneywellRow(row))
                return false;

            return IsHoneywellHc35Text(row[(int)Columns.Type]?.ToString()) ||
                   IsHoneywellHc35Text(row[(int)Columns.UniqueId]?.ToString());
        }

        private bool IsHoneywellHc35Text(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("HC35", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsHoneywellHc30Or35Text(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   (value.IndexOf("HC30", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("HC35", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private bool IsHoneywellHc30Or35Row(DataRow row)
        {
            if (row == null || !IsHoneywellRow(row))
                return false;

            return IsHoneywellHc30Or35Text(row[(int)Columns.Type]?.ToString()) ||
                   IsHoneywellHc30Or35Text(row[(int)Columns.UniqueId]?.ToString());
        }

        private string NormalizeHoneywellActivationStatus(DataRow row, string status)
        {
            if (row == null || !IsHoneywellRow(row))
                return status;

            if (string.Equals(status, "Inactive", StringComparison.OrdinalIgnoreCase))
                return status;

            if (string.IsNullOrWhiteSpace(status) ||
                status.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                return "Unknown";

            return status;
        }

        private string NormalizeHoneywellActivationStatus(string modelOrType, string status)
        {
            if (string.Equals(status, "Inactive", StringComparison.OrdinalIgnoreCase))
                return status;

            if (string.IsNullOrWhiteSpace(status) ||
                status.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                return "Unknown";

            return status;
        }

        private bool IsGenericWsDiscoveryText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("WSDiscovery", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsGenericSsdpText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("SSDP", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsGenericNetworkVideoType(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("NetworkVideoTransmitter", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsGenericDahuaType(string value)
        {
            return string.IsNullOrWhiteSpace(value) ||
                   value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                   IsDahuaPrivateUpnpText(value) ||
                   IsGenericNetworkVideoType(value) ||
                   IsGenericWsDiscoveryText(value) ||
                   IsGenericSsdpText(value);
        }

        private bool IsGenericUniviewType(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Equals("Uniview Device", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsGenericDiscoveryUpdate(string protocol, string deviceType)
        {
            return IsGenericWsDiscoveryText(protocol) ||
                   IsGenericSsdpText(protocol) ||
                   IsGenericNetworkVideoType(deviceType);
        }

        private bool IsGenericSharedDiscoveryResult(string protocol, string deviceType, string deviceUUID)
        {
            return IsGenericWsDiscoveryText(protocol) ||
                   IsGenericSsdpText(protocol) ||
                   IsGenericNetworkVideoType(deviceType) ||
                   IsGenericUpnpText(deviceType) ||
                   IsGenericUpnpText(deviceUUID);
        }

        private bool IsGenericDiscoveryRow(DataRow row)
        {
            if (row == null)
                return false;

            return IsGenericWsDiscoveryText(row[(int)Columns.Vendor]?.ToString()) ||
                   IsGenericWsDiscoveryText(row[(int)Columns.Protocol]?.ToString()) ||
                   IsGenericSsdpText(row[(int)Columns.Vendor]?.ToString()) ||
                   IsGenericSsdpText(row[(int)Columns.Protocol]?.ToString()) ||
                   IsGenericUpnpText(row[(int)Columns.Type]?.ToString()) ||
                   IsGenericNetworkVideoType(row[(int)Columns.Type]?.ToString());
        }

        private bool IsGenericUpnpText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("UPnP", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsHoneywellOuiVendor(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.IndexOf("Honeywell", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("Alerton", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsStaleHoneywellIdentityForIncomingMac(DataRow row, string incomingMac, string incomingMacVendor, VendorProbeResult detected)
        {
            if (row == null || !IsHoneywellRow(row))
                return false;

            string currentMac = NormalizeMac(row[(int)Columns.MAC]?.ToString());
            string newMac = NormalizeMac(incomingMac);
            if (string.IsNullOrWhiteSpace(newMac) ||
                currentMac == newMac)
            {
                return false;
            }

            if (IsHoneywellOuiVendor(incomingMacVendor))
                return false;

            if (HasStrongHoneywellIdentity(row) ||
                (detected != null &&
                 (IsHoneywellText(detected.Vendor) ||
                  IsHoneywellText(detected.Protocol) ||
                  IsHoneywellText(detected.Type))))
            {
                return false;
            }

            return true;
        }

        private bool HasStrongHoneywellIdentity(DataRow row)
        {
            if (row == null)
                return false;

            string type = row[(int)Columns.Type]?.ToString();
            string uniqueId = row[(int)Columns.UniqueId]?.ToString();

            return IsHoneywellText(type) || IsHoneywellText(uniqueId);
        }

        private void ResetRowToCurrentGenericIdentity(DataRow row, VendorProbeResult detected, string macVendor)
        {
            if (row == null)
                return;

            string vendor = detected?.Vendor;
            if (string.IsNullOrWhiteSpace(vendor) || vendor.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                vendor = !string.IsNullOrWhiteSpace(macVendor) ? macVendor + " (OUI)" : "Unknown";

            string protocol = detected?.Protocol;
            if (string.IsNullOrWhiteSpace(protocol) || IsHoneywellText(protocol))
                protocol = "Ping";

            string type = detected?.Type;
            if (string.IsNullOrWhiteSpace(type) || IsHoneywellText(type))
                type = "Unknown";

            row[(int)Columns.Vendor] = vendor;
            row[(int)Columns.Protocol] = protocol;
            row[(int)Columns.Type] = type;
            row[(int)Columns.Activation] = "N/A";
            row[(int)Columns.OnvifProfile] = "Unknown";
            row[(int)Columns.UniqueId] = "";
        }

        private bool IsUniviewText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   (value.IndexOf("Uniview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("Zhejiang Uniview", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private bool IsUniviewOuiVendor(string value)
        {
            return IsUniviewText(value);
        }

        private bool IsUniviewRow(DataRow row)
        {
            if (row == null)
                return false;

            return IsUniviewText(row[(int)Columns.Vendor]?.ToString()) ||
                   IsUniviewText(row[(int)Columns.Protocol]?.ToString()) ||
                   IsUniviewText(row[(int)Columns.MACVendor]?.ToString()) ||
                   IsUniviewText(row[(int)Columns.Type]?.ToString());
        }

        private bool IsDahuaText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   (value.IndexOf("Dahua", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("Zhejiang Dahua", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("dahuatech", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private bool IsDahuaVmsType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.IndexOf("VMS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("Video Management System", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsDahuaSwitchType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string compact = value.Replace("-", "").Replace(" ", "");
            int modelStart = compact.IndexOf("DHS", StringComparison.OrdinalIgnoreCase);
            bool hasDahuaSwitchModel = modelStart >= 0 &&
                                        modelStart + 4 < compact.Length &&
                                        char.IsDigit(compact[modelStart + 3]) &&
                                        compact.IndexOf("GT", modelStart, StringComparison.OrdinalIgnoreCase) >= 0;

            return hasDahuaSwitchModel ||
                   value.IndexOf("Dahua Switch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("Network Switch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("Ethernet Switch", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsDahuaSwitchRow(DataRow row)
        {
            return row != null && IsDahuaText(row[(int)Columns.Vendor]?.ToString()) &&
                   IsDahuaSwitchType(row[(int)Columns.Type]?.ToString());
        }

        private bool IsDahuaPrivateUpnpText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("Private Upnp", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsDahuaRow(DataRow row)
        {
            if (row == null)
                return false;

            if (IsDahuaVmsType(row[(int)Columns.Type]?.ToString()))
                return false;

            if (IsHoneywellOuiVendor(row[(int)Columns.MACVendor]?.ToString()))
                return false;

            return IsDahuaText(row[(int)Columns.Vendor]?.ToString()) ||
                   IsDahuaText(row[(int)Columns.Protocol]?.ToString()) ||
                   IsDahuaText(row[(int)Columns.MACVendor]?.ToString()) ||
                   IsDahuaText(row[(int)Columns.Type]?.ToString()) ||
                   IsDahuaText(row[(int)Columns.UniqueId]?.ToString());
        }

        private bool IsSunellText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("Sunell", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsSunellOuiVendor(string value)
        {
            return IsSunellText(value);
        }

        private bool IsSunellRow(DataRow row)
        {
            if (row == null)
                return false;

            return IsSunellText(row[(int)Columns.Vendor]?.ToString()) ||
                   IsSunellText(row[(int)Columns.Protocol]?.ToString()) ||
                   IsSunellText(row[(int)Columns.MACVendor]?.ToString()) ||
                   IsSunellText(row[(int)Columns.Type]?.ToString());
        }

        private bool IsTiandyText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("Tiandy", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsTiandyRow(DataRow row)
        {
            if (row == null)
                return false;

            return IsTiandyText(row[(int)Columns.Vendor]?.ToString()) ||
                   IsTiandyText(row[(int)Columns.Protocol]?.ToString()) ||
                   IsTiandyText(row[(int)Columns.MACVendor]?.ToString()) ||
                   IsTiandyText(row[(int)Columns.Type]?.ToString());
        }

        private bool IsDahuaUpnpRow(DataRow row)
        {
            if (row == null)
                return false;

            return IsDahuaText(row[(int)Columns.UniqueId]?.ToString()) ||
                   (IsDahuaText(row[(int)Columns.MACVendor]?.ToString()) &&
                    (IsGenericSsdpText(row[(int)Columns.Protocol]?.ToString()) ||
                     IsDahuaPrivateUpnpText(row[(int)Columns.Type]?.ToString())));
        }

        private void ApplyDahuaIdentity(DataRow row)
        {
            if (row == null)
                return;

            row[(int)Columns.Vendor] = "Dahua";
            row[(int)Columns.Protocol] = "Dahua";

            string status = DeviceSDK.Dahua.DahuaDiscoveryState.GetActivationStatus(
                row[(int)Columns.IPAddress]?.ToString(),
                row[(int)Columns.UniqueId]?.ToString());
            row[(int)Columns.Activation] = string.IsNullOrWhiteSpace(status) ? "Active" : status;

            string model = DeviceSDK.Dahua.DahuaDiscoveryState.GetDeviceModel(
                row[(int)Columns.IPAddress]?.ToString(),
                row[(int)Columns.MAC]?.ToString(),
                row[(int)Columns.UniqueId]?.ToString());

            if (IsDahuaPrivateUpnpText(row[(int)Columns.Type]?.ToString()) &&
                string.IsNullOrWhiteSpace(model))
            {
                row[(int)Columns.Type] = "Dahua UPnP response (model unknown)";
                return;
            }

            if (!string.IsNullOrWhiteSpace(model) &&
                IsGenericDahuaType(row[(int)Columns.Type]?.ToString()))
            {
                row[(int)Columns.Type] = model;
            }
        }

        private void ApplyHoneywellIdentity(DataRow row)
        {
            if (row == null)
                return;

            row[(int)Columns.Vendor] = "Honeywell";
            row[(int)Columns.Protocol] = "Honeywell";

            string model = DeviceSDK.Dahua.DahuaDiscoveryState.GetDeviceModel(
                row[(int)Columns.IPAddress]?.ToString(),
                row[(int)Columns.MAC]?.ToString(),
                row[(int)Columns.UniqueId]?.ToString());

            string currentType = row[(int)Columns.Type]?.ToString();
            if (!string.IsNullOrWhiteSpace(model) &&
                (string.IsNullOrWhiteSpace(currentType) ||
                 currentType == "Unknown" ||
                 IsDahuaText(currentType) ||
                 IsGenericNetworkVideoType(currentType)))
            {
                row[(int)Columns.Type] = model;
            }

            row[(int)Columns.Activation] = NormalizeHoneywellActivationStatus(
                row,
                row[(int)Columns.Activation]?.ToString());
        }

        private void ApplyUniviewIdentity(DataRow row)
        {
            if (row == null)
                return;

            row[(int)Columns.Vendor] = "Uniview";
            row[(int)Columns.Protocol] = "Uniview";

            string currentType = row[(int)Columns.Type]?.ToString();
            if (string.IsNullOrWhiteSpace(currentType) ||
                currentType == "Unknown" ||
                IsGenericNetworkVideoType(currentType) ||
                IsGenericUniviewType(currentType) ||
                IsGenericWsDiscoveryText(currentType))
            {
                row[(int)Columns.Type] = "Unknown";
            }

            row[(int)Columns.Activation] = "N/A";
        }

        private void ApplySunellIdentity(DataRow row)
        {
            if (row == null)
                return;

            row[(int)Columns.Vendor] = "Sunell";
            row[(int)Columns.Protocol] = "Sunell";

            string ip = row[(int)Columns.IPAddress]?.ToString();
            string model = DeviceSDK.Sunell.SunellActivationStatusProbe.GetModel(ip);
            string currentType = row[(int)Columns.Type]?.ToString();
            if (!string.IsNullOrWhiteSpace(model))
            {
                row[(int)Columns.Type] = model;
            }
            else if (string.IsNullOrWhiteSpace(currentType) ||
                currentType == "Unknown" ||
                IsGenericNetworkVideoType(currentType) ||
                IsGenericWsDiscoveryText(currentType))
            {
                row[(int)Columns.Type] = "Network Video Transmitter";
            }

            row[(int)Columns.Activation] = "N/A";
        }

        private void ApplyBoschIdentity(DataRow row)
        {
            if (row == null)
                return;

            row[(int)Columns.Vendor] = "Bosch";
            row[(int)Columns.Protocol] = "Bosch";

            string currentType = row[(int)Columns.Type]?.ToString();
            if (string.IsNullOrWhiteSpace(currentType) ||
                currentType == "Unknown" ||
                IsGenericNetworkVideoType(currentType) ||
                IsGenericWsDiscoveryText(currentType))
            {
                row[(int)Columns.Type] = "Bosch";
            }
        }

        private bool ShouldPreserveExistingIdentity(DataRow existingRow, string incomingProtocol, string incomingType)
        {
            if (existingRow == null)
                return false;

            if (IsBoschText(incomingProtocol) || IsBoschText(incomingType))
                return false;

            string macVendor = existingRow[(int)Columns.MACVendor]?.ToString();
            
            // Protect strongly identified OEM rows from being overwritten by their OEM protocols.
            if (IsHoneywellOuiVendor(macVendor) && IsHoneywellRow(existingRow))
            {
                if (IsDahuaText(incomingProtocol) || IsUniviewText(incomingProtocol) || IsSunellText(incomingProtocol) || IsTiandyText(incomingProtocol) || IsGenericDiscoveryUpdate(incomingProtocol, incomingType))
                {
                    return true;
                }
            }

            // WS-Discovery often rediscoveres an already-known Honeywell/Hikvision camera as a
            // generic "NetworkVideoTransmitter Device". Do not allow that generic result to
            // overwrite a richer vendor/model row that we already have.
            if (!IsGenericDiscoveryUpdate(incomingProtocol, incomingType))
                return false;

            return IsHoneywellRow(existingRow) || IsHikvisionRow(existingRow) || IsUniviewRow(existingRow) || IsDahuaRow(existingRow) || IsSunellRow(existingRow) || IsBoschRow(existingRow) || IsAxisRow(existingRow);
        }

        /// <summary>
        /// Returns true for any row that uses the Hikvision ISAPI activation stack —
        /// this includes both genuine Hikvision devices and Honeywell 30/35-series
        /// cameras, which are Hikvision OEM and share the same firmware.
        /// </summary>
        private bool IsHikvisionCompatibleRow(DataRow row)
        {
            return IsHikvisionRow(row) || IsHoneywellRow(row);
        }

                private void UpdateDataRowField(string ip, string uniqueId, int columnIndex, string value, Guid? expectedSessionId = null, string expectedMac = null)
        {
            StartupDiagnostics.Increment("dataRow_updates");
            if (this.InvokeRequired)
            {
                StartupDiagnostics.Increment("ui_begin_invokes"); this.BeginInvoke(new Action(() => UpdateDataRowField(ip, uniqueId, columnIndex, value, expectedSessionId, expectedMac)));
                return;
            }
            
            if (expectedSessionId.HasValue && expectedSessionId.Value != Guid.Empty && expectedSessionId.Value != this.currentScanSessionId)
            {
                return;
            }

            DataRow row = FindDeviceRow(ip, "", uniqueId);
            if (row != null)
            {
                if (row.RowState == DataRowState.Detached || row.RowState == DataRowState.Deleted) return;
                
                string currentMac = NormalizeMac(row[(int)Columns.MAC]?.ToString());
                if (!string.IsNullOrWhiteSpace(expectedMac) && !string.IsNullOrWhiteSpace(currentMac) && NormalizeMac(expectedMac) != currentMac)
                {
                    return;
                }
                
                if (columnIndex == (int)Columns.OnvifProfile)
                    value = NormalizeOnvifProfileDisplay(value);

                string currentValue = row[columnIndex]?.ToString() ?? "";
                StartupDiagnostics.Increment("redundant_dataRow_updates_skipped"); if (currentValue == value) return;

                row[columnIndex] = value;
            }
        }

        private static string NormalizeOnvifProfileDisplay(string profiles)
        {
            if (string.IsNullOrWhiteSpace(profiles))
                return "Unknown";

            string value = profiles.Trim();
            if (value.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                return "Unknown";

            value = Regex.Replace(value, @"\bProfile\s+", "", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\s*,\s*", ", ");
            value = Regex.Replace(value, @"\s+", " ").Trim();

            return string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
        }

        private async void UpdateActivationStatus(string ip, Guid? expectedSessionId = null, string expectedMac = null)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return;

            DataRow targetRow = null;
            string uniqueId = "";
            bool isHikvision = false, isHoneywell = false, isUniview = false, isDahua = false, isSunell = false, isTiandy = false, isDahuaUpnp = false, isBosch = false, isAxis = false;

            foreach (DataRow row in foundDeviceList.Rows)
            {
                if (row[(int)Columns.IPAddress]?.ToString() == ip)
                {
                    targetRow = row;
                    uniqueId = targetRow[(int)Columns.UniqueId]?.ToString() ?? "";
                    isHikvision = IsHikvisionCompatibleRow(targetRow);
                    isHoneywell = IsHoneywellRow(targetRow);
                    isUniview = IsUniviewRow(targetRow);
                    isDahua = IsDahuaRow(targetRow);
                    isDahuaUpnp = IsDahuaUpnpRow(targetRow);
                    isSunell = IsSunellRow(targetRow);
                    isTiandy = IsTiandyRow(targetRow);
                    isBosch = IsBoschRow(targetRow);
                    isAxis = IsAxisRow(targetRow);
                    break;
                }
            }

            if (targetRow == null)
                return;

            if (IsDahuaSwitchRow(targetRow))
            {
                UpdateDataRowField(ip, uniqueId, (int)Columns.Activation, "N/A (Switch)", expectedSessionId, expectedMac);
                return;
            }

            if (!isHikvision && !isHoneywell && !isUniview && !isDahua && !isSunell && !isTiandy && !isBosch && !isAxis)
            {
                UpdateDataRowField(ip, uniqueId, (int)Columns.Activation, "N/A", expectedSessionId, expectedMac);
                return;
            }

            UpdateDataRowField(ip, uniqueId, (int)Columns.Activation, "Checking...", expectedSessionId, expectedMac);

            try
            {
                string status = "";
                if (isHoneywell)
                    status = NormalizeHoneywellActivationStatus(targetRow, await DeviceSDK.Honeywell.HoneywellActivationStatusProbe.GetStatusAsync(ip));
                else if (isHikvision)
                    status = await HikvisionActivationStatusProbe.GetStatusAsync(ip);
                else if (isUniview)
                    status = await DeviceSDK.Uniview.UniviewActivationStatusProbe.GetStatusAsync(ip);
                else if (isDahua)
                {
                    string discoveryStatus = DeviceSDK.Dahua.DahuaDiscoveryState.GetActivationStatus(ip);
                    status = !string.IsNullOrWhiteSpace(discoveryStatus)
                        ? discoveryStatus
                        : isDahuaUpnp
                        ? "Active"
                        : await WithTimeout(
                            Task.Run(() => DeviceSDK.Dahua.DahuaActivationStatusProbe.GetStatus(ip)),
                            5000,
                            "Unknown").ConfigureAwait(false);
                }
                else if (isSunell)
                    status = await WithTimeout(
                        Task.Run(() => DeviceSDK.Sunell.SunellActivationStatusProbe.GetStatus(ip)),
                        5000,
                        "Unknown").ConfigureAwait(false);
                else if (isTiandy)
                    status = await WithTimeout(
                        Task.Run(() => DeviceSDK.Tiandy.TiandyActivationStatusProbe.GetStatus(ip)),
                        5000,
                        "Unknown").ConfigureAwait(false);
                else if (isBosch)
                    status = await WithTimeout(
                        DeviceSDK.Bosch.BoschActivationStatusProbe.GetStatusAsync(ip),
                        5000,
                        "Unknown").ConfigureAwait(false);
                else if (isAxis)
                    status = await WithTimeout(
                        DeviceSDK.Axis.AxisActivationStatusProbe.GetStatusAsync(ip),
                        5000,
                        "Unknown").ConfigureAwait(false);
                else
                    status = await WithTimeout(
                        HikvisionActivationStatusProbe.GetStatusAsync(ip),
                        5000,
                        "Unknown").ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(status))
                    status = "Unknown";

                UpdateDataRowField(ip, uniqueId, (int)Columns.Activation, status);
            }
            catch
            {
                UpdateDataRowField(ip, uniqueId, (int)Columns.Activation, "Unknown");
            }
        }        private async void UpdatePing(string ip, Guid? expectedSessionId = null, string expectedMac = null)
        {
            try
            {
                var sw_ping = System.Diagnostics.Stopwatch.StartNew();
                
                PingReply reply = await Task.Run(() => 
                {
                    using (System.Net.NetworkInformation.Ping ping = new System.Net.NetworkInformation.Ping())
                    {
                        return ping.Send(ip, 1000);
                    }
                }).ConfigureAwait(false);
                
                sw_ping.Stop(); 
                StartupDiagnostics.Increment("PingProbes"); 
                StartupDiagnostics.AddTimer("PingProbes", sw_ping.ElapsedMilliseconds); 
                StartupDiagnostics.LogSlowOperation("Ping", ip, "Unknown", sw_ping.ElapsedMilliseconds, reply.Status == System.Net.NetworkInformation.IPStatus.Success);

                string result = reply.Status == System.Net.NetworkInformation.IPStatus.Success
                    ? reply.RoundtripTime + " ms"
                    : "No reply";

                UpdateDataRowField(ip, "", (int)Columns.Ping, result, expectedSessionId, expectedMac);
            }
            catch
            {
                UpdateDataRowField(ip, "", (int)Columns.Ping, "Failed", expectedSessionId, expectedMac);
            }
        }
        private string GetMacAddress(string ip)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "arp";
                psi.Arguments = "-a " + ip;
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;

                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    string[] lines = output.Split(
                        new string[] { "\r\n", "\n" },
                        StringSplitOptions.RemoveEmptyEntries
                    );

                    foreach (string line in lines)
                    {
                        if (line.Contains(ip))
                        {
                            string[] parts = line.Split(
                                new char[] { ' ', '\t' },
                                StringSplitOptions.RemoveEmptyEntries
                            );

                            foreach (string part in parts)
                            {
                                string candidate = part.Trim().ToUpper();

                                if (Regex.IsMatch(candidate, @"^[0-9A-F]{2}[-:][0-9A-F]{2}[-:][0-9A-F]{2}[-:][0-9A-F]{2}[-:][0-9A-F]{2}[-:][0-9A-F]{2}$"))
                                    return candidate.Replace(":", "-");
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        private DataRow FindDeviceRow(string ip, string mac)
        {
            return FindDeviceRow(ip, mac, "");
        }

        private DataRow FindDeviceRow(string ip, string mac, string uniqueId)
        {
            if (foundDeviceList == null)
                return null;

            string cleanIp = (ip ?? "").Trim();
            string cleanMac = NormalizeMac(mac);
            string cleanUniqueId = (uniqueId ?? "").Trim();
            string displayCleanUniqueId = NormalizeUniqueIdForDisplay(cleanUniqueId);

            // Best match: Unique ID. This survives IP changes.
            if (!string.IsNullOrWhiteSpace(cleanUniqueId))
            {
                foreach (DataRow row in foundDeviceList.Rows)
                {
                    if (row.RowState == DataRowState.Deleted)
                        continue;

                    string rowUniqueId = row[(int)Columns.UniqueId]?.ToString()?.Trim();
                    string displayRowUniqueId = NormalizeUniqueIdForDisplay(rowUniqueId);
                    if (!string.IsNullOrWhiteSpace(rowUniqueId) &&
                        (rowUniqueId.Equals(cleanUniqueId, StringComparison.OrdinalIgnoreCase) ||
                         displayRowUniqueId.Equals(displayCleanUniqueId, StringComparison.OrdinalIgnoreCase)))
                        return row;
                }
            }

            // Second match: MAC address. This also survives IP changes.
            if (!string.IsNullOrWhiteSpace(cleanMac))
            {
                foreach (DataRow row in foundDeviceList.Rows)
                {
                    if (row.RowState == DataRowState.Deleted)
                        continue;

                    string rowMac = NormalizeMac(row[(int)Columns.MAC]?.ToString());
                    if (!string.IsNullOrWhiteSpace(rowMac) && rowMac == cleanMac)
                        return row;
                }
            }

            // Last match: IP address. Do not match a recently changed old IP,
            // because that old IP may belong to a delayed scan result.
            if (!string.IsNullOrWhiteSpace(cleanIp) && !IsOldIpSuppressed(cleanIp))
            {
                foreach (DataRow row in foundDeviceList.Rows)
                {
                    if (row.RowState == DataRowState.Deleted)
                        continue;

                    string rowIp = row[(int)Columns.IPAddress]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(rowIp) &&
                        rowIp.Equals(cleanIp, StringComparison.OrdinalIgnoreCase))
                        return row;
                }
            }

            return null;
        }

        private DataRow FindDahuaFactoryDefaultDiscoveryRow(string ip, string mac, string uniqueId)
        {
            string cleanUniqueId = (uniqueId ?? "").Trim();
            string cleanMac = NormalizeMac(mac);
            bool hasConcreteIdentity =
                !IsDahuaPlaceholderIdentity(cleanUniqueId) ||
                !string.IsNullOrWhiteSpace(cleanMac);

            if (hasConcreteIdentity)
            {
                string lookupUniqueId = IsDahuaPlaceholderIdentity(cleanUniqueId) ? "" : cleanUniqueId;
                return FindDeviceRow("", cleanMac, lookupUniqueId);
            }

            return FindDeviceRow(ip, "", "");
        }

        private bool IsDahuaFactoryDefaultIp(string ip)
        {
            return string.Equals((ip ?? "").Trim(), "192.168.1.108", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsDahuaPlaceholderIdentity(string uniqueId)
        {
            if (string.IsNullOrWhiteSpace(uniqueId))
                return true;

            string clean = uniqueId.Trim();
            return clean.Equals("Dahua device", StringComparison.OrdinalIgnoreCase) ||
                   clean.Equals("Dahua", StringComparison.OrdinalIgnoreCase);
        }

        private bool ShouldKeepSeparateDahuaFactoryDefaultRow(DataRow keepRow, DataRow candidateRow, string sharedIp)
        {
            if (!IsDahuaFactoryDefaultIp(sharedIp) || keepRow == null || candidateRow == null)
                return false;

            if (!IsDahuaRow(keepRow) || !IsDahuaRow(candidateRow))
                return false;

            string keepMac = NormalizeMac(keepRow[(int)Columns.MAC]?.ToString());
            string candidateMac = NormalizeMac(candidateRow[(int)Columns.MAC]?.ToString());
            if (!string.IsNullOrWhiteSpace(keepMac) &&
                !string.IsNullOrWhiteSpace(candidateMac) &&
                keepMac != candidateMac)
                return true;

            string keepUniqueId = NormalizeUniqueIdForDisplay(keepRow[(int)Columns.UniqueId]?.ToString());
            string candidateUniqueId = NormalizeUniqueIdForDisplay(candidateRow[(int)Columns.UniqueId]?.ToString());
            if (!IsDahuaPlaceholderIdentity(keepUniqueId) &&
                !IsDahuaPlaceholderIdentity(candidateUniqueId) &&
                !keepUniqueId.Equals(candidateUniqueId, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private string NormalizeMac(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac))
                return "";

            string clean = mac.Trim().ToUpperInvariant().Replace(":", "-");
            if (Regex.IsMatch(clean, @"^[0-9A-F]{12}$"))
                clean = Regex.Replace(clean, "(.{2})(?!$)", "$1-");

            return clean;
        }

        private bool IsMacAddressText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string clean = value.Trim().Replace(":", "").Replace("-", "");
            return Regex.IsMatch(clean, @"^[0-9A-Fa-f]{12}$");
        }

        private string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private string NormalizeUniqueIdForDisplay(string uniqueId)
        {
            if (string.IsNullOrWhiteSpace(uniqueId))
                return "";

            const string dahuaDevicePrefix = "device_3_0-";
            string clean = uniqueId.Trim();

            if (clean.StartsWith(dahuaDevicePrefix, StringComparison.OrdinalIgnoreCase))
                return clean.Substring(dahuaDevicePrefix.Length);

            return clean;
        }

        private void ScannerWindow_FormClosed(object sender, FormClosedEventArgs e)
        {
            Logger.getInstance().Dispose();
            Application.Exit();
        }

        private ToolStripMenuItem onvifConfigMenu;
        private ToolStripMenuItem onvifDateTimeToolStripMenuItem;
        private ToolStripMenuItem hikvisionOnvifEnableItem;
        private ToolStripMenuItem hikvisionOnvifBatchEnableItem;
        private ToolStripMenuItem loginToolStripMenuItem;
        private ToolStripMenuItem onvifEventMonitorMenuItem;
        private ToolStripMenuItem initializeDeviceMenuItem;
        private ToolStripMenuItem changeIpMenuItem;
        private ToolStripMenuItem changeIpAcrossSubnetMenuItem;
        private ToolStripMenuItem batchOperationsMenu;
        
        private ToolStripMenuItem onvifMotionDetectionMenu;
        private ToolStripMenuItem motionUniviewItem;
        private ToolStripMenuItem motionHikvisionItem;
        private ToolStripMenuItem motionDahuaItem;
        private ToolStripMenuItem motionHoneywellItem;
        private ToolStripMenuItem motionAxisItem;

        private void rightClickMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (showUniqueIdColumnMenuItem != null && dataGridView1.Columns.Contains(ColumnNames[(int)Columns.UniqueId]))
                showUniqueIdColumnMenuItem.Checked = dataGridView1.Columns[ColumnNames[(int)Columns.UniqueId]].Visible;

            ApplyAiBrandOverridesToSelectedRowsForAction();
            UpdateAdvancedToolsAvailability();

            if (dataGridView1.SelectedRows.Count == 0)
            {
                if (viewMenu != null) viewMenu.Enabled = false;
                if (showLiveByVlcMenuItem != null) showLiveByVlcMenuItem.Enabled = false;
                if (showSelectedByVlcMenuItem != null) showSelectedByVlcMenuItem.Enabled = false;
                if (onvifConfigMenu != null) onvifConfigMenu.Enabled = false;
                if (onvifDateTimeToolStripMenuItem != null) onvifDateTimeToolStripMenuItem.Enabled = false;
                if (hikvisionOnvifEnableItem != null) hikvisionOnvifEnableItem.Enabled = false;
                if (hikvisionOnvifBatchEnableItem != null) hikvisionOnvifBatchEnableItem.Enabled = false;
                if (onvifMotionDetectionMenu != null) onvifMotionDetectionMenu.Enabled = false;
                if (pingSelectedOnceMenuItem != null) pingSelectedOnceMenuItem.Enabled = false;
            }
            else
            {
                if (viewMenu != null) viewMenu.Enabled = true;
                bool hasDahuaSwitch = dataGridView1.SelectedRows.Cast<DataGridViewRow>()
                    .Any(selected => IsDahuaSwitchRow(GetDataRowFromGridRow(selected)));
                if (showLiveByVlcMenuItem != null) showLiveByVlcMenuItem.Enabled = dataGridView1.SelectedRows.Count == 1 && !hasDahuaSwitch;
                if (showSelectedByVlcMenuItem != null) showSelectedByVlcMenuItem.Enabled = dataGridView1.SelectedRows.Count > 0 && !hasDahuaSwitch;
                if (onvifConfigMenu != null) onvifConfigMenu.Enabled = !hasDahuaSwitch;
                if (onvifDateTimeToolStripMenuItem != null) onvifDateTimeToolStripMenuItem.Enabled = !hasDahuaSwitch;
                if (onvifEventMonitorMenuItem != null) onvifEventMonitorMenuItem.Enabled = dataGridView1.SelectedRows.Count == 1 && !hasDahuaSwitch;
                if (initializeDeviceMenuItem != null) initializeDeviceMenuItem.Enabled = !hasDahuaSwitch;
                if (changeIpMenuItem != null) changeIpMenuItem.Enabled = !hasDahuaSwitch;
                if (changeIpAcrossSubnetMenuItem != null) changeIpAcrossSubnetMenuItem.Enabled = dataGridView1.SelectedRows.Count == 1 && !hasDahuaSwitch;
                if (batchOperationsMenu != null) batchOperationsMenu.Enabled = !hasDahuaSwitch;
                if (pingSelectedOnceMenuItem != null) pingSelectedOnceMenuItem.Enabled = true;

                bool hasOnlyHikvisionRows = true;
                foreach (DataGridViewRow selectedRow in dataGridView1.SelectedRows)
                {
                    if (!IsHikvisionCompatibleRow(GetDataRowFromGridRow(selectedRow)))
                    {
                        hasOnlyHikvisionRows = false;
                        break;
                    }
                }

                if (hikvisionOnvifEnableItem != null)
                    hikvisionOnvifEnableItem.Enabled = dataGridView1.SelectedRows.Count == 1 && hasOnlyHikvisionRows;
                if (hikvisionOnvifBatchEnableItem != null)
                    hikvisionOnvifBatchEnableItem.Enabled = dataGridView1.SelectedRows.Count > 1 && hasOnlyHikvisionRows;

                if (onvifMotionDetectionMenu != null)
                {
                    onvifMotionDetectionMenu.Enabled = true;
                    if (dataGridView1.SelectedRows.Count == 1)
                    {
                        var row = dataGridView1.SelectedRows[0];
                        var dataRow = GetDataRowFromGridRow(row);
                        bool isUniview = IsUniviewRow(dataRow);
                        bool isHoneywell = IsHoneywellRow(dataRow);
                        bool isAxis = IsAxisRow(dataRow);
                        motionUniviewItem.Enabled = isUniview;
                        motionHikvisionItem.Enabled = false; // Placeholder
                        motionDahuaItem.Enabled = false; // Placeholder
                        motionHoneywellItem.Enabled = isHoneywell;
                        motionAxisItem.Enabled = isAxis;
                    }
                    else
                    {
                        motionUniviewItem.Enabled = false;
                        motionHikvisionItem.Enabled = false;
                        motionDahuaItem.Enabled = false;
                        motionHoneywellItem.Enabled = false;
                        motionAxisItem.Enabled = false;
                    }
                }
            }
        }

        private void onvifDateTimeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selRows = dataGridView1.SelectedRows;
            if (selRows.Count == 0) return;

            if (selRows.Count == 1)
            {
                var row = selRows[0];
                string ip = row.Cells[(int)Columns.IPAddress].Value?.ToString();
                string uniqueId = GetDataRowFromGridRow(row)?[(int)Columns.UniqueId]?.ToString();
                if (!string.IsNullOrEmpty(ip))
                {
                    bool isHik = IsHikvisionCompatibleRow(GetDataRowFromGridRow(row));
                    UniversalScanner.Forms.OnvifDateTimeConfigurationForm form = new UniversalScanner.Forms.OnvifDateTimeConfigurationForm(ip, 80, isHik);
                    form.ShowDialog(this);
                }
            }
            else
            {
                var devices = new System.Collections.Generic.List<(string Ip, int Port, string Vendor, string Model)>();
                foreach (System.Windows.Forms.DataGridViewRow r in selRows)
                {
                    string ip = r.Cells[(int)Columns.IPAddress].Value?.ToString();
                    string vendor = r.Cells[(int)Columns.Vendor].Value?.ToString() ?? "";
                    string model = r.Cells[(int)Columns.Type].Value?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(ip))
                    {
                        devices.Add((ip, 80, vendor, model));
                    }
                }

                if (devices.Count > 0)
                {
                    UniversalScanner.Forms.OnvifBatchDateTimeConfigurationForm form = new UniversalScanner.Forms.OnvifBatchDateTimeConfigurationForm(devices);
                    form.ShowDialog(this);
                }
            }
        }

        private void motionUniviewItem_Click(object sender, EventArgs e)
        {
            var selRows = dataGridView1.SelectedRows;
            if (selRows.Count == 1)
            {
                var row = selRows[0];
                string ip = row.Cells[(int)Columns.IPAddress].Value?.ToString();
                string model = row.Cells[(int)Columns.Type].Value?.ToString() ?? "";
                if (!string.IsNullOrEmpty(ip) && IsUniviewRow(GetDataRowFromGridRow(row)))
                {
                    UniversalScanner.DeviceSDK.MotionDetection.IMotionDetectionService service = new UniversalScanner.DeviceSDK.Uniview.UniviewMotionDetectionService();
                    UniversalScanner.Forms.MotionDetectionForm form = new UniversalScanner.Forms.MotionDetectionForm(service, ip, "Uniview", model);
                    form.ShowDialog(this);
                }
            }
        }

        private async void motionHoneywellItem_Click(object sender, EventArgs e)
        {
            var selRows = dataGridView1.SelectedRows;
            if (selRows.Count == 1)
            {
                var row = selRows[0];
                string ip = row.Cells[(int)Columns.IPAddress].Value?.ToString();
                string model = row.Cells[(int)Columns.Type].Value?.ToString() ?? "";
                if (!string.IsNullOrEmpty(ip) && IsHoneywellRow(GetDataRowFromGridRow(row)))
                {
                    await ShowStatusAsync("Detecting Honeywell Web Runtime support...", 0);
                    bool isWebRuntime = await UniversalScanner.DeviceSDK.Honeywell.HoneywellWebRuntimeMotionService.IsSupportedAsync(ip);
                    await ShowStatusAsync("Ready", 0);

                    UniversalScanner.DeviceSDK.MotionDetection.IMotionDetectionService service;
                    if (isWebRuntime)
                    {
                        service = new UniversalScanner.DeviceSDK.Honeywell.HoneywellWebRuntimeMotionService();
                    }
                    else
                    {
                        service = new UniversalScanner.DeviceSDK.Honeywell.HoneywellMotionDetectionService();
                    }
                    UniversalScanner.Forms.MotionDetectionForm form = new UniversalScanner.Forms.MotionDetectionForm(service, ip, "Honeywell", model);
                    form.ShowDialog(this);
                }
            }
        }

        private async void motionAxisItem_Click(object sender, EventArgs e)
        {
            var selRows = dataGridView1.SelectedRows;
            if (selRows.Count != 1)
                return;

            var row = selRows[0];
            string ip = row.Cells[(int)Columns.IPAddress].Value?.ToString();
            if (string.IsNullOrWhiteSpace(ip) || !IsAxisRow(GetDataRowFromGridRow(row)))
                return;

            string universalUsername;
            string universalPassword;
            UniversalLoginManager.GetUniversalCredentials(out universalUsername, out universalPassword);

            if (!string.IsNullOrWhiteSpace(universalUsername) && !string.IsNullOrWhiteSpace(universalPassword))
            {
                try
                {
                    await ShowStatusAsync("Starting Axis motion detection app on " + ip + " using Universal Login...", 0);
                    var universalResult = await UniversalScanner.DeviceSDK.Axis.AxisMotionApp
                        .StartMotionDetectionAsync(ip, universalUsername, universalPassword);

                    if (universalResult.Success)
                    {
                        await ShowStatusAsync(universalResult.Message, 3500);
                        MessageBox.Show(
                            this,
                            universalResult.Message,
                            "Axis Motion Detection",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }

                    if (!IsAxisMotionCredentialFailure(universalResult.Message))
                    {
                        await ShowStatusAsync(universalResult.Message, 3500);
                        MessageBox.Show(this, universalResult.Message, "Axis Motion Detection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    await ShowStatusAsync("Universal Login was rejected by the Axis camera.", 2500);
                }
                catch (Exception ex)
                {
                    if (!IsAxisMotionCredentialFailure(ex.Message))
                    {
                        await ShowStatusAsync("Axis motion app start failed.", 3500);
                        MessageBox.Show(this, ex.Message, "Axis Motion Detection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                finally
                {
                    await ShowStatusAsync("Ready", 0);
                }
            }

            using (var form = new AxisMotionAppForm(ip, universalUsername, universalPassword))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    await ShowStatusAsync("Starting Axis motion detection app on " + ip + "...", 0);
                    var result = await UniversalScanner.DeviceSDK.Axis.AxisMotionApp
                        .StartMotionDetectionAsync(ip, form.Username, form.Password);

                    await ShowStatusAsync(result.Message, 3500);
                    MessageBox.Show(
                        this,
                        result.Message,
                        "Axis Motion Detection",
                        MessageBoxButtons.OK,
                        result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
                catch (UnauthorizedAccessException ex)
                {
                    await ShowStatusAsync("Axis motion app start failed.", 3500);
                    MessageBox.Show(this, ex.Message, "Axis Motion Detection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    await ShowStatusAsync("Axis motion app start failed.", 3500);
                    MessageBox.Show(this, ex.Message, "Axis Motion Detection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    await ShowStatusAsync("Ready", 0);
                }
            }
        }

        private static bool IsAxisMotionCredentialFailure(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.IndexOf("username or password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("authentication", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("rejected", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void dataGridView1_CellContentDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            string ip;

            if (e.RowIndex < 0)
                return;

            ip = dataGridView1.Rows[e.RowIndex].Cells[(int)Columns.IPAddress].Value.ToString();

            if (ip != "")
            {
                IPAddress parsed;

                if (IPAddress.TryParse(ip, out parsed))
                {
                    switch (parsed.AddressFamily)
                    {
                        case AddressFamily.InterNetwork:
                            Process.Start(String.Format("http://{0}", parsed.ToString()));
                            break;

                        case AddressFamily.InterNetworkV6:
                            Process.Start(String.Format("http://[{0}]", parsed.ToString()));
                            break;
                    }
                }
            }
        }

        private void aboutButton_Click(object sender, EventArgs e)
        {
            FileVersionInfo versionInfo;

            versionInfo = localVersion.getVersionInfo();

            using (ModernAboutDialog aboutDialog = new ModernAboutDialog(
                versionInfo.ProductName,
                versionInfo.FileMajorPart + "." + versionInfo.FileMinorPart,
                "October 17, 2026",
                versionInfo.LegalCopyright))
            {
                aboutDialog.ShowDialog(this);
            }
        }

        private void ScannerWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.Shift && e.KeyCode == Keys.F12)
            {
                using (var form = new SpecialThanksForm())
                {
                    form.ShowDialog(this);
                }
                e.Handled = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.A)
            {
                dataGridView1.SelectAll();
                e.Handled = true;
                return;
            }

            if (e.Alt && e.KeyCode == Keys.L)
            {
                if (dataGridView1.SelectedRows.Count == 1)
                    showLiveByVlcToolStripMenuItem_Click(sender, EventArgs.Empty);

                e.Handled = true;
                return;
            }

            if (e.Alt && e.KeyCode == Keys.V)
            {
                if (dataGridView1.SelectedRows.Count > 0)
                    showSelectedByVlcToolStripMenuItem_Click(sender, EventArgs.Empty);

                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                deleteSelectedRows();
                e.Handled = true;
                return;
            }
        }

        private void exportAsCSV()
        {
            StringBuilder sb;

            string local_name = Thread.CurrentThread.CurrentCulture.EnglishName;
            string local_separator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;

            using (SaveFileDialog saveAs = new SaveFileDialog())
            {
                saveAs.Filter = "CSV file|*.csv|CSV " + local_name + " format|*.csv|TSV file|*.txt;*.tsv";
                saveAs.Title = "Export device list";
                saveAs.OverwritePrompt = true;
                saveAs.CheckPathExists = true;

                if (saveAs.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(saveAs.FileName))
                    return;

                try
                {
                    string quote_start = "\"";
                    string quote_end = "\"";
                    string quote_escape = quote_end + quote_end;
                    string separator = ",";

                    switch (saveAs.FilterIndex)
                    {
                        case 1:
                            break;

                        case 2:
                            quote_start = "\"";
                            quote_end = "\"";
                            separator = local_separator;
                            break;

                        case 3:
                            quote_start = "";
                            quote_end = "";
                            separator = "\t";
                            break;
                    }

                    sb = new StringBuilder();

                    var headers = dataGridView1.Columns.Cast<DataGridViewColumn>();
                    sb.AppendLine(string.Join(separator, headers.Select(
                        column => quote_start + ((quote_end != "") ? column.HeaderText.Replace(quote_end, quote_escape) : column.HeaderText) + quote_end
                    ).ToArray()));

                    foreach (DataGridViewRow row in dataGridView1.Rows)
                    {
                        if (row.IsNewRow)
                            continue;

                        var cells = row.Cells.Cast<DataGridViewCell>();
                        sb.AppendLine(string.Join(separator, cells.Select(cell =>
                        {
                            string value = cell.Value?.ToString() ?? "";
                            return quote_start + ((quote_end != "") ? value.Replace(quote_end, quote_escape) : value) + quote_end;
                        }).ToArray()));
                    }

                    File.WriteAllText(saveAs.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show(this, "CSV exported successfully.", "Export Device List",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Failed to export CSV:\r\n\r\n" + ex.Message, "Export Device List",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public void formatProtocol(string protocol, int color)
        {
            if (!protocolFormat.ContainsKey(protocol))
            {
                protocolFormat.Add(protocol, color);
            }
        }

        private void exportListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            exportAsCSV();
        }

        private void exportListPdfToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<DataGridViewColumn> visibleColumns = dataGridView1.Columns
                .Cast<DataGridViewColumn>()
                .Where(column => column.Visible || column.HeaderText == ColumnNames[(int)Columns.UniqueId])
                .OrderBy(column => column.DisplayIndex)
                .ToList();

            List<List<string>> rows = dataGridView1.Rows
                .Cast<DataGridViewRow>()
                .Where(row => !row.IsNewRow)
                .OrderBy(row => row.Index)
                .Select(row => visibleColumns
                    .Select(column => row.Cells[column.Index].Value?.ToString() ?? "")
                    .ToList())
                .Where(values => values.Any(value => !string.IsNullOrWhiteSpace(value)))
                .ToList();

            if (visibleColumns.Count == 0 || rows.Count == 0)
            {
                MessageBox.Show(this, "There are no devices to export.", "Export Device List",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Export Device List to PDF";
                dialog.Filter = "PDF files (*.pdf)|*.pdf";
                dialog.FileName = "Device-List.pdf";
                dialog.OverwritePrompt = true;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    SimplePdfTableExporter.ExportFullDeviceList(dialog.FileName, visibleColumns.Select(column => column.HeaderText).ToList(), rows);
                    MessageBox.Show(this, "Device list exported successfully.", "Export Device List",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Failed to export PDF:\r\n\r\n" + ex.Message, "Export Device List",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void dataGridView1_RowPrePaint(object sender, DataGridViewRowPrePaintEventArgs e)
        {
            int index = e.RowIndex;

            string protocol = dataGridView1.Rows[index].Cells[(int)Columns.Protocol].Value?.ToString();

            if (protocol != null && protocolFormat.ContainsKey(protocol))
            {
                dataGridView1.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(protocolFormat[protocol]);
            }
        }

        private void dataGridView1_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            DataGridViewColumn column = dataGridView1.Columns[e.ColumnIndex];

            if (column.SortMode == DataGridViewColumnSortMode.Programmatic)
            {
                bool ascending = column.HeaderCell.SortGlyphDirection != SortOrder.Ascending;

                var orderedRows = foundDeviceList.Rows
                    .Cast<DataRow>()
                    .Where(row => row.RowState != DataRowState.Deleted)
                    .Select((row, index) => new
                    {
                        SortValue = GetIpSortValue(row.Field<string>(ColumnNames[(int)Columns.IPAddress])),
                        Index = index,
                        Values = row.ItemArray
                    });

                object[][] rowValues = ascending
                    ? orderedRows.OrderBy(row => row.SortValue).ThenBy(row => row.Index).Select(row => row.Values).ToArray()
                    : orderedRows.OrderByDescending(row => row.SortValue).ThenBy(row => row.Index).Select(row => row.Values).ToArray();

                foundDeviceList.BeginLoadData();
                foundDeviceList.Clear();

                foreach (object[] values in rowValues)
                {
                    foundDeviceList.Rows.Add(values);
                }

                foundDeviceList.EndLoadData();
                binding.RemoveSort();
                column.HeaderCell.SortGlyphDirection = ascending ? SortOrder.Ascending : SortOrder.Descending;
            }
        }

        private UInt32 GetIpSortValue(string ip)
        {
            IPAddress value;

            if (!IPAddress.TryParse(ip, out value) || value.AddressFamily != AddressFamily.InterNetwork)
                return UInt32.MaxValue;

            byte[] valueBytes = value.GetAddressBytes();

            return (UInt32)(valueBytes[0] << 24
                | valueBytes[1] << 16
                | valueBytes[2] << 8
                | valueBytes[3]);
        }

        public void updateAvailable()
        {
            this.Invoke(new MethodInvoker(delegate
            {
                newVersion.Visible = false;
            }));
        }

        private void ScannerWindow_Load(object sender, EventArgs e)
        {
            
            System.Windows.Forms.Timer diagTimer = new System.Windows.Forms.Timer(); 
            diagTimer.Interval = 100; 
            System.Diagnostics.Stopwatch lastTick = System.Diagnostics.Stopwatch.StartNew(); 
            ToolStripStatusLabel diagLabel = new ToolStripStatusLabel("Diagnostic: Active");
            diagLabel.ForeColor = System.Drawing.Color.Red;
            statusStrip.Items.Add(diagLabel);
            diagTimer.Tick += (s, ev) => { 
                long elapsed = lastTick.ElapsedMilliseconds; 
                lastTick.Restart(); 
                long delay = elapsed - 100; 
                if (delay > 0) { 
                    if (delay > StartupDiagnostics.MaxUiDelayMs) StartupDiagnostics.MaxUiDelayMs = delay; 
                    StartupDiagnostics.TotalUiDelayMs += delay; 
                } 
                StartupDiagnostics.UiTicks++; 
                if (delay < 20 && StartupDiagnostics.AppLifetime.ElapsedMilliseconds > 5000 && StartupDiagnostics.FirstResponsiveTimeMs == -1) StartupDiagnostics.FirstResponsiveTimeMs = StartupDiagnostics.AppLifetime.ElapsedMilliseconds;
                StartupDiagnostics.WriteLog(foundDeviceList.Rows.Count); 
            }; 
            diagTimer.Start();

#if DEBUG
            if (!Config.getInstance().DebugMode)
            {
                if (MessageBox.Show(String.Format("This version is a debug version, it can be unstable and with lower performances.\n\n"
                    + "You might want to download the release version at:\n{0}\n\n"
                    + "Do you really want to continue?",
                    "https://github.com/julienblitte/UniversalScanner/releases"), "Debug version", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
                    != DialogResult.Yes)
                {
                    Application.Exit();
                }
            }

            this.Text += " - Debug version " + localVersion.getVersionInfo().ProductVersion;
            newVersion.Visible = false;
#else
            newVersion.Visible = false;
#endif
        }

        private void openSelectedInBrowserToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selRows = dataGridView1.SelectedRows;

            if (selRows.Count == 1)
            {
                dataGridView1_CellContentDoubleClick(dataGridView1, new DataGridViewCellEventArgs((int)Columns.IPAddress, selRows[0].Index));
            }
        }

        private async void openAcrossSubnetInBrowserToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selRows = dataGridView1.SelectedRows;
            if (selRows.Count != 1)
                return;

            string ip = selRows[0].Cells[(int)Columns.IPAddress].Value?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(ip))
                return;

            IPAddress cameraIp;
            if (!IPAddress.TryParse(ip, out cameraIp) || cameraIp.AddressFamily != AddressFamily.InterNetwork)
            {
                MessageBox.Show(this, "Open Across Subnet supports IPv4 addresses only.", "Open Across Subnet", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string subnetMask = RecentIpNetworkRegistry.GetSubnetMask(ip);
            if (string.IsNullOrWhiteSpace(subnetMask))
                subnetMask = NetworkTools.GetKnownSubnetMaskForTarget(ip);
            if (string.IsNullOrWhiteSpace(subnetMask))
                subnetMask = ip.StartsWith("169.254.", StringComparison.Ordinal) ? "255.255.0.0" : "255.255.255.0";
            DataRow dataRow = GetDataRowFromGridRow(selRows[0]);
            bool isAxis = IsAxisRow(dataRow);
            string axisAdapterAlias = isAxis ? AxisLinkLocalRegistry.GetAdapterAlias(ip) : "";

            if (NetworkTools.IsIpOnAnyLocalSubnet(ip, subnetMask) &&
                (!isAxis ||
                 (!string.IsNullOrWhiteSpace(axisAdapterAlias) &&
                  NetworkTools.IsIpOnLocalSubnetForAdapter(axisAdapterAlias, ip, subnetMask))))
            {
                bool directlyReachable = await TemporarySubnetService.Instance.IsCameraReachableAsync(cameraIp, null, isAxis ? axisAdapterAlias : null);
                if (directlyReachable)
                {
                    OpenUrlInDefaultBrowser("http://" + ip);
                    return;
                }
            }

            if (!TemporarySubnetService.Instance.IsAdministrator())
            {
                MessageBox.Show(this,
                    "Open Across Subnet needs administrator permission so Universal Scanner can add a temporary PC IP address for this camera subnet.\r\n\r\nRun Universal Scanner as administrator, then try again.",
                    "Open Across Subnet",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string adapterAlias = axisAdapterAlias;
            if (string.IsNullOrWhiteSpace(adapterAlias))
            {
                adapterAlias = NetworkTools.GetPreferredPhysicalAdapterName();
            }

            if (string.IsNullOrWhiteSpace(adapterAlias))
            {
                using (SelectAdapterForm adapterForm = new SelectAdapterForm())
                {
                    if (adapterForm.ShowDialog(this) != DialogResult.OK)
                        return;

                    adapterAlias = adapterForm.SelectedAdapterName;
                }
            }

            IPAddress mask = IPAddress.Parse(subnetMask);
            TemporaryIpRecord existingRecord = TemporarySubnetService.Instance.GetExistingRecordForSubnet(cameraIp, mask);
            if (existingRecord != null &&
                !string.Equals(existingRecord.AdapterName, adapterAlias, StringComparison.OrdinalIgnoreCase))
            {
                existingRecord = null;
            }

            IPAddress temporaryIp;

            if (existingRecord != null && IPAddress.TryParse(existingRecord.TemporaryIp, out temporaryIp))
            {
                await ShowStatusAsync("Using existing temporary IP " + temporaryIp + " to open " + ip + "...");
            }
            else
            {
                temporaryIp = TemporarySubnetService.Instance.FindAvailableTemporaryIp(cameraIp, mask);
                if (temporaryIp == null)
                {
                    MessageBox.Show(this, "Could not find an available temporary IP for this camera subnet.", "Open Across Subnet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    await ShowStatusAsync("Ready", 0);
                    return;
                }

                await ShowStatusAsync("Adding temporary IP " + temporaryIp + " / " + subnetMask + "...");
                TemporarySubnetService.AddIpResult addResult = await TemporarySubnetService.Instance.AddTemporaryIpAsync(adapterAlias, temporaryIp, mask, cameraIp);
                if (addResult == null || !addResult.Success)
                {
                    string reason = addResult != null && addResult.DhcpDetected
                        ? "The selected adapter is using DHCP. Add the temporary IP manually or use a static adapter profile first."
                        : (addResult?.ErrorMessage ?? "Windows did not add the temporary IP.");

                    MessageBox.Show(this, reason, "Open Across Subnet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    await ShowStatusAsync("Ready", 0);
                    return;
                }

                TemporarySubnetService.Instance.ActiveRecords.Add(new TemporaryIpRecord
                {
                    AdapterName = adapterAlias,
                    AdapterId = adapterAlias,
                    TemporaryIp = temporaryIp.ToString(),
                    SubnetMask = subnetMask,
                    PrefixLength = GetSubnetPrefixLength(mask),
                    CameraIp = ip,
                    CreatedTime = DateTime.Now,
                    Status = "Active - opened in browser",
                    CreatedByUniversalScanner = true,
                    RouteAdded = true,
                    WasDhcpBefore = addResult.WasDhcpBefore,
                    OriginalDhcpIp = addResult.OriginalDhcpIp,
                    OriginalDhcpMask = addResult.OriginalDhcpMask,
                    OriginalDhcpGateway = addResult.OriginalDhcpGateway,
                    OriginalDhcpDns = addResult.OriginalDhcpDns
                });
            }

            await TemporarySubnetService.Instance.ValidateAndConfigureRouteAsync(adapterAlias, temporaryIp, cameraIp);
            await Task.Delay(1200);

            bool webReady = await WaitForCameraWebAsync(ip, temporaryIp.ToString());
            OpenUrlInDefaultBrowser("http://" + ip);
            await ShowStatusAsync(webReady ? "Opened " + ip + " through " + adapterAlias + "." : "Opened " + ip + ". Camera web port did not answer yet.", 3500);
        }

        private async Task<bool> WaitForCameraWebAsync(string ip, string sourceIp)
        {
            for (int attempt = 1; attempt <= 8; attempt++)
            {
                await ShowStatusAsync("Waiting for camera web page " + ip + " (" + attempt + "/8)...");

                if (await NetworkTools.IsTcpPortOpenAsync(ip, 80, 1500, sourceIp))
                    return true;

                if (await NetworkTools.IsTcpPortOpenAsync(ip, 443, 1500, sourceIp))
                    return true;

                await Task.Delay(750);
            }

            return false;
        }

        private void OpenUrlInDefaultBrowser(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open browser:\r\n" + ex.Message, "Open Browser", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static int GetSubnetPrefixLength(IPAddress subnetMask)
        {
            if (subnetMask == null)
                return 24;

            int prefix = 0;
            foreach (byte b in subnetMask.GetAddressBytes())
            {
                byte value = b;
                for (int i = 0; i < 8; i++)
                {
                    if ((value & 0x80) == 0)
                        return prefix;

                    prefix++;
                    value <<= 1;
                }
            }

            return prefix;
        }

        private void clearListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "The current list will be cleared. Are you sure?", "Clear the discovered item list",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                foundDeviceList.Clear();
                dataGridView1.Refresh();

                if (scanEvent != null)
                    scanEvent.Invoke();
            }
        }

        private void enableIPv6ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Config.getInstance().EnableIPv6 = !Config.getInstance().EnableIPv6;

            enableIPv6ToolStripMenuItem.Checked = Config.getInstance().EnableIPv6;
        }

        private async void exhaustiveprotocolsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "[Brand Filter] Opening dialog");
            using (var form = new BrandFilterForm())
            {
                DialogResult result = form.ShowDialog(this);

                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "[Brand Filter] Dialog returned: " + result);

                if (result != DialogResult.OK)
                    return;

                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "[Brand Filter] Starting automatic Normal Scan");

                await StartAutoScanAsync();
            }
        }

        private void showUnconfiguredDevicesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bool status;

            status = Config.getInstance().ForceZeroConf || Config.getInstance().ForceLinkLocal;
            status = !status;

            Config.getInstance().ForceZeroConf = status;
            Config.getInstance().ForceLinkLocal = status;

            showUnconfiguredDevicesToolStripMenuItem.Checked = status;
            ApplyUnconfiguredDevicesFilter();
            UpdateDeviceCountLabel();
        }

        private void ApplyUnconfiguredDevicesFilter()
        {
            if (binding == null)
                return;

            bool showUnconfigured = Config.getInstance().ForceZeroConf || Config.getInstance().ForceLinkLocal;
            binding.Filter = showUnconfigured ? "" : BuildRecognizedDevicesOnlyFilter();
        }

        private string BuildRecognizedDevicesOnlyFilter()
        {
            string vendorColumn = EscapeFilterColumn(ColumnNames[(int)Columns.Vendor]);
            string protocolColumn = EscapeFilterColumn(ColumnNames[(int)Columns.Protocol]);
            string typeColumn = EscapeFilterColumn(ColumnNames[(int)Columns.Type]);

            return "NOT (" +
                ContainsFilter(vendorColumn, "WSDiscovery") + " OR " +
                ContainsFilter(vendorColumn, "SSDP") + " OR " +
                ContainsFilter(vendorColumn, "VMS/Windows") + " OR " +
                EqualsFilter(vendorColumn, "Discovery") + " OR " +
                EqualsFilter(vendorColumn, "Unknown") + " OR " +
                EqualsFilter(vendorColumn, "Google") + " OR " +
                ContainsFilter(protocolColumn, "WSDiscovery") + " OR " +
                ContainsFilter(protocolColumn, "SSDP") + " OR " +
                EqualsFilter(protocolColumn, "Discovery") + " OR " +
                EqualsFilter(protocolColumn, "Unknown") + " OR " +
                EqualsFilter(protocolColumn, "Google") + " OR " +
                EqualsFilter(typeColumn, "anonymous") + " OR " +
                EqualsFilter(typeColumn, "VMS Windows") + " OR " +
                ContainsFilter(typeColumn, "NetworkVideoTransmitter") +
                ")";
        }

        private static string EscapeFilterColumn(string columnName)
        {
            return "[" + (columnName ?? "").Replace("]", "\\]") + "]";
        }

        private static string ContainsFilter(string escapedColumnName, string value)
        {
            return "Convert(" + escapedColumnName + ", 'System.String') LIKE '%" + EscapeFilterValue(value) + "%'";
        }

        private static string EqualsFilter(string escapedColumnName, string value)
        {
            return "Convert(" + escapedColumnName + ", 'System.String') = '" + EscapeFilterValue(value) + "'";
        }

        private static string EscapeFilterValue(string value)
        {
            return (value ?? "").Replace("'", "''");
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (scanEvent != null)
                scanEvent.Invoke();
        }

        private void ScannerWindow_Shown(object sender, EventArgs e)
        {
            enableIPv6ToolStripMenuItem.Checked = Config.getInstance().EnableIPv6;
            exhaustiveprotocolsToolStripMenuItem.Checked = Config.getInstance().ForceGenericProtocols;
            showUnconfiguredDevicesToolStripMenuItem.Checked = Config.getInstance().ForceZeroConf || Config.getInstance().ForceLinkLocal;

            BeginInvoke(new MethodInvoker(() =>
            {
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 1000;
                t.Tick += (s, ev) =>
                {
                    t.Stop();
                    t.Dispose();
                    scanButton.PerformClick();
                };
                t.Start();
            }));
        }

        private void newVersion_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/julienblitte/UniversalScanner/releases/latest");
        }

        private void label1_Click(object sender, EventArgs e)
        {
        }
        private List<string> GetLocalSubnets()
        {
            List<string> subnets = new List<string>();

            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (UnicastIPAddressInformation ipInfo in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ipInfo.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    string ip = ipInfo.Address.ToString();

                    if (ip.StartsWith("169.254."))
                        continue;

                    string[] parts = ip.Split('.');

                    if (parts.Length == 4)
                    {
                        string subnet = parts[0] + "." + parts[1] + "." + parts[2];

                        if (!subnets.Contains(subnet))
                            subnets.Add(subnet);
                    }
                }
            }

            return subnets;
        }

        private void StartTiandyAssistScan(string subnet)
        {
            if (string.IsNullOrWhiteSpace(subnet))
                return;

            Task.Run(async () => await ScanTiandyAssistAsync(subnet));
        }

        private async Task ScanTiandyAssistAsync(string subnet)
        {
            if (tiandyAssistScanRunning)
                return;

            tiandyAssistScanRunning = true;

            try
            {
                if (string.IsNullOrWhiteSpace(subnet))
                    return;

                List<Task> tasks = new List<Task>();
                SemaphoreSlim throttle = new SemaphoreSlim(8);

                for (int i = 1; i <= 254; i++)
                {
                    string ip = subnet + "." + i;

                    if (IsOldIpSuppressed(ip))
                        continue;

                    await throttle.WaitAsync();
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            if (!await NetworkTools.IsDeviceReachableAsync(ip, 700))
                                return;

                            var sw_mac = System.Diagnostics.Stopwatch.StartNew(); string mac = await NetworkTools.GetMacAsync(ip, 2, 150); sw_mac.Stop(); StartupDiagnostics.Increment("MacProbes"); StartupDiagnostics.AddTimer("MacProbes", sw_mac.ElapsedMilliseconds); StartupDiagnostics.LogSlowOperation("MAC", ip, "Unknown", sw_mac.ElapsedMilliseconds, !string.IsNullOrEmpty(mac));
                            string macVendor = NetworkTools.GetVendor(mac);

                            if (!IsTiandyText(macVendor))
                                return;

                            string pingText = "Reachable";
                            using (Ping ping = new Ping())
                            {
                                try
                                {
                                    var sw_ping = System.Diagnostics.Stopwatch.StartNew(); PingReply reply = await ping.SendPingAsync(ip, 700); sw_ping.Stop(); StartupDiagnostics.Increment("PingProbes"); StartupDiagnostics.AddTimer("PingProbes", sw_ping.ElapsedMilliseconds); StartupDiagnostics.LogSlowOperation("Ping", ip, "Unknown", sw_ping.ElapsedMilliseconds, reply.Status == System.Net.NetworkInformation.IPStatus.Success);
                                    pingText = reply.Status == IPStatus.Success
                                        ? reply.RoundtripTime + " ms"
                                        : "No ping";
                                }
                                catch
                                {
                                    pingText = "No ping";
                                }
                            }

                            string activation = "Unknown";
                            string model = "Unknown";
                            string tiandyUniqueId = mac;
                            if (string.IsNullOrWhiteSpace(tiandyUniqueId))
                                tiandyUniqueId = mac;

                            BeginInvoke(new MethodInvoker(() =>
                            {
                                DataRow existingRow = FindDeviceRow(ip, mac);
                                if (existingRow != null)
                                {
                                    existingRow[(int)Columns.Vendor] = "Tiandy";
                                    existingRow[(int)Columns.Protocol] = "Ping";
                                    existingRow[(int)Columns.Ping] = pingText;
                                    existingRow[(int)Columns.Activation] = string.IsNullOrWhiteSpace(activation) ? "Unknown" : activation;
                                    existingRow[(int)Columns.MAC] = mac;
                                    existingRow[(int)Columns.MACVendor] = macVendor;
                                    if (!string.IsNullOrWhiteSpace(tiandyUniqueId))
                                        existingRow[(int)Columns.UniqueId] = NormalizeUniqueIdForDisplay(tiandyUniqueId);
                                    if (string.IsNullOrWhiteSpace(existingRow[(int)Columns.Type]?.ToString()))
                                        existingRow[(int)Columns.Type] = "Unknown";
                                    QueueOnvifProfileLookup(ip, existingRow[(int)Columns.UniqueId]?.ToString());
                                    return;
                                }

                                foundDeviceList.Rows.Add(
                                    "Tiandy",
                                    "Ping",
                                    0,
                                    ip,
                                    pingText,
                                    string.IsNullOrWhiteSpace(activation) ? "Unknown" : activation,
                                    mac,
                                    macVendor,
                                    model,
                                    "Unknown",
                                    NormalizeUniqueIdForDisplay(tiandyUniqueId));

                                QueueOnvifProfileLookup(ip, tiandyUniqueId);
                            }));
                        }
                        catch
                        {
                        }
                        finally
                        {
                            throttle.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);
            }
            catch
            {
            }
            finally
            {
                tiandyAssistScanRunning = false;
            }
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, int timeoutMs, T fallback)
        {
            if (task == null)
                return fallback;

            Task completed = await Task.WhenAny(task, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (completed != task)
                return fallback;

            try
            {
                return await task.ConfigureAwait(false);
            }
            catch
            {
                return fallback;
            }
        }

        private static void ObserveFault(Task task)
        {
            task.ContinueWith(
                faultedTask =>
                {
                    AggregateException ignored = faultedTask.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static async Task<T> AwaitWithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!cancellationToken.CanBeCanceled)
                return await task;

            TaskCompletionSource<bool> cancellationSignal =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (cancellationToken.Register(() => cancellationSignal.TrySetResult(true)))
            {
                Task completed = await Task.WhenAny(task, cancellationSignal.Task);
                if (completed != task)
                {
                    ObserveFault(task);
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return await task;
        }

        private void PrepareSubnetScanUi()
        {
            currentSubnetScanIndex = 0;
            currentSubnetScanIp = "";

            if (scanProgressBar != null)
            {
                scanProgressBar.Minimum = 0;
                scanProgressBar.Maximum = 254;
                scanProgressBar.Value = 0;
                scanProgressBar.Visible = true;
                scanProgressBar.Refresh();
            }

            SetSubnetScanStatus("Preparing subnet scan...");
        }

        private string BuildSubnetScanStoppedStatus()
        {
            if (currentSubnetScanIndex <= 0 || string.IsNullOrWhiteSpace(currentSubnetScanIp))
                return "Subnet scan stopped before the IP sweep began";

            return "Subnet scan stopped at " + currentSubnetScanIp +
                   " (" + currentSubnetScanIndex + "/254)";
        }

        private async Task ScanSubnetAsync(string subnet, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanProgressBar.Minimum = 0;
            scanProgressBar.Maximum = 254;
            scanProgressBar.Value = 0;

            for (int i = 1; i <= 254; i++)
            {
                if (stopSubnetScan || cancellationToken.IsCancellationRequested)
                    break;

                currentSubnetScanIndex = i;
                currentSubnetScanIp = subnet + "." + i;
                scanProgressBar.Value = i;
                scanProgressBar.Refresh();

                SetSubnetScanStatus("Scanning " + currentSubnetScanIp +
                                    " (" + i + "/254)");

                Application.DoEvents();
                cancellationToken.ThrowIfCancellationRequested();

                if (IsDisposed)
                    return;

                string ip = currentSubnetScanIp;

                if (IsOldIpSuppressed(ip))
                    continue;

                try
                {
                    bool reachable = await NetworkTools.IsDeviceReachableAsync(ip, 1000, cancellationToken);

                    if (reachable)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (IsOldIpSuppressed(ip))
                            continue;

                        await Task.Delay(100, cancellationToken);

                        if (IsOldIpSuppressed(ip))
                            continue;

                        string pingText = "Reachable";

                        try
                        {
                            var sw_ping = System.Diagnostics.Stopwatch.StartNew(); PingReply reply = await NetworkTools.PingAsync(ip, 1000, cancellationToken); sw_ping.Stop(); StartupDiagnostics.Increment("PingProbes"); StartupDiagnostics.AddTimer("PingProbes", sw_ping.ElapsedMilliseconds); StartupDiagnostics.LogSlowOperation("Ping", ip, "Unknown", sw_ping.ElapsedMilliseconds, reply != null && reply.Status == System.Net.NetworkInformation.IPStatus.Success);
                            pingText = reply != null && reply.Status == IPStatus.Success
                                ? reply.RoundtripTime + " ms"
                                : "No ping";
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch
                        {
                            pingText = "No ping";
                        }

                        var sw_onvif = System.Diagnostics.Stopwatch.StartNew(); VendorProbeResult detected =
                            await SubnetVendorProbe.DetectAsync(ip, cancellationToken); sw_onvif.Stop(); StartupDiagnostics.Increment("OnvifProbes"); StartupDiagnostics.AddTimer("OnvifProbes", sw_onvif.ElapsedMilliseconds); StartupDiagnostics.LogSlowOperation("ONVIF", ip, "Unknown", sw_onvif.ElapsedMilliseconds, detected != null && detected.Vendor != "Unknown");

                        var sw_mac = System.Diagnostics.Stopwatch.StartNew(); string mac = await NetworkTools.GetMacAsync(ip, 5, 300, cancellationToken); sw_mac.Stop(); StartupDiagnostics.Increment("MacProbes"); StartupDiagnostics.AddTimer("MacProbes", sw_mac.ElapsedMilliseconds); StartupDiagnostics.LogSlowOperation("MAC", ip, "Unknown", sw_mac.ElapsedMilliseconds, !string.IsNullOrEmpty(mac));
                        string macVendor = NetworkTools.GetVendor(mac);

                        string displayVendor = detected.Vendor;
                        if (displayVendor == "Unknown" || string.IsNullOrWhiteSpace(displayVendor))
                        {
                            if (!string.IsNullOrWhiteSpace(macVendor))
                                displayVendor = macVendor + " (OUI)";
                        }

                        if (string.IsNullOrWhiteSpace(displayVendor))
                        {
                            displayVendor = "Unknown";
                        }

                        if (IsHoneywellOuiVendor(macVendor))
                        {
                            displayVendor = "Honeywell";
                        }
                        else if ((displayVendor == "Unknown" || IsGenericWsDiscoveryText(displayVendor) || IsGenericSsdpText(displayVendor) || IsUniviewText(displayVendor)) &&
                                 (IsUniviewOuiVendor(macVendor) || IsUniviewText(detected.Vendor) || IsUniviewText(detected.Type)))
                        {
                            displayVendor = "Uniview";
                        }
                        else if ((displayVendor == "Unknown" || IsGenericWsDiscoveryText(displayVendor) || IsGenericSsdpText(displayVendor)) &&
                                 (IsDahuaText(macVendor) || IsDahuaText(detected.Vendor) || IsDahuaText(detected.Type)))
                        {
                            displayVendor = "Dahua";
                        }
                        else if ((displayVendor == "Unknown" || IsGenericWsDiscoveryText(displayVendor) || IsGenericSsdpText(displayVendor)) &&
                                 (IsSunellOuiVendor(macVendor) || IsSunellText(detected.Vendor) || IsSunellText(detected.Type)))
                        {
                            displayVendor = "Sunell";
                        }

                        bool detectedHoneywell =
                            IsHoneywellText(detected.Protocol) ||
                            IsHoneywellText(detected.Vendor) ||
                            IsHoneywellText(detected.Type) ||
                            IsHoneywellOuiVendor(macVendor);

                        bool detectedHikvision =
                            !detectedHoneywell &&
                            (IsHikvisionText(detected.Protocol) ||
                             IsHikvisionText(detected.Vendor) ||
                             IsHikvisionText(detected.Type));

                        bool detectedUniview =
                            !detectedHoneywell &&
                            !detectedHikvision &&
                            (IsUniviewText(detected.Protocol) ||
                             IsUniviewText(detected.Vendor) ||
                             IsUniviewText(detected.Type) ||
                             IsUniviewOuiVendor(macVendor));

                        bool detectedDahua =
                            !detectedHoneywell &&
                            !detectedHikvision &&
                            !detectedUniview &&
                            (IsDahuaText(detected.Protocol) ||
                             IsDahuaText(detected.Vendor) ||
                             IsDahuaText(detected.Type) ||
                             IsDahuaText(macVendor));

                        bool detectedSunell =
                            !detectedHoneywell &&
                            !detectedHikvision &&
                            !detectedUniview &&
                            !detectedDahua &&
                            (IsSunellText(detected.Protocol) ||
                             IsSunellText(detected.Vendor) ||
                             IsSunellText(detected.Type) ||
                             IsSunellOuiVendor(macVendor));

                        bool detectedTiandy =
                            !detectedHoneywell &&
                            !detectedHikvision &&
                            !detectedUniview &&
                            !detectedDahua &&
                            !detectedSunell &&
                            (IsTiandyText(detected.Protocol) ||
                             IsTiandyText(detected.Vendor) ||
                             IsTiandyText(detected.Type) ||
                             IsTiandyText(macVendor));

                        if (detectedTiandy)
                            displayVendor = "Tiandy";

                        bool detectedBosch =
                            !detectedHoneywell &&
                            !detectedHikvision &&
                            !detectedUniview &&
                            !detectedDahua &&
                            !detectedSunell &&
                            !detectedTiandy &&
                            (IsBoschText(detected.Protocol) ||
                             IsBoschText(detected.Vendor) ||
                             IsBoschText(detected.Type));

                        bool detectedAxis =
                            !detectedHoneywell &&
                            !detectedHikvision &&
                            !detectedUniview &&
                            !detectedDahua &&
                            !detectedSunell &&
                            !detectedTiandy &&
                            !detectedBosch &&
                            (IsAxisText(detected.Protocol) ||
                             IsAxisText(detected.Vendor) ||
                             IsAxisText(detected.Type) ||
                             IsAxisOuiVendor(macVendor));

                        if (detectedBosch)
                            displayVendor = "Bosch";

                        string activation = "N/A";

                        if (detectedHoneywell)
                        {
                            activation = NormalizeHoneywellActivationStatus(
                                detected.Type,
                                await AwaitWithCancellation(
                                    DeviceSDK.Honeywell.HoneywellActivationStatusProbe.GetStatusAsync(ip),
                                    cancellationToken));
                        }
                        else if (detectedHikvision)
                        {
                            activation = await AwaitWithCancellation(
                                HikvisionActivationStatusProbe.GetStatusAsync(ip),
                                cancellationToken);
                        }
                        else if (detectedUniview)
                        {
                            activation = await AwaitWithCancellation(
                                DeviceSDK.Uniview.UniviewActivationStatusProbe.GetStatusAsync(ip),
                                cancellationToken);
                        }
                        else if (detectedDahua)
                        {
                            activation = IsDahuaText(macVendor)
                                ? "Active"
                                : await AwaitWithCancellation(
                                    Task.Run(() => DeviceSDK.Dahua.DahuaActivationStatusProbe.GetStatus(ip)),
                                    cancellationToken);
                        }
                        else if (detectedSunell)
                        {
                            activation = await AwaitWithCancellation(
                                Task.Run(() => DeviceSDK.Sunell.SunellActivationStatusProbe.GetStatus(ip)),
                                cancellationToken);
                        }
                        else if (detectedTiandy)
                        {
                            activation = await AwaitWithCancellation(
                                Task.Run(() => DeviceSDK.Tiandy.TiandyActivationStatusProbe.GetStatus(ip)),
                                cancellationToken);
                        }
                        else if (detectedBosch)
                        {
                            activation = await AwaitWithCancellation(
                                DeviceSDK.Bosch.BoschActivationStatusProbe.GetStatusAsync(ip),
                                cancellationToken);
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        DataRow existingRow = FindDeviceRow(ip, mac);

                        if (existingRow != null)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (IsStaleHoneywellIdentityForIncomingMac(existingRow, mac, macVendor, detected))
                                ResetRowToCurrentGenericIdentity(existingRow, detected, macVendor);

                            existingRow[(int)Columns.Ping] = pingText;

                            if (detectedHoneywell || detectedHikvision || detectedUniview || detectedDahua || detectedSunell || detectedTiandy || detectedBosch)
                                existingRow[(int)Columns.Activation] = activation;
                            else if (!IsHikvisionCompatibleRow(existingRow) && !IsUniviewRow(existingRow) && !IsDahuaRow(existingRow) && !IsSunellRow(existingRow) && !IsTiandyRow(existingRow) && !IsBoschRow(existingRow))
                                existingRow[(int)Columns.Activation] = "N/A";

                            if (!string.IsNullOrWhiteSpace(mac))
                                existingRow[(int)Columns.MAC] = mac;

                            if (!string.IsNullOrWhiteSpace(macVendor))
                                existingRow[(int)Columns.MACVendor] = macVendor;

                            if (detectedHoneywell)
                                ApplyHoneywellIdentity(existingRow);
                            else if (detectedUniview && (IsGenericDiscoveryRow(existingRow) || IsUniviewRow(existingRow)))
                                ApplyUniviewIdentity(existingRow);
                            else if (detectedDahua && (IsGenericDiscoveryRow(existingRow) || IsDahuaRow(existingRow)))
                                ApplyDahuaIdentity(existingRow);
                            else if (detectedSunell && (IsGenericDiscoveryRow(existingRow) || IsSunellRow(existingRow)))
                                ApplySunellIdentity(existingRow);
                            else if (detectedTiandy && (IsGenericDiscoveryRow(existingRow) || IsTiandyRow(existingRow)))
                            {
                                existingRow[(int)Columns.Vendor] = "Tiandy";
                                existingRow[(int)Columns.Protocol] = "Ping";
                            }
                            else if (detectedBosch && (IsGenericDiscoveryRow(existingRow) || IsBoschRow(existingRow)))
                                ApplyBoschIdentity(existingRow);

                            string currentVendor = existingRow[(int)Columns.Vendor]?.ToString();

                            bool preserveExistingIdentity = ShouldPreserveExistingIdentity(existingRow, detected.Protocol, detected.Type);

                            if (!preserveExistingIdentity &&
                                !string.IsNullOrWhiteSpace(displayVendor) &&
                                displayVendor != "Unknown" &&
                                (string.IsNullOrWhiteSpace(currentVendor) || currentVendor == "Unknown" || IsGenericWsDiscoveryText(currentVendor)))
                            {
                                existingRow[(int)Columns.Vendor] = displayVendor;
                            }

                            if (!preserveExistingIdentity && detectedHoneywell)
                                existingRow[(int)Columns.Protocol] = "Honeywell";
                            else if (!preserveExistingIdentity && !detectedUniview && !detectedDahua && !detectedSunell && !detectedTiandy && !string.IsNullOrWhiteSpace(detected.Protocol) && detected.Protocol != "Ping")
                                existingRow[(int)Columns.Protocol] = detected.Protocol;

                            if (!preserveExistingIdentity && !detectedUniview && !detectedDahua && !detectedSunell && !detectedTiandy && !string.IsNullOrWhiteSpace(detected.Type) && detected.Type != "Unknown")
                                existingRow[(int)Columns.Type] = detected.Type;

                            QueueOnvifProfileLookup(ip, existingRow[(int)Columns.UniqueId]?.ToString());

                            continue;
                        }

                        string displayProtocol = detectedHoneywell ? "Honeywell" : detectedUniview ? "Uniview" : detectedDahua ? "Dahua" : detectedSunell ? "Sunell" : detectedTiandy ? "Ping" : detected.Protocol;
                        string displayType = detectedUniview &&
                            (string.IsNullOrWhiteSpace(detected.Type) || detected.Type == "Unknown" || IsGenericNetworkVideoType(detected.Type) || IsGenericUniviewType(detected.Type))
                            ? "Unknown"
                            : detected.Type;
                        if (detectedHoneywell)
                        {
                            string honeywellModel = DeviceSDK.Dahua.DahuaDiscoveryState.GetDeviceModel(ip, mac, "");
                            if (!string.IsNullOrWhiteSpace(honeywellModel))
                                displayType = honeywellModel;
                        }
                        if (detectedDahua && IsGenericDahuaType(displayType))
                        {
                            string dahuaModel = DeviceSDK.Dahua.DahuaDiscoveryState.GetDeviceModel(ip, mac, "");
                            if (!string.IsNullOrWhiteSpace(dahuaModel))
                                displayType = dahuaModel;
                        }
                        if (detectedSunell &&
                            (string.IsNullOrWhiteSpace(displayType) || displayType == "Unknown" || IsGenericNetworkVideoType(displayType) || IsGenericWsDiscoveryText(displayType)))
                        {
                            string sunellModel = await AwaitWithCancellation(
                                Task.Run(() => DeviceSDK.Sunell.SunellActivationStatusProbe.GetModel(ip)),
                                cancellationToken);
                            displayType = string.IsNullOrWhiteSpace(sunellModel)
                                ? "Network Video Transmitter"
                                : sunellModel;
                        }
                        if (detectedTiandy &&
                            (string.IsNullOrWhiteSpace(displayType) || displayType == "Unknown" || IsGenericNetworkVideoType(displayType) || IsGenericWsDiscoveryText(displayType)))
                        {
                            string tiandyModel = await AwaitWithCancellation(
                                Task.Run(() => DeviceSDK.Tiandy.TiandyInfoProbe.GetModelAsync(ip)),
                                cancellationToken);
                            displayType = string.IsNullOrWhiteSpace(tiandyModel)
                                ? "Unknown"
                                : tiandyModel;
                        }
                        string displayUniqueId = detectedTiandy
                            ? FirstNonEmpty(DeviceSDK.Tiandy.TiandyInfoProbe.LastUniqueId, mac)
                            : "";

                        cancellationToken.ThrowIfCancellationRequested();
                        foundDeviceList.Rows.Add(
                            displayVendor,
                            displayProtocol,
                            0,
                            ip,
                            pingText,
                            activation,
                            mac,
                            macVendor,
                            displayType,
                            "Unknown",
                            NormalizeUniqueIdForDisplay(displayUniqueId)
                        );

                        QueueOnvifProfileLookup(ip, NormalizeUniqueIdForDisplay(displayUniqueId));
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                }
            }

            if (!stopSubnetScan && !cancellationToken.IsCancellationRequested)
            {
                scanProgressBar.Value = 254;
                scanProgressBar.Refresh();
                SetSubnetScanStatus("Subnet scan completed");
            }
        }

        private void scanProgressBar_Click(object sender, EventArgs e)
        {
        }


        private void stopSubnetButton_Click_1(object sender, EventArgs e)
        {
            subnetStopRequestVersion++;

            Task subnetScanTask = activeSubnetScanTask;
            if (subnetScanTask != null && !subnetScanTask.IsCompleted)
                SetSubnetScanStatus("Stopping subnet scan...");

            RequestSubnetScanStop();
        }


        private async void scanSubnetButton_Click(object sender, EventArgs e)
        {
            await RunTrackedSubnetScanAsync(ScanSelectedSubnetAsync);
        }

        private async Task ScanSelectedSubnetAsync(CancellationToken cancellationToken)
        {
            currentScanSessionId = Guid.NewGuid();

            PrepareSubnetScanUi();
            ClearDeviceListPreservingRecentIpChanges();

            if (scanEvent != null)
                scanEvent.Invoke();

            await Task.Delay(2000, cancellationToken);

            string subnet = subnetTextBox.Text.Trim();
            await ScanSubnetAsync(subnet, cancellationToken);
        }

        private void exportCSVToolStripMenuItem_Click(object sender, EventArgs e)
        {
            exportAsCSV();
        }

        private async void scanAllLocalSubnetsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await RunTrackedSubnetScanAsync(ScanAllLocalSubnetsAsync);
        }

        private async Task ScanAllLocalSubnetsAsync(CancellationToken cancellationToken)
        {
            currentScanSessionId = Guid.NewGuid();

            PrepareSubnetScanUi();
            ClearDeviceListPreservingRecentIpChanges();

            if (scanEvent != null)
                scanEvent.Invoke();

            await Task.Delay(2000, cancellationToken);

            List<string> subnets = GetLocalSubnets();

            foreach (string subnet in subnets)
            {
                if (stopSubnetScan || cancellationToken.IsCancellationRequested)
                    break;

                await ScanSubnetAsync(subnet, cancellationToken);
            }
        }
        private void saveScanResults()
        {
            SaveFileDialog saveAs = new SaveFileDialog();
            saveAs.Filter = "UniversalScanner Scan|*.usscan";
            saveAs.Title = "Save scan results";
            saveAs.OverwritePrompt = true;
            saveAs.CheckPathExists = true;

            if (saveAs.ShowDialog() != DialogResult.OK)
                return;

            StringBuilder sb = new StringBuilder();

            foreach (DataRow row in foundDeviceList.Rows)
            {
                sb.AppendLine(string.Join("|", new string[]
                {
            row[(int)Columns.Vendor]?.ToString()?.Replace("|", " ") ?? "",
            row[(int)Columns.Protocol]?.ToString()?.Replace("|", " ") ?? "",
            row[(int)Columns.Version]?.ToString() ?? "0",
            row[(int)Columns.IPAddress]?.ToString()?.Replace("|", " ") ?? "",
            row[(int)Columns.Ping]?.ToString()?.Replace("|", " ") ?? "",
            row[(int)Columns.Activation]?.ToString()?.Replace("|", " ") ?? "",
            row[(int)Columns.MAC]?.ToString()?.Replace("|", " ") ?? "",
            row[(int)Columns.MACVendor]?.ToString()?.Replace("|", " ") ?? "",
            row[(int)Columns.Type]?.ToString()?.Replace("|", " ") ?? "",
            row[(int)Columns.OnvifProfile]?.ToString()?.Replace("|", " ") ?? "",
            row[(int)Columns.UniqueId]?.ToString()?.Replace("|", " ") ?? ""
                }));
            }

            File.WriteAllText(saveAs.FileName, sb.ToString(), Encoding.UTF8);

            MessageBox.Show(this, "Scan results saved successfully.", "Save Scan Results",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void loadScanResults()
        {
            OpenFileDialog openFile = new OpenFileDialog();
            openFile.Filter = "UniversalScanner Scan|*.usscan";
            openFile.Title = "Load scan results";
            openFile.CheckFileExists = true;

            if (openFile.ShowDialog() != DialogResult.OK)
                return;

            foundDeviceList.Clear();

            string[] lines = File.ReadAllLines(openFile.FileName, Encoding.UTF8);

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] parts = line.Split('|');

                if (parts.Length < 9)
                    continue;

                int version = 0;
                int.TryParse(parts[2], out version);

                if (parts.Length >= 11)
                {
                    foundDeviceList.Rows.Add(
                        parts[0],
                        parts[1],
                        version,
                        parts[3],
                        parts[4],
                        parts[5],
                        parts[6],
                        parts[7],
                        parts[8],
                        parts[9],
                        NormalizeUniqueIdForDisplay(parts[10])
                    );
                }
                else if (parts.Length >= 10)
                {
                    foundDeviceList.Rows.Add(
                        parts[0],
                        parts[1],
                        version,
                        parts[3],
                        parts[4],
                        parts[5],
                        parts[6],
                        parts[7],
                        parts[8],
                        "Unknown",
                        NormalizeUniqueIdForDisplay(parts[9])
                    );
                }
                else
                {
                    foundDeviceList.Rows.Add(
                        parts[0],
                        parts[1],
                        version,
                        parts[3],
                        parts[4],
                        "Unknown",
                        parts[5],
                        parts[6],
                        parts[7],
                        "Unknown",
                        NormalizeUniqueIdForDisplay(parts[8])
                    );
                }
            }

            dataGridView1.Refresh();

            MessageBox.Show(this, "Scan results loaded successfully.", "Load Scan Results",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void saveScanResultsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            saveScanResults();
        }

        private void loadScanResultsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            loadScanResults();
        }
        private void deleteSelectedRows()
        {
            if (dataGridView1.SelectedRows.Count == 0)
                return;

            int count = dataGridView1.SelectedRows.Count;

            if (MessageBox.Show(this,
                "Delete " + count + " selected row(s) from the current results?",
                "Delete Selected Rows",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            foreach (DataGridViewRow gridRow in dataGridView1.SelectedRows)
            {
                if (gridRow.DataBoundItem is DataRowView rowView)
                {
                    rowView.Row.Delete();
                }
            }

            foundDeviceList.AcceptChanges();
        }

        private void deleteSelectedRowsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            deleteSelectedRows();
        }
        private void copyIPAddressToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count == 0)
                return;

            string ip = dataGridView1.SelectedRows[0]
                .Cells[(int)Columns.IPAddress]
                .Value?
                .ToString();

            if (!string.IsNullOrWhiteSpace(ip))
                Clipboard.SetText(ip);
        }
        private void copyMACAddressToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count == 0)
                return;

            string mac = dataGridView1.SelectedRows[0].Cells[(int)Columns.MAC].Value?.ToString();

            if (!string.IsNullOrWhiteSpace(mac))
                Clipboard.SetText(mac);
        }

        private void copyVendorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count == 0)
                return;

            string vendor = dataGridView1.SelectedRows[0].Cells[(int)Columns.Vendor].Value?.ToString();

            if (!string.IsNullOrWhiteSpace(vendor))
                Clipboard.SetText(vendor);
        }

        private void copyTypeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count == 0)
                return;

            string type = dataGridView1.SelectedRows[0].Cells[(int)Columns.Type].Value?.ToString();

            if (!string.IsNullOrWhiteSpace(type))
                Clipboard.SetText(type);
        }

        private void copyDeviceInformationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count == 0)
                return;

            DataGridViewRow row = dataGridView1.SelectedRows[0];

            string info =
                "Vendor: " + row.Cells[(int)Columns.Vendor].Value + Environment.NewLine +
                "Protocol: " + row.Cells[(int)Columns.Protocol].Value + Environment.NewLine +
                "IP Address: " + row.Cells[(int)Columns.IPAddress].Value + Environment.NewLine +
                "Ping: " + row.Cells[(int)Columns.Ping].Value + Environment.NewLine +
                "Activation: " + row.Cells[(int)Columns.Activation].Value + Environment.NewLine +
                "MAC Address: " + row.Cells[(int)Columns.MAC].Value + Environment.NewLine +
                "OUI Vendor: " + row.Cells[(int)Columns.MACVendor].Value + Environment.NewLine +
                "Model: " + row.Cells[(int)Columns.Type].Value + Environment.NewLine +
                "ONVIF Profile: " + row.Cells[(int)Columns.OnvifProfile].Value + Environment.NewLine +
"Unique ID: " + row.Cells[(int)Columns.UniqueId].Value;

            Clipboard.SetText(info);
        }

        private void onvifEventMonitorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count == 0)
                return;

            DataGridViewRow row = dataGridView1.SelectedRows[0];
            if (IsDahuaSwitchRow(GetDataRowFromGridRow(row)))
                return;
            string ip = row.Cells[(int)Columns.IPAddress].Value?.ToString() ?? "";

            if (!IPAddress.TryParse(ip, out IPAddress parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            {
                MessageBox.Show(this, "Please select a device with a valid IPv4 address.", "ONVIF Live Event Monitor",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string vendor = row.Cells[(int)Columns.Vendor].Value?.ToString() ?? "";
            string model = row.Cells[(int)Columns.Type].Value?.ToString() ?? "";
            string deviceName = row.Cells[(int)Columns.UniqueId].Value?.ToString() ?? "";

            UniversalLoginManager.GetUniversalCredentials(out string username, out string password);

            using (OnvifEventMonitorForm form = new OnvifEventMonitorForm(parsed.ToString(), 80, username, password, vendor, model, deviceName))
            {
                form.ShowDialog(this);
            }
        }

        private async void showLiveByVlcToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count == 0)
                return;

            ApplyAiBrandOverridesToSelectedRowsForAction();

            DataGridViewRow row = dataGridView1.SelectedRows[0];
            if (IsDahuaSwitchRow(GetDataRowFromGridRow(row)))
                return;
            string ip = row.Cells[(int)Columns.IPAddress].Value?.ToString() ?? "";

            if (!IPAddress.TryParse(ip, out IPAddress parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            {
                MessageBox.Show(this, "Please select a device with a valid IPv4 address.", "Show Live by VLC",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string vendor = row.Cells[(int)Columns.Vendor].Value?.ToString() ?? "";
            string protocol = row.Cells[(int)Columns.Protocol].Value?.ToString() ?? "";
            string ouiVendor = row.Cells[(int)Columns.MACVendor].Value?.ToString() ?? "";
            string model = row.Cells[(int)Columns.Type].Value?.ToString() ?? "";

            string rtspVendorContext = string.Join(" ", new[] { vendor, protocol, ouiVendor });
            VlcMosaicStreamChoice streamChoice;
            using (VlcStreamChoiceForm streamForm = new VlcStreamChoiceForm(1))
            {
                if (streamForm.ShowDialog(this) != DialogResult.OK)
                    return;

                streamChoice = streamForm.SelectedStream;
            }

            bool useSubStream = streamChoice == VlcMosaicStreamChoice.Sub;
            using (RtspLiveViewForm form = new RtspLiveViewForm(parsed.ToString(), rtspVendorContext, model, useSubStream))
            {
                UniversalLoginManager.GetUniversalCredentials(out string un, out string pw);
                form.PreFillCredentials(un, pw);

                if (UniversalLoginManager.HasUniversalCredentials() &&
                    await form.TryAutoLaunchAsync(true))
                    return;

                form.ShowDialog(this);
            }
        }

        private async void showSelectedByVlcToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ApplyAiBrandOverridesToSelectedRowsForAction();

            List<DataGridViewRow> rows = GetSelectedDeviceRows()
                .Where(row => row != null && !row.IsNewRow)
                .ToList();

            if (rows.Count == 0)
            {
                MessageBox.Show(this, "Please select one or more devices first.", "Show Selected by VLC",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (rows.Any(row => IsDahuaSwitchRow(GetDataRowFromGridRow(row))))
                return;

            if (!UniversalLoginManager.HasUniversalCredentials())
            {
                MessageBox.Show(this, "Set Universal Login first, then use Show Selected.", "Show Selected by VLC",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            UniversalLoginManager.GetUniversalCredentials(out string username, out string password);
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show(this, "Universal Login is missing a username or password.", "Show Selected by VLC",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string vlcPath = RtspLiveViewForm.FindVlcPath();
            if (string.IsNullOrWhiteSpace(vlcPath))
            {
                MessageBox.Show(this, "VLC was not found.\r\n\r\nPlease install VLC Media Player, or add vlc.exe to PATH.", "Show Selected by VLC",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            VlcMosaicStreamChoice streamChoice;
            using (VlcStreamChoiceForm streamForm = new VlcStreamChoiceForm(rows.Count))
            {
                if (streamForm.ShowDialog(this) != DialogResult.OK)
                    return;

                streamChoice = streamForm.SelectedStream;
            }

            List<VlcMosaicStream> streams = new List<VlcMosaicStream>();
            List<string> skippedIps = new List<string>();
            List<string> authRejectedIps = new List<string>();
            bool allowSingleRowHikvisionProbe = rows.Count == 1;
            bool shouldCheckDahuaRtspAuth = rows.Any(row => IsDahuaRow(GetDataRowFromGridRow(row)));

            foreach (DataGridViewRow row in rows)
            {
                await AddVlcStreamsForRowAsync(row, streamChoice, username, password, allowSingleRowHikvisionProbe, streams, skippedIps, authRejectedIps);
            }

            // Do not block VLC mosaic from the RTSP OPTIONS pre-check. Several
            // Hikvision/NVR firmwares reject OPTIONS even when VLC can play the
            // stream with the same credentials.
            string rejectedIp = authRejectedIps.FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(rejectedIp) && shouldCheckDahuaRtspAuth)
                rejectedIp = await FindRejectedRtspCredentialIpAsync(streams, username, password);

            if (!string.IsNullOrWhiteSpace(rejectedIp))
            {
                if (!PromptForVlcCredentials(rejectedIp, ref username, ref password))
                    return;

                UniversalLoginManager.SetUniversalCredentials(username, password);
                streams.Clear();
                skippedIps.Clear();
                authRejectedIps.Clear();

                foreach (DataGridViewRow row in rows)
                {
                    await AddVlcStreamsForRowAsync(row, streamChoice, username, password, allowSingleRowHikvisionProbe, streams, skippedIps, authRejectedIps);
                }

                rejectedIp = authRejectedIps.FirstOrDefault() ?? "";
                if (string.IsNullOrWhiteSpace(rejectedIp) && shouldCheckDahuaRtspAuth)
                    rejectedIp = await FindRejectedRtspCredentialIpAsync(streams, username, password);

                if (!string.IsNullOrWhiteSpace(rejectedIp))
                {
                    MessageBox.Show(this, "Authentication failed for " + rejectedIp + ". Please check the Universal Login username and password.", "Show Selected by VLC",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            if (streams.Count == 0)
            {
                MessageBox.Show(this, "No selected devices have a valid IPv4 address.", "Show Selected by VLC",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                VlcMosaicViewForm form = new VlcMosaicViewForm(vlcPath, streams);
                form.Show(this);

                string status = "Opening VLC mosaic for " + streams.Count + " selected device" + (streams.Count == 1 ? "" : "s") + "...";
                if (skippedIps.Count > 0)
                    status += " Skipped invalid IPs: " + string.Join(", ", skippedIps);
                _ = ShowStatusAsync(status, 4000);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to open VLC mosaic:\r\n\r\n" + ex.Message, "Show Selected by VLC",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task AddVlcStreamsForRowAsync(
            DataGridViewRow row,
            VlcMosaicStreamChoice streamChoice,
            string username,
            string password,
            bool allowGenericHikvisionProbe,
            List<VlcMosaicStream> streams,
            List<string> skippedIps,
            List<string> authRejectedIps)
        {
            string ip = row.Cells[(int)Columns.IPAddress].Value?.ToString() ?? "";
            if (!IPAddress.TryParse(ip, out IPAddress parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            {
                if (!string.IsNullOrWhiteSpace(ip))
                    skippedIps.Add(ip);
                return;
            }

            string vendor = row.Cells[(int)Columns.Vendor].Value?.ToString() ?? "";
            string protocol = row.Cells[(int)Columns.Protocol].Value?.ToString() ?? "";
            string ouiVendor = row.Cells[(int)Columns.MACVendor].Value?.ToString() ?? "";
            string model = row.Cells[(int)Columns.Type].Value?.ToString() ?? "";
            string rtspVendorContext = string.Join(" ", new[] { vendor, protocol, ouiVendor });
            bool useSubStream = streamChoice == VlcMosaicStreamChoice.Sub;
            string defaultPath = RtspLiveViewForm.GetRtspPath(rtspVendorContext, model, useSubStream);

            if (ShouldExpandHikvisionDvrMosaic(vendor, protocol, ouiVendor, model, defaultPath, allowGenericHikvisionProbe))
            {
                try
                {
                    List<HikvisionChannel> channels = await HikvisionStreamingHelper.GetChannelsAsync(parsed.ToString(), username, password);
                    if (channels != null && channels.Count > 1)
                    {
                        int streamCountBeforeDvr = streams.Count;
                        foreach (HikvisionChannel channel in channels)
                        {
                            int streamType = SelectHikvisionStreamType(channel, useSubStream);
                            int streamId;
                            try
                            {
                                streamId = checked((channel.PhysicalChannel * 100) + streamType);
                            }
                            catch
                            {
                                continue;
                            }

                            string path = "/Streaming/Channels/" + streamId;
                            streams.Add(new VlcMosaicStream
                            {
                                Title = parsed + "  Camera " + channel.PhysicalChannel + "  " + (useSubStream ? "Sub" : "Main"),
                                Url = BuildCredentialedRtspUrl(parsed.ToString(), path, username, password),
                                ProbeUrl = BuildPlainRtspUrl(parsed.ToString(), path)
                            });
                        }

                        if (streams.Count > streamCountBeforeDvr)
                            return;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    authRejectedIps.Add(parsed.ToString());
                    return;
                }
                catch
                {
                    // Fall back to the normal single-stream path for non-Hikvision devices or devices that block ISAPI.
                }
            }

            streams.Add(new VlcMosaicStream
            {
                Title = parsed + "  " + FirstNonEmpty(vendor, model, "Camera") + "  " + (useSubStream ? "Sub" : "Main"),
                Url = BuildCredentialedRtspUrl(parsed.ToString(), defaultPath, username, password),
                ProbeUrl = BuildPlainRtspUrl(parsed.ToString(), defaultPath)
            });
        }

        private static bool ShouldExpandHikvisionDvrMosaic(string vendor, string protocol, string ouiVendor, string model, string defaultPath, bool allowGenericProbe)
        {
            string text = string.Join(" ", new[] { vendor, protocol, ouiVendor, model }).ToLowerInvariant();
            if (text.Contains("hikvision") ||
                text.Contains("hiwatch") ||
                text.Contains("hik-connect") ||
                text.Contains("ds-") ||
                text.Contains("ids-") ||
                text.Contains("nvr") ||
                text.Contains("dvr"))
                return true;

            return allowGenericProbe && !string.IsNullOrWhiteSpace(defaultPath) &&
                   defaultPath.StartsWith("/Streaming/Channels/", StringComparison.OrdinalIgnoreCase);
        }

        private static int SelectHikvisionStreamType(HikvisionChannel channel, bool useSubStream)
        {
            int preferred = useSubStream ? 2 : 1;
            if (channel.SupportedStreamTypes.Contains(preferred))
                return preferred;

            if (channel.SupportedStreamTypes.Count > 0)
                return channel.SupportedStreamTypes[0];

            return preferred;
        }

        private static string BuildCredentialedRtspUrl(string ip, string path, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(path))
                path = "/";
            if (!path.StartsWith("/", StringComparison.Ordinal))
                path = "/" + path;

            string safeUser = Uri.EscapeDataString(username ?? "");
            string safePassword = Uri.EscapeDataString(password ?? "");
            return "rtsp://" + safeUser + ":" + safePassword + "@" + ip + ":554" + path;
        }

        private static string BuildPlainRtspUrl(string ip, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                path = "/";
            if (!path.StartsWith("/", StringComparison.Ordinal))
                path = "/" + path;

            return "rtsp://" + ip + ":554" + path;
        }

        private async Task<string> FindRejectedRtspCredentialIpAsync(IEnumerable<VlcMosaicStream> streams, string username, string password)
        {
            foreach (VlcMosaicStream stream in streams)
            {
                string probeUrl = stream?.ProbeUrl;
                if (string.IsNullOrWhiteSpace(probeUrl))
                    probeUrl = stream?.Url;

                if (string.IsNullOrWhiteSpace(probeUrl))
                    continue;

                try
                {
                    if (await RtspLiveViewForm.IsRtspAuthenticationRejectedAsync(probeUrl, username, password))
                    {
                        Uri uri = new Uri(probeUrl);
                        return uri.Host;
                    }
                }
                catch
                {
                }
            }

            return "";
        }

        private bool PromptForVlcCredentials(string ip, ref string username, ref string password)
        {
            using (Form dialog = new Form())
            using (Label messageLabel = new Label())
            using (Label usernameLabel = new Label())
            using (TextBox usernameBox = new TextBox())
            using (Label passwordLabel = new Label())
            using (TextBox passwordBox = new TextBox())
            using (CheckBox showPasswordBox = new CheckBox())
            using (Button okButton = new Button())
            using (Button cancelButton = new Button())
            {
                dialog.Text = "Show Selected by VLC";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ClientSize = new Size(430, 210);

                messageLabel.Text = "Universal Login was rejected by " + ip + ". Enter the camera username and password.";
                messageLabel.Left = 18;
                messageLabel.Top = 18;
                messageLabel.Width = 390;
                messageLabel.Height = 40;

                usernameLabel.Text = "Username:";
                usernameLabel.Left = 18;
                usernameLabel.Top = 72;
                usernameLabel.Width = 105;

                usernameBox.Left = 130;
                usernameBox.Top = 68;
                usernameBox.Width = 270;
                usernameBox.Text = username ?? "";

                passwordLabel.Text = "Password:";
                passwordLabel.Left = 18;
                passwordLabel.Top = 108;
                passwordLabel.Width = 105;

                passwordBox.Left = 130;
                passwordBox.Top = 104;
                passwordBox.Width = 270;
                passwordBox.UseSystemPasswordChar = true;
                passwordBox.Text = password ?? "";

                showPasswordBox.Text = "Show password";
                showPasswordBox.Left = 130;
                showPasswordBox.Top = 134;
                showPasswordBox.Width = 160;
                showPasswordBox.CheckedChanged += (s, e) => passwordBox.UseSystemPasswordChar = !showPasswordBox.Checked;

                okButton.Text = "Open Mosaic";
                okButton.Left = 220;
                okButton.Top = 166;
                okButton.Width = 90;
                okButton.DialogResult = DialogResult.OK;

                cancelButton.Text = "Cancel";
                cancelButton.Left = 320;
                cancelButton.Top = 166;
                cancelButton.Width = 80;
                cancelButton.DialogResult = DialogResult.Cancel;

                dialog.Controls.Add(messageLabel);
                dialog.Controls.Add(usernameLabel);
                dialog.Controls.Add(usernameBox);
                dialog.Controls.Add(passwordLabel);
                dialog.Controls.Add(passwordBox);
                dialog.Controls.Add(showPasswordBox);
                dialog.Controls.Add(okButton);
                dialog.Controls.Add(cancelButton);
                dialog.AcceptButton = okButton;
                dialog.CancelButton = cancelButton;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return false;

                username = usernameBox.Text.Trim();
                password = passwordBox.Text;
                return !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);
            }
        }

        private void ArrangeVlcWindowsAsMosaic(IList<Process> processes)
        {
            if (processes == null || processes.Count == 0)
                return;

            Rectangle area = Screen.FromControl(this).WorkingArea;
            int count = processes.Count;
            int cols = (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling(count / (double)cols);
            int tileWidth = Math.Max(320, area.Width / Math.Max(1, cols));
            int tileHeight = Math.Max(240, area.Height / Math.Max(1, rows));

            for (int i = 0; i < processes.Count; i++)
            {
                Process process = processes[i];
                IntPtr handle = WaitForMainWindow(process, 6000);
                if (handle == IntPtr.Zero)
                    continue;

                int col = i % cols;
                int row = i / cols;
                int x = area.Left + (col * tileWidth);
                int y = area.Top + (row * tileHeight);
                int width = col == cols - 1 ? area.Right - x : tileWidth;
                int height = row == rows - 1 ? area.Bottom - y : tileHeight;

                MoveWindow(handle, x, y, Math.Max(320, width), Math.Max(240, height), true);
            }
        }

        private static IntPtr WaitForMainWindow(Process process, int timeoutMs)
        {
            if (process == null)
                return IntPtr.Zero;

            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                try
                {
                    process.Refresh();
                    if (process.HasExited)
                        return IntPtr.Zero;
                    if (process.MainWindowHandle != IntPtr.Zero)
                        return process.MainWindowHandle;
                }
                catch
                {
                    return IntPtr.Zero;
                }

                Application.DoEvents();
                Thread.Sleep(100);
            }

            return IntPtr.Zero;
        }

        private static string CreateVlcMosaicConfig(IList<string> streamUrls, string username, string password)
        {
            int count = Math.Max(1, streamUrls == null ? 0 : streamUrls.Count);
            int cols = (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling(count / (double)cols);
            int tileWidth = 480;
            int tileHeight = 270;
            int mosaicWidth = Math.Max(tileWidth, cols * tileWidth);
            int mosaicHeight = Math.Max(tileHeight, rows * tileHeight);

            string tempBase = Path.Combine(Path.GetTempPath(), "UniversalScanner-VLC-Mosaic-" + Guid.NewGuid().ToString("N"));
            string backgroundPath = tempBase + ".png";
            using (Bitmap background = new Bitmap(mosaicWidth, mosaicHeight))
            using (Graphics graphics = Graphics.FromImage(background))
            {
                graphics.Clear(Color.Black);
                background.Save(backgroundPath, System.Drawing.Imaging.ImageFormat.Png);
            }

            string order = string.Join(",", Enumerable.Range(1, count).Select(i => "ch" + i));
            StringBuilder config = new StringBuilder();

            config.AppendLine("del all");

            for (int i = 0; i < count; i++)
            {
                int id = i + 1;
                string channel = "camera" + id;
                string mosaicId = "ch" + id;
                config.AppendLine("new " + channel + " broadcast enabled");
                config.AppendLine("setup " + channel + " input \"" + EscapeVlmValue(streamUrls[i]) + "\"");
                config.AppendLine("setup " + channel + " option rtsp-user=\"" + EscapeVlmValue(username) + "\"");
                config.AppendLine("setup " + channel + " option rtsp-pwd=\"" + EscapeVlmValue(password) + "\"");
                config.AppendLine("setup " + channel + " option rtsp-tcp");
                config.AppendLine("setup " + channel + " option network-caching=500");
                config.AppendLine("setup " + channel + " output #mosaic-bridge{id=" + mosaicId +
                    ",width=" + tileWidth +
                    ",height=" + tileHeight +
                    "}");
            }

            config.AppendLine("new background broadcast enabled");
            config.AppendLine("setup background input \"" + EscapeVlmValue(new Uri(backgroundPath).AbsoluteUri) + "\"");
            config.AppendLine("setup background option image-duration=-1");
            config.AppendLine("setup background option image-fps=25/1");
            config.AppendLine("setup background output #transcode{vcodec=mp4v,vb=0,fps=0,acodec=none,sfilter=mosaic{alpha=255,width=" + mosaicWidth +
                ",height=" + mosaicHeight +
                ",cols=" + cols +
                ",rows=" + rows +
                ",position=1,order=\"" + order +
                "\",keep-aspect-ratio=enabled,mosaic-align=0,keep-picture=1}}:bridge-in{offset=100}:display");

            for (int i = 0; i < count; i++)
                config.AppendLine("control camera" + (i + 1) + " play");
            config.AppendLine("control background play");

            string path = tempBase + ".vlm";
            File.WriteAllText(path, config.ToString(), Encoding.UTF8);
            return path;
        }

        private static string EscapeVlmValue(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private void exportSelectedIpMacPdfToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count == 0)
            {
                MessageBox.Show(this, "Please select one or more devices first.", "Export Selected Devices",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<PdfDeviceExportRow> selectedDevices = dataGridView1.SelectedRows
                .Cast<DataGridViewRow>()
                .Where(row => !row.IsNewRow)
                .OrderBy(row => row.Index)
                .Select(row => new PdfDeviceExportRow
                {
                    IPAddress = row.Cells[(int)Columns.IPAddress].Value?.ToString() ?? "",
                    MACAddress = row.Cells[(int)Columns.MAC].Value?.ToString() ?? "",
                    Vendor = row.Cells[(int)Columns.Vendor].Value?.ToString() ?? ""
                })
                .Where(row => !string.IsNullOrWhiteSpace(row.IPAddress) ||
                              !string.IsNullOrWhiteSpace(row.MACAddress) ||
                              !string.IsNullOrWhiteSpace(row.Vendor))
                .ToList();

            if (selectedDevices.Count == 0)
            {
                MessageBox.Show(this, "The selected rows do not contain IP, MAC, or vendor data.", "Export Selected Devices",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Export Selected Device IP/MAC Table";
                dialog.Filter = "PDF files (*.pdf)|*.pdf";
                dialog.FileName = "Selected-Device-IP-MAC.pdf";
                dialog.OverwritePrompt = true;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    SimplePdfTableExporter.ExportIpMacTable(dialog.FileName, selectedDevices);
                    MessageBox.Show(this, "Selected device IP/MAC table exported successfully.", "Export Selected Devices",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Failed to export PDF:\r\n\r\n" + ex.Message, "Export Selected Devices",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        private void initializeDeviceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count == 0)
                return;

            ApplyAiBrandOverridesToSelectedRowsForAction();

            DataGridViewRow row = dataGridView1.SelectedRows[0];
            if (IsDahuaSwitchRow(GetDataRowFromGridRow(row)))
                return;

            string vendor = row.Cells[(int)Columns.Vendor].Value?.ToString() ?? "";
            string ip = row.Cells[(int)Columns.IPAddress].Value?.ToString() ?? "";
            string type = row.Cells[(int)Columns.Type].Value?.ToString() ?? "";
            string activation = row.Cells[(int)Columns.Activation].Value?.ToString() ?? "Unknown";

            if (string.IsNullOrWhiteSpace(ip))
                return;

            using (DeviceInitializationForm form = new DeviceInitializationForm(vendor, ip, type, activation))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    row.Cells[(int)Columns.Activation].Value = "Active";

                    if (!string.IsNullOrWhiteSpace(form.UpdatedModel) &&
                        form.UpdatedModel != "Unknown")
                        row.Cells[(int)Columns.Type].Value = form.UpdatedModel;

                    if (!string.IsNullOrWhiteSpace(form.UpdatedUniqueId))
                        row.Cells[(int)Columns.UniqueId].Value = NormalizeUniqueIdForDisplay(form.UpdatedUniqueId);

                    if (IsHoneywellText(vendor) || IsHoneywellText(type))
                        DeviceSDK.Honeywell.HoneywellActivationCache.MarkActive(ip);

                    dataGridView1.Refresh();
                }
                else if (form.ActivationAttempted)
                {
                    string status = form.ActivationStatus;
                    if (!string.IsNullOrWhiteSpace(status))
                        row.Cells[(int)Columns.Activation].Value = status;

                    dataGridView1.Refresh();
                }
            }
        }

        private async void changeIPAddressToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count == 0)
                return;

            ApplyAiBrandOverridesToSelectedRowsForAction();

            DataGridViewRow row = dataGridView1.SelectedRows[0];
            if (IsDahuaSwitchRow(GetDataRowFromGridRow(row)))
                return;

            string vendor = row.Cells[(int)Columns.Vendor].Value?.ToString() ?? "";
            string protocol = row.Cells[(int)Columns.Protocol].Value?.ToString() ?? "";
            string ip = row.Cells[(int)Columns.IPAddress].Value?.ToString() ?? "";
            string type = row.Cells[(int)Columns.Type].Value?.ToString() ?? "";
            string activation = row.Cells[(int)Columns.Activation].Value?.ToString() ?? "Unknown";
            DataRow dataRow = GetDataRowFromGridRow(row);

            if (string.IsNullOrWhiteSpace(ip))
                return;

            if (IsHoneywellRow(dataRow) && !IsHoneywellText(vendor) && !IsHoneywellText(type))
                vendor = "Honeywell";

            using (ChangeIpForm form = new ChangeIpForm(vendor, ip, type, activation))
            {
                DialogResult dr = form.ShowDialog(this);
                if (dr == DialogResult.OK)
                {
                    string oldIp = ip;
                    string newIp = form.NewIPAddress;

                    if (string.IsNullOrWhiteSpace(newIp))
                        newIp = oldIp;

                    if (IsBoschText(vendor) || IsBoschText(protocol) || IsBoschText(type))
                        await CompleteBoschIpChangeRowAsync(row, oldIp, newIp);
                    else
                        await RefreshChangedHikvisionIpRowAsync(row, oldIp, newIp, form.SubnetMask);
                }
                else if (dr == DialogResult.Retry)
                {
                    string acrossSubnetMask = ResolveAcrossSubnetMask(ip);
                    string acrossSubnetAdapter = ResolveAcrossSubnetAdapter(dataRow, ip, acrossSubnetMask);
                    using (ChangeIpAcrossSubnetForm acrossSubnetForm = new ChangeIpAcrossSubnetForm(ip, vendor, type, acrossSubnetAdapter, acrossSubnetMask))
                    {
                        if (acrossSubnetForm.ShowDialog(this) == DialogResult.OK)
                        {
                            await RefreshChangedHikvisionIpRowAsync(row, ip, ip);
                        }
                    }
                }
            }
        }

        private async void changeIPAcrossSubnetToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count != 1)
                return;

            ApplyAiBrandOverridesToSelectedRowsForAction();

            DataGridViewRow row = dataGridView1.SelectedRows[0];
            if (IsDahuaSwitchRow(GetDataRowFromGridRow(row)))
                return;

            string vendor = row.Cells[(int)Columns.Vendor].Value?.ToString() ?? "";
            string ip = row.Cells[(int)Columns.IPAddress].Value?.ToString() ?? "";
            string type = row.Cells[(int)Columns.Type].Value?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(ip))
                return;

            DataRow dataRow = GetDataRowFromGridRow(row);
            string acrossSubnetMask = ResolveAcrossSubnetMask(ip);
            string acrossSubnetAdapter = ResolveAcrossSubnetAdapter(dataRow, ip, acrossSubnetMask);
            using (ChangeIpAcrossSubnetForm acrossSubnetForm = new ChangeIpAcrossSubnetForm(ip, vendor, type, acrossSubnetAdapter, acrossSubnetMask))
            {
                if (acrossSubnetForm.ShowDialog(this) == DialogResult.OK)
                {
                    await RefreshChangedHikvisionIpRowAsync(row, ip, ip);
                }
            }
        }

        private string ResolveAcrossSubnetMask(string ip)
        {
            string subnetMask = RecentIpNetworkRegistry.GetSubnetMask(ip);
            if (string.IsNullOrWhiteSpace(subnetMask))
                subnetMask = NetworkTools.GetKnownSubnetMaskForTarget(ip);
            if (string.IsNullOrWhiteSpace(subnetMask))
                subnetMask = ip.StartsWith("169.254.", StringComparison.Ordinal) ? "255.255.0.0" : "255.255.255.0";

            return subnetMask;
        }

        private string ResolveAcrossSubnetAdapter(DataRow dataRow, string ip, string subnetMask)
        {
            bool isAxis = IsAxisRow(dataRow);
            string adapterAlias = isAxis ? AxisLinkLocalRegistry.GetAdapterAlias(ip) : "";

            if (string.IsNullOrWhiteSpace(adapterAlias))
                adapterAlias = NetworkTools.GetPreferredPhysicalAdapterName();

            if (string.IsNullOrWhiteSpace(adapterAlias))
                adapterAlias = NetworkTools.GetBestAdapterNameForTarget(ip, subnetMask);

            return adapterAlias ?? "";
        }

        private async Task CompleteBoschIpChangeRowAsync(DataGridViewRow row, string oldIp, string newIp)
        {
            if (row == null || string.IsNullOrWhiteSpace(newIp))
                return;

            DataRow keepRow = GetDataRowFromGridRow(row);
            string targetMac = NormalizeMac(keepRow?[(int)Columns.MAC]?.ToString());

            RegisterIpChangeGuard(oldIp);
            RegisterRecentChangedIp(newIp);
            NetworkTools.FlushArpEntry(oldIp);
            RemoveDuplicateRowsForIpChange(oldIp, newIp, keepRow);

            row.Cells[(int)Columns.Vendor].Value = "Bosch";
            row.Cells[(int)Columns.Protocol].Value = "Bosch";
            row.Cells[(int)Columns.IPAddress].Value = newIp;
            row.Cells[(int)Columns.Ping].Value = "No reply";
            row.Cells[(int)Columns.Activation].Value = "Active";
            if (!string.IsNullOrWhiteSpace(targetMac))
                row.Cells[(int)Columns.MAC].Value = targetMac;

            if (keepRow != null)
                ApplyBoschIdentity(keepRow);

            dataGridView1.Refresh();
            await ShowStatusAsync("Ready", 1200);
        }

        private async Task RefreshChangedHikvisionIpRowAsync(DataGridViewRow row, string oldIp, string newIp, string newSubnetMask = null)
        {
            if (row == null)
                return;

            if (string.IsNullOrWhiteSpace(newIp))
                return;

            DataRow keepRow = GetDataRowFromGridRow(row);
            string targetMac = NormalizeMac(keepRow?[(int)Columns.MAC]?.ToString());
            bool isAxisChange = IsAxisRow(keepRow);
            bool confirmNewIpBeforeMovingRow =
                IsHoneywellRow(keepRow) &&
                !IsHoneywellHc30Or35Row(keepRow);

            if (!confirmNewIpBeforeMovingRow)
            {
                RegisterIpChangeGuard(oldIp);
                RegisterRecentChangedIp(newIp);
            }

            // Flush the Windows ARP cache entry for the old IP so that packets sent to the old
            // address stop reaching the camera immediately.  Without this the stale ARP entry can
            // persist for ~2 minutes and make the camera appear reachable on its old IP even after
            // the stored configuration has been updated.
            NetworkTools.FlushArpEntry(oldIp);

            if (confirmNewIpBeforeMovingRow)
            {
                row.Cells[(int)Columns.IPAddress].Value = oldIp;
                row.Cells[(int)Columns.Ping].Value = "Waiting for new IP...";
                row.Cells[(int)Columns.Activation].Value = "Checking...";
            }
            else
            {
                // SADP-like behavior:
                // The SDK already reported success, so update the visible device immediately.
                RemoveDuplicateRowsForIpChange(oldIp, newIp, keepRow);

                row.Cells[(int)Columns.IPAddress].Value = newIp;
                if (keepRow != null && IsBoschRow(keepRow))
                    ApplyBoschIdentity(keepRow);
                row.Cells[(int)Columns.Ping].Value = "Refreshing...";
                row.Cells[(int)Columns.Activation].Value = "Checking...";
                row.Cells[(int)Columns.MAC].Value = "";
                row.Cells[(int)Columns.MACVendor].Value = "";
            }
            dataGridView1.Refresh();

            bool axisLinkLocalRefreshAvailable = isAxisChange && AxisLinkLocalRegistry.HasRoute(newIp);
            if (!string.IsNullOrWhiteSpace(newSubnetMask))
                RecentIpNetworkRegistry.SetSubnetMask(newIp, newSubnetMask);

            string reachabilityWarning = axisLinkLocalRefreshAvailable ? "" : NetworkTools.GetReachabilityWarning(newIp);
            if (!string.IsNullOrWhiteSpace(reachabilityWarning))
            {
                row.Cells[(int)Columns.Ping].Value = "Unreachable subnet";
                row.Cells[(int)Columns.Activation].Value = "Unknown";
                await ShowStatusAsync("IP changed to " + newIp + ", but this PC is not on that subnet.");
            }
            else
            {
                await ShowStatusAsync(confirmNewIpBeforeMovingRow
                    ? "Waiting for Honeywell legacy camera to answer at " + newIp + "..."
                    : "IP changed to " + newIp + ". Refreshing device information...");
            }

            Guid capturedSessionId = this.currentScanSessionId;

            // Let the camera finish applying settings, but do not mark it as offline while it reboots.
            _ = RefreshChangedHikvisionIpRowInBackgroundAsync(GetDataRowFromGridRow(row)?[(int)Columns.UniqueId]?.ToString() ?? "", oldIp, newIp, confirmNewIpBeforeMovingRow, capturedSessionId, targetMac, isAxisChange, newSubnetMask);
        }

        private async Task RefreshChangedHikvisionIpRowInBackgroundAsync(string targetUniqueId, string oldIp, string newIp, bool confirmNewIpBeforeMovingRow = false, Guid? capturedSessionId = null, string targetMac = null, bool isAxisChange = false, string newSubnetMask = null)
        {
            TemporarySubnetRefreshContext axisTempContext = null;
            try
            {
            axisTempContext = await EnsureAxisTemporarySubnetForRefreshAsync(isAxisChange, oldIp, newIp, newSubnetMask);

            // Hikvision cameras typically take 30–90 s to finish rebooting after an IP change.
            // Wait before starting to poll so we don't exhaust all attempts during the reboot
            // blackout period (NIC is down and every ping times out).
            int initialRebootWaitSeconds = isAxisChange && AxisLinkLocalRegistry.HasRoute(newIp) ? 5 : 20;
            const int attempts = 25;

            string currentIp = confirmNewIpBeforeMovingRow ? oldIp : newIp;
            await ShowStatusAsync("Waiting for device to reboot (" + initialRebootWaitSeconds + " s)...");
            await Task.Delay(initialRebootWaitSeconds * 1000);

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    await ShowStatusAsync("Refreshing " + newIp + " (" + attempt + "/" + attempts + ")...");

                    bool axisLinkLocalReachable = isAxisChange && AxisLinkLocalRegistry.HasRoute(newIp);
                    bool ipv4Reachable = await NetworkTools.IsDeviceReachableAsync(newIp, 1200);
                    bool useAxisLinkLocalFallback = axisLinkLocalReachable && !ipv4Reachable;
                    bool reachable = ipv4Reachable || useAxisLinkLocalFallback;

                    if (reachable)
                    {
                        if (confirmNewIpBeforeMovingRow)
                        {
                            RegisterIpChangeGuard(oldIp);
                            RegisterRecentChangedIp(newIp);
                            DeviceSDK.Honeywell.HoneywellActivationCache.Remove(oldIp);
                            DeviceSDK.Honeywell.HoneywellActivationCache.MarkActive(newIp);
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.IPAddress, newIp, capturedSessionId, targetMac); currentIp = newIp;
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.MAC, "", capturedSessionId, targetMac);
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.MACVendor, "", capturedSessionId, targetMac);
                        }

                        VendorProbeResult detected;
                        string mac;
                        string macVendor;

                        if (useAxisLinkLocalFallback)
                        {
                            DataRow existingAxisRow = FindDeviceRow(currentIp, "", targetUniqueId);
                            detected = new VendorProbeResult
                            {
                                Vendor = "Axis",
                                Protocol = "Axis",
                                Type = FirstNonEmpty(existingAxisRow?[(int)Columns.Type]?.ToString(), "Axis Device")
                            };
                            mac = FirstNonEmpty(targetMac, existingAxisRow?[(int)Columns.MAC]?.ToString());
                            macVendor = FirstNonEmpty(existingAxisRow?[(int)Columns.MACVendor]?.ToString(), "Axis Communications");
                        }
                        else
                        {
                            var sw_onvif = System.Diagnostics.Stopwatch.StartNew(); detected =
                                await SubnetVendorProbe.DetectAsync(newIp); sw_onvif.Stop(); StartupDiagnostics.Increment("OnvifProbes"); StartupDiagnostics.AddTimer("OnvifProbes", sw_onvif.ElapsedMilliseconds); StartupDiagnostics.LogSlowOperation("ONVIF", newIp, "Unknown", sw_onvif.ElapsedMilliseconds, detected != null && detected.Vendor != "Unknown");

                            var sw_mac = System.Diagnostics.Stopwatch.StartNew(); mac = await NetworkTools.GetMacAsync(newIp); sw_mac.Stop(); StartupDiagnostics.Increment("MacProbes"); StartupDiagnostics.AddTimer("MacProbes", sw_mac.ElapsedMilliseconds); StartupDiagnostics.LogSlowOperation("MAC", newIp, "Unknown", sw_mac.ElapsedMilliseconds, !string.IsNullOrEmpty(mac));
                            macVendor = NetworkTools.GetVendor(mac);
                        }

                        bool detectedHikvision =
                            IsHikvisionText(detected.Protocol) ||
                            IsHikvisionText(detected.Vendor) ||
                            IsHikvisionText(detected.Type) ||
                            IsHikvisionCompatibleRow(FindDeviceRow(currentIp, "", targetUniqueId));

                        bool detectedHoneywell =
                            IsHoneywellText(detected.Protocol) ||
                            IsHoneywellText(detected.Vendor) ||
                            IsHoneywellText(detected.Type) ||
                            IsHoneywellRow(FindDeviceRow(currentIp, "", targetUniqueId)) ||
                            IsHoneywellText(macVendor);

                        bool detectedUniview =
                            !detectedHoneywell &&
                            !detectedHikvision &&
                            (IsUniviewText(detected.Protocol) ||
                             IsUniviewText(detected.Vendor) ||
                             IsUniviewText(detected.Type) ||
                             IsUniviewRow(FindDeviceRow(currentIp, "", targetUniqueId)));

                        bool detectedDahua =
                            !detectedHoneywell &&
                            !detectedHikvision &&
                            !detectedUniview &&
                            (IsDahuaText(detected.Protocol) ||
                             IsDahuaText(detected.Vendor) ||
                             IsDahuaText(detected.Type) ||
                             IsDahuaRow(FindDeviceRow(currentIp, "", targetUniqueId)) ||
                             IsDahuaText(macVendor));

                        bool detectedSunell =
                            !detectedHoneywell &&
                            !detectedHikvision &&
                            !detectedUniview &&
                            !detectedDahua &&
                            (IsSunellText(detected.Protocol) ||
                             IsSunellText(detected.Vendor) ||
                             IsSunellText(detected.Type) ||
                             IsSunellRow(FindDeviceRow(currentIp, "", targetUniqueId)) ||
                             IsSunellOuiVendor(macVendor));

                        bool detectedTiandy =
                            !detectedHoneywell &&
                            !detectedHikvision &&
                            !detectedUniview &&
                            !detectedDahua &&
                            !detectedSunell &&
                            (IsTiandyText(detected.Protocol) ||
                             IsTiandyText(detected.Vendor) ||
                             IsTiandyText(detected.Type) ||
                             IsTiandyRow(FindDeviceRow(currentIp, "", targetUniqueId)) ||
                             IsTiandyText(macVendor));

                        bool detectedBosch =
                            !detectedHoneywell &&
                            !detectedHikvision &&
                            !detectedUniview &&
                            !detectedDahua &&
                            !detectedSunell &&
                            !detectedTiandy &&
                            (IsBoschText(detected.Protocol) ||
                             IsBoschText(detected.Vendor) ||
                             IsBoschText(detected.Type));

                        bool detectedAxis =
                            !detectedHoneywell &&
                            !detectedHikvision &&
                            !detectedUniview &&
                            !detectedDahua &&
                            !detectedSunell &&
                            !detectedTiandy &&
                            !detectedBosch &&
                            (IsAxisText(detected.Protocol) ||
                             IsAxisText(detected.Vendor) ||
                             IsAxisText(detected.Type) ||
                             IsAxisRow(FindDeviceRow(currentIp, "", targetUniqueId)) ||
                             IsAxisOuiVendor(macVendor));

                        string activation;
                        if (detectedHoneywell)
                        {
                            activation = NormalizeHoneywellActivationStatus(
                                FindDeviceRow(currentIp, "", targetUniqueId),
                                await DeviceSDK.Honeywell.HoneywellActivationStatusProbe.GetStatusAsync(newIp));
                        }
                        else if (detectedHikvision)
                        {
                            activation = await HikvisionActivationStatusProbe.GetStatusAsync(newIp);
                        }
                        else if (detectedUniview)
                        {
                            activation = await DeviceSDK.Uniview.UniviewActivationStatusProbe.GetStatusAsync(newIp);
                        }
                        else if (detectedDahua)
                        {
                            activation = IsDahuaText(macVendor) || IsDahuaRow(FindDeviceRow(currentIp, "", targetUniqueId))
                                ? "Active"
                                : await Task.Run(() => DeviceSDK.Dahua.DahuaActivationStatusProbe.GetStatus(newIp));
                        }
                        else if (detectedSunell)
                        {
                            activation = await Task.Run(() => DeviceSDK.Sunell.SunellActivationStatusProbe.GetStatus(newIp));
                        }
                        else if (detectedTiandy)
                        {
                            activation = await Task.Run(() => DeviceSDK.Tiandy.TiandyActivationStatusProbe.GetStatus(newIp));
                        }
                        else if (detectedBosch)
                        {
                            activation = await Task.Run(() => DeviceSDK.Bosch.BoschActivationStatusProbe.GetStatusAsync(newIp));
                        }
                        else if (detectedAxis)
                        {
                            activation = await DeviceSDK.Axis.AxisActivationStatusProbe.GetStatusAsync(newIp);
                        }
                        else
                        {
                            activation = "N/A";
                        }

                        if (useAxisLinkLocalFallback)
                        {
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Ping, "IPv6 refresh", capturedSessionId, targetMac);
                        }
                        else
                        {
                            using (Ping ping = new Ping())
                            {
                                var sw_ping = System.Diagnostics.Stopwatch.StartNew(); PingReply reply = await ping.SendPingAsync(newIp, 1000); sw_ping.Stop(); StartupDiagnostics.Increment("PingProbes"); StartupDiagnostics.AddTimer("PingProbes", sw_ping.ElapsedMilliseconds); StartupDiagnostics.LogSlowOperation("Ping", newIp, "Unknown", sw_ping.ElapsedMilliseconds, reply.Status == System.Net.NetworkInformation.IPStatus.Success);

                                if (reply.Status == IPStatus.Success)
                                    UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Ping, reply.RoundtripTime + " ms", capturedSessionId, targetMac);
                                else
                                    UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Ping, "Refreshing...", capturedSessionId, targetMac);
                            }
                        }

                        if (detectedDahua)
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Vendor, "Dahua", capturedSessionId, targetMac);
                        else if (detectedSunell)
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Vendor, "Sunell", capturedSessionId, targetMac);
                        else if (detectedTiandy)
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Vendor, "Tiandy", capturedSessionId, targetMac);
                        else if (detectedAxis)
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Vendor, "Axis", capturedSessionId, targetMac);
                        else if (!string.IsNullOrWhiteSpace(detected.Vendor) &&
                                 detected.Vendor != "Unknown")
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Vendor, detected.Vendor, capturedSessionId, targetMac);

                        if (detectedDahua)
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Protocol, "Dahua", capturedSessionId, targetMac);
                        else if (detectedSunell)
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Protocol, "Sunell", capturedSessionId, targetMac);
                        else if (detectedTiandy)
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Protocol, "Ping", capturedSessionId, targetMac);
                        else if (detectedAxis)
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Protocol, "Axis", capturedSessionId, targetMac);
                        else if (!string.IsNullOrWhiteSpace(detected.Protocol) &&
                                 detected.Protocol != "Ping")
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Protocol, detected.Protocol, capturedSessionId, targetMac);

                        if (detectedDahua)
                        {
                            string model = DeviceSDK.Dahua.DahuaDiscoveryState.GetDeviceModel(newIp, mac, FindDeviceRow(currentIp, "", targetUniqueId)?[(int)Columns.UniqueId]?.ToString());
                            if (!string.IsNullOrWhiteSpace(model))
                                UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Type, model, capturedSessionId, targetMac);
                        }
                        else if (detectedSunell)
                        {
                            string model = DeviceSDK.Sunell.SunellActivationStatusProbe.GetModel(newIp);
                            if (!string.IsNullOrWhiteSpace(model))
                                UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Type, model, capturedSessionId, targetMac);
                            else if (string.IsNullOrWhiteSpace(FindDeviceRow(currentIp, "", targetUniqueId)?[(int)Columns.Type]?.ToString()) ||
                                     FindDeviceRow(currentIp, "", targetUniqueId)?[(int)Columns.Type]?.ToString() == "Unknown" ||
                                     IsGenericNetworkVideoType(FindDeviceRow(currentIp, "", targetUniqueId)?[(int)Columns.Type]?.ToString()) ||
                                     IsGenericWsDiscoveryText(FindDeviceRow(currentIp, "", targetUniqueId)?[(int)Columns.Type]?.ToString()))
                                UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Type, "Network Video Transmitter", capturedSessionId, targetMac);
                        }
                        else if (detectedTiandy)
                        {
                            string model = await DeviceSDK.Tiandy.TiandyInfoProbe.GetModelAsync(newIp);
                            if (!string.IsNullOrWhiteSpace(model))
                                UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Type, model, capturedSessionId, targetMac);
                            string tiandyUniqueId = FirstNonEmpty(DeviceSDK.Tiandy.TiandyInfoProbe.LastUniqueId, mac);
                            if (!string.IsNullOrWhiteSpace(tiandyUniqueId))
                                UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.UniqueId, NormalizeUniqueIdForDisplay(tiandyUniqueId), capturedSessionId, targetMac);
                        }
                        else if (!string.IsNullOrWhiteSpace(detected.Type) &&
                                 detected.Type != "Unknown")
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Type, detected.Type, capturedSessionId, targetMac);

                        UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Activation, string.IsNullOrWhiteSpace(activation) ? "Unknown" : activation, capturedSessionId, targetMac);

                        if (!string.IsNullOrWhiteSpace(mac))
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.MAC, mac, capturedSessionId, targetMac);

                        if (!string.IsNullOrWhiteSpace(macVendor))
                            UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.MACVendor, macVendor, capturedSessionId, targetMac);

                        QueueOnvifProfileLookup(newIp, FindDeviceRow(currentIp, "", targetUniqueId)?[(int)Columns.UniqueId]?.ToString());

                        RemoveDuplicateRowsForIpChange(oldIp, newIp, FindDeviceRow(currentIp, "", targetUniqueId));

                        dataGridView1.Refresh();
                        await ShowStatusAsync("Device refreshed at " + newIp, 2500);
                        return;
                    }
                }
                catch
                {
                }

                // Do not overwrite the row with "No reply" during reboot.
                bool newIpHasLocalSubnet = isAxisChange && !string.IsNullOrWhiteSpace(newSubnetMask)
                    ? NetworkTools.IsIpOnAnyLocalSubnet(newIp, newSubnetMask) || axisTempContext != null
                    : NetworkTools.IsIpOnAnyLocalSubnet(newIp);

                if (newIpHasLocalSubnet)
                {
                    UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Ping, "Refreshing...", capturedSessionId, targetMac);
                    UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Activation, "Checking...", capturedSessionId, targetMac);
                }
                else
                {
                    UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Ping, "Unreachable subnet", capturedSessionId, targetMac);
                    UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Activation, "Unknown", capturedSessionId, targetMac);
                }
                dataGridView1.Refresh();

                if (attempt < attempts)
                    await Task.Delay(4000);
            }

            // Keep SADP-like visible state. User can press Scan later if the device needs more time.
            if (confirmNewIpBeforeMovingRow)
            {
                bool oldIpStillReachable = await NetworkTools.IsDeviceReachableAsync(oldIp, 1200);
                if (oldIpStillReachable)
                {
                    UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.IPAddress, oldIp, capturedSessionId, targetMac);
                    UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Ping, "Still at old IP", capturedSessionId, targetMac);
                    UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Activation, "Unknown", capturedSessionId, targetMac);
                    await ShowStatusAsync("Honeywell legacy camera still answers at " + oldIp + "; new IP " + newIp + " was not confirmed.", 5000);
                    dataGridView1.Refresh();
                    return;
                }
            }

            bool finalNewIpHasLocalSubnet = isAxisChange && !string.IsNullOrWhiteSpace(newSubnetMask)
                ? NetworkTools.IsIpOnAnyLocalSubnet(newIp, newSubnetMask) || axisTempContext != null
                : NetworkTools.IsIpOnAnyLocalSubnet(newIp);

            if (finalNewIpHasLocalSubnet)
            {
                UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Ping, "Refresh needed", capturedSessionId, targetMac);
                UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Activation, "Unknown", capturedSessionId, targetMac);
                await ShowStatusAsync("IP changed to " + newIp + ". Device did not answer ping/web/SDK yet.", 4000);
            }
            else
            {
                UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Ping, "Unreachable subnet", capturedSessionId, targetMac);
                UpdateDataRowField(currentIp, targetUniqueId, (int)Columns.Activation, "Unknown", capturedSessionId, targetMac);
                await ShowStatusAsync("IP changed to " + newIp + ". Add a PC IP/route for that subnet, then rescan.", 5000);
            }
            dataGridView1.Refresh();
            }
            finally
            {
                if (axisTempContext != null)
                    await CleanupAxisTemporarySubnetForRefreshAsync(axisTempContext);
            }
        }

        private async Task<TemporarySubnetRefreshContext> EnsureAxisTemporarySubnetForRefreshAsync(bool isAxisChange, string oldIp, string newIp, string newSubnetMask)
        {
            if (!isAxisChange ||
                string.IsNullOrWhiteSpace(newIp) ||
                string.IsNullOrWhiteSpace(newSubnetMask) ||
                NetworkTools.IsIpOnAnyLocalSubnet(newIp, newSubnetMask))
                return null;

            if (!TemporarySubnetService.Instance.IsAdministrator())
            {
                await ShowStatusAsync("Axis IP changed to " + newIp + ". Run as administrator or add a PC IP in " + newSubnetMask + " to refresh it after reboot.", 5000);
                return null;
            }

            IPAddress cameraIp;
            IPAddress mask;
            if (!IPAddress.TryParse(newIp, out cameraIp) ||
                !IPAddress.TryParse(newSubnetMask, out mask))
                return null;

            string adapterAlias = FirstNonEmpty(
                AxisLinkLocalRegistry.GetAdapterAlias(newIp),
                AxisLinkLocalRegistry.GetAdapterAlias(oldIp),
                NetworkTools.GetPreferredPhysicalAdapterName(),
                NetworkTools.GetBestAdapterNameForTarget(newIp, newSubnetMask, oldIp));
            if (string.IsNullOrWhiteSpace(adapterAlias))
                return null;

            IPAddress temporaryIp = TemporarySubnetService.Instance.FindAvailableTemporaryIp(cameraIp, mask);
            if (temporaryIp == null)
                return null;

            await ShowStatusAsync("Adding temporary Axis subnet " + temporaryIp + " / " + newSubnetMask + " for refresh...");
            TemporarySubnetService.AddIpResult addResult = await TemporarySubnetService.Instance.AddTemporaryIpAsync(adapterAlias, temporaryIp, mask, cameraIp);
            if (addResult == null || !addResult.Success)
            {
                await ShowStatusAsync("Axis IP changed to " + newIp + ", but temporary subnet could not be added.", 5000);
                return null;
            }

            await TemporarySubnetService.Instance.ValidateAndConfigureRouteAsync(adapterAlias, temporaryIp, cameraIp);
            return new TemporarySubnetRefreshContext
            {
                AdapterAlias = adapterAlias,
                TemporaryIp = temporaryIp,
                CameraIp = cameraIp
            };
        }

        private async Task CleanupAxisTemporarySubnetForRefreshAsync(TemporarySubnetRefreshContext context)
        {
            try
            {
                await TemporarySubnetService.Instance.RemoveTemporaryIpAsync(context.AdapterAlias, context.TemporaryIp, context.CameraIp, null);
                await ShowStatusAsync("Temporary Axis subnet removed.", 1500);
            }
            catch
            {
            }
        }

        private sealed class TemporarySubnetRefreshContext
        {
            public string AdapterAlias { get; set; }
            public IPAddress TemporaryIp { get; set; }
            public IPAddress CameraIp { get; set; }
        }

        private void RemoveDuplicateRowsForIpChange(string oldIp, string newIp, DataRow keepRow)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => RemoveDuplicateRowsForIpChange(oldIp, newIp, keepRow)));
                return;
            }

            if (foundDeviceList == null)
                return;

            string keepMac = "";
            string keepUniqueId = "";
            string displayKeepUniqueId = "";

            if (keepRow != null && keepRow.RowState != DataRowState.Deleted)
            {
                keepMac = NormalizeMac(keepRow[(int)Columns.MAC]?.ToString());
                keepUniqueId = keepRow[(int)Columns.UniqueId]?.ToString()?.Trim() ?? "";
                displayKeepUniqueId = NormalizeUniqueIdForDisplay(keepUniqueId);
            }

            List<DataRow> rowsToRemove = new List<DataRow>();

            foreach (DataRow dataRow in foundDeviceList.Rows)
            {
                if (dataRow == null || dataRow.RowState == DataRowState.Deleted)
                    continue;

                if (keepRow != null && object.ReferenceEquals(dataRow, keepRow))
                    continue;

                string rowIp = dataRow[(int)Columns.IPAddress]?.ToString()?.Trim() ?? "";
                string rowMac = NormalizeMac(dataRow[(int)Columns.MAC]?.ToString());
                string rowUniqueId = dataRow[(int)Columns.UniqueId]?.ToString()?.Trim() ?? "";
                string displayRowUniqueId = NormalizeUniqueIdForDisplay(rowUniqueId);

                bool sameIp =
                    (!string.IsNullOrWhiteSpace(oldIp) && rowIp.Equals(oldIp, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(newIp) && rowIp.Equals(newIp, StringComparison.OrdinalIgnoreCase));
                if (sameIp && ShouldKeepSeparateDahuaFactoryDefaultRow(keepRow, dataRow, rowIp))
                    sameIp = false;

                bool sameMac =
                    !string.IsNullOrWhiteSpace(keepMac) &&
                    !string.IsNullOrWhiteSpace(rowMac) &&
                    rowMac == keepMac;

                bool sameUniqueId =
                    !string.IsNullOrWhiteSpace(keepUniqueId) &&
                    !string.IsNullOrWhiteSpace(rowUniqueId) &&
                    (rowUniqueId.Equals(keepUniqueId, StringComparison.OrdinalIgnoreCase) ||
                     displayRowUniqueId.Equals(displayKeepUniqueId, StringComparison.OrdinalIgnoreCase));

                bool sameUniqueIdMac =
                    IsMacAddressText(keepUniqueId) &&
                    IsMacAddressText(rowUniqueId) &&
                    NormalizeMac(rowUniqueId) == NormalizeMac(keepUniqueId);

                if (sameIp || sameMac || sameUniqueId || sameUniqueIdMac)
                    rowsToRemove.Add(dataRow);
            }

            foreach (DataRow dataRow in rowsToRemove)
                foundDeviceList.Rows.Remove(dataRow);

            dataGridView1.Refresh();
        }

        private DataRow GetDataRowFromGridRow(DataGridViewRow gridRow)
        {
            if (gridRow == null)
                return null;

            DataRowView rowView = gridRow.DataBoundItem as DataRowView;

            if (rowView == null)
                return null;

            return rowView.Row;
        }

        private async void pingSelectedDeviceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenPingMonitor(1, 1000, 1000);
            await Task.CompletedTask;
        }

        private string GetSelectedIPAddress()
        {
            if (dataGridView1.SelectedRows.Count == 0)
                return "";

            return dataGridView1.SelectedRows[0]
                .Cells[(int)Columns.IPAddress]
                .Value?
                .ToString() ?? "";
        }

        private void OpenPingMonitor(int count, int timeoutMs, int intervalMs)
        {
            string ip = GetSelectedIPAddress();

            if (string.IsNullOrWhiteSpace(ip))
                return;

            DataGridViewRow selectedRow = dataGridView1.SelectedRows[0];

            PingMonitorForm monitor = new PingMonitorForm(
                ip,
                count,
                timeoutMs,
                intervalMs,
                async (text, delay) => await ShowStatusAsync(text, delay),
                result =>
                {
                    try
                    {
                        if (!IsDisposed && selectedRow != null && selectedRow.Index >= 0)
                            selectedRow.Cells[(int)Columns.Ping].Value = result;
                    }
                    catch
                    {
                    }
                });

            activePingMonitor = monitor;
            monitor.FormClosed += (s, e) =>
            {
                if (activePingMonitor == monitor)
                    activePingMonitor = null;
            };

            monitor.Show(this);
        }

        private void OpenCustomPingMonitor()
        {
            string ip = GetSelectedIPAddress();

            if (string.IsNullOrWhiteSpace(ip))
                return;

            using (CustomPingDialog dialog = new CustomPingDialog(ip))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    OpenPingMonitor(dialog.PingCount, dialog.TimeoutMs, dialog.IntervalMs);
                }
            }
        }

        private async Task PingSelectedDeviceAsync(int count)
        {
            if (dataGridView1.SelectedRows.Count == 0)
                return;

            DataGridViewRow selectedRow = dataGridView1.SelectedRows[0];

            string ip = selectedRow
                .Cells[(int)Columns.IPAddress]
                .Value?
                .ToString();

            if (string.IsNullOrWhiteSpace(ip))
                return;

            CancelCurrentPing();
            CancellationTokenSource currentPingSource = new CancellationTokenSource();
            pingCancellationTokenSource = currentPingSource;
            CancellationToken token = currentPingSource.Token;

            bool continuous = count <= 0;
            int current = 0;
            string lastResult = "";

            try
            {
                using (Ping ping = new Ping())
                {
                    while (!token.IsCancellationRequested && (continuous || current < count))
                    {
                        current++;

                        string progressText = continuous
                            ? "Pinging " + ip + " (" + current + ")..."
                            : "Pinging " + ip + " (" + current + "/" + count + ")...";

                        await ShowStatusAsync(progressText);

                        var sw_ping = System.Diagnostics.Stopwatch.StartNew(); PingReply reply = await ping.SendPingAsync(ip, 1000); sw_ping.Stop(); StartupDiagnostics.Increment("PingProbes"); StartupDiagnostics.AddTimer("PingProbes", sw_ping.ElapsedMilliseconds); StartupDiagnostics.LogSlowOperation("Ping", ip, "Unknown", sw_ping.ElapsedMilliseconds, reply.Status == System.Net.NetworkInformation.IPStatus.Success);

                        lastResult = reply.Status == IPStatus.Success
                            ? reply.RoundtripTime + " ms"
                            : reply.Status.ToString();

                        selectedRow.Cells[(int)Columns.Ping].Value = lastResult;

                        await ShowStatusAsync("Reply from " + ip + ": " + lastResult);

                        if (!continuous && current >= count)
                            break;

                        await Task.Delay(1000, token);
                    }
                }

                if (token.IsCancellationRequested)
                    await ShowStatusAsync("Ping stopped", 1500);
                else
                    await ShowStatusAsync("Ping completed (" + lastResult + ")", 2000);
            }
            catch (TaskCanceledException)
            {
                await ShowStatusAsync("Ping stopped", 1500);
            }
            catch (Exception ex)
            {
                selectedRow.Cells[(int)Columns.Ping].Value = "Failed";
                await ShowStatusAsync("Ping failed: " + ex.Message, 3000);
            }
            finally
            {
                if (pingCancellationTokenSource == currentPingSource)
                    pingCancellationTokenSource = null;

                currentPingSource.Dispose();
            }
        }

        private async Task PingSelectedRowsOnceAsync()
        {
            if (dataGridView1.SelectedRows.Count == 0)
                return;

            var rows = new List<DataGridViewRow>();
            var ips = new List<string>();

            foreach (DataGridViewRow row in dataGridView1.SelectedRows)
            {
                string ip = row.Cells[(int)Columns.IPAddress].Value?.ToString()?.Trim();
                IPAddress parsed;
                if (string.IsNullOrWhiteSpace(ip) ||
                    !IPAddress.TryParse(ip, out parsed) ||
                    parsed.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                rows.Add(row);
                ips.Add(ip);
                row.Cells[(int)Columns.Ping].Value = "Pinging...";
            }

            if (ips.Count == 0)
            {
                await ShowStatusAsync("No valid selected IP addresses to ping.", 2500);
                return;
            }

            var targets = new List<PingSelectedTarget>();
            for (int i = 0; i < ips.Count; i++)
            {
                DataGridViewRow capturedRow = rows[i];
                targets.Add(new PingSelectedTarget(
                    ips[i],
                    result =>
                    {
                        try
                        {
                            if (!IsDisposed && capturedRow != null && capturedRow.Index >= 0)
                                capturedRow.Cells[(int)Columns.Ping].Value = result;
                        }
                        catch
                        {
                        }
                    }));
            }

            PingSelectedResultsForm monitor = new PingSelectedResultsForm(
                targets,
                async (text, delay) => await ShowStatusAsync(text, delay));

            monitor.Show(this);
            await Task.CompletedTask;
        }

        private async Task<string> PingOnceForGridAsync(string ip, int timeoutMs)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    var sw_ping = System.Diagnostics.Stopwatch.StartNew();
                    PingReply reply = await ping.SendPingAsync(ip, timeoutMs);
                    sw_ping.Stop();
                    StartupDiagnostics.Increment("PingProbes");
                    StartupDiagnostics.AddTimer("PingProbes", sw_ping.ElapsedMilliseconds);
                    StartupDiagnostics.LogSlowOperation("Ping", ip, "Unknown", sw_ping.ElapsedMilliseconds, reply.Status == IPStatus.Success);

                    return reply.Status == IPStatus.Success
                        ? reply.RoundtripTime + " ms"
                        : reply.Status.ToString();
                }
            }
            catch
            {
                return "Failed";
            }
        }

        private void CancelCurrentPing()
        {
            if (pingCancellationTokenSource != null && !pingCancellationTokenSource.IsCancellationRequested)
                pingCancellationTokenSource.Cancel();

            if (activePingMonitor != null && !activePingMonitor.IsDisposed)
                activePingMonitor.CancelPings();
        }

        private void ApplyStatusMessage(string text, int version)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new MethodInvoker(() => ApplyStatusMessage(text, version)));
                }
                catch
                {
                }

                return;
            }

            if (version == Volatile.Read(ref statusMessageVersion) && statusLabel != null)
                statusLabel.Text = text;
        }

        private int SetStatusMessage(string text)
        {
            int version = Interlocked.Increment(ref statusMessageVersion);
            ApplyStatusMessage(text, version);
            return version;
        }

        private void SetSubnetScanStatus(string text)
        {
            int version = Interlocked.Increment(ref statusMessageVersion);

            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new MethodInvoker(() =>
                    {
                        if (version != Volatile.Read(ref statusMessageVersion) || IsDisposed || Disposing)
                            return;

                        Text = "Universal Scanner - " + text;
                        if (statusLabel != null)
                            statusLabel.Text = text;
                    }));
                }
                catch
                {
                }

                return;
            }

            if (version != Volatile.Read(ref statusMessageVersion))
                return;

            Text = "Universal Scanner - " + text;
            if (statusLabel != null)
                statusLabel.Text = text;
        }

        private async Task ShowStatusAsync(string text, int delay = 0)
        {
            int version = SetStatusMessage(text);

            if (delay > 0)
            {
                await Task.Delay(delay);

                int readyVersion = unchecked(version + 1);
                if (Interlocked.CompareExchange(ref statusMessageVersion, readyVersion, version) == version)
                {
                    ApplyStatusMessage("Ready", readyVersion);
                }
            }
        }

        // -----------------------------------------------------------------------
        // Batch operation helpers
        // -----------------------------------------------------------------------

        private List<DataGridViewRow> GetSelectedDeviceRows()
        {
            return dataGridView1.SelectedRows
                .Cast<DataGridViewRow>()
                .Where(r => r != null && !r.IsNewRow)
                .OrderBy(r => IpToUInt32Safe(r.Cells[(int)Columns.IPAddress].Value?.ToString()))
                .ToList();
        }

        private static bool IsValidIPv4Text(string ip)
        {
            IPAddress address;
            return !string.IsNullOrWhiteSpace(ip) &&
                   IPAddress.TryParse(ip.Trim(), out address) &&
                   address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        }

        private static uint IpToUInt32Safe(string ip)
        {
            try
            {
                if (!IsValidIPv4Text(ip))
                    return uint.MaxValue;

                byte[] bytes = IPAddress.Parse(ip.Trim()).GetAddressBytes();
                return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            }
            catch
            {
                return uint.MaxValue;
            }
        }

        private static string IncrementIPv4(string ip, int offset)
        {
            byte[] bytes = IPAddress.Parse(ip).GetAddressBytes();
            Array.Reverse(bytes);
            uint value = BitConverter.ToUInt32(bytes, 0);
            value += (uint)offset;
            byte[] outBytes = BitConverter.GetBytes(value);
            Array.Reverse(outBytes);
            return new System.Net.IPAddress(outBytes).ToString();
        }

        private class HoneywellBatchIpChangeItem
        {
            public DataGridViewRow Row;
            public string OldIp;
            public string NewIp;
            public string ExpectedMac;
            public string UniqueId;
        }

        private class HoneywellBatchVerificationResult
        {
            public HoneywellBatchIpChangeItem Item;
            public bool Success;
            public string Message;
        }

        private async Task<HoneywellBatchVerificationResult> VerifyHoneywellBatchIpChangeAsync(HoneywellBatchIpChangeItem item)
        {
            string expectedMac = NormalizeMac(item.ExpectedMac);

            for (int attempt = 1; attempt <= 18; attempt++)
            {
                NetworkTools.FlushArpEntry(item.NewIp);
                bool reachable = await NetworkTools.IsDeviceReachableAsync(item.NewIp, 1000);

                if (reachable)
                {
                    string actualMac = await NetworkTools.GetMacAsync(item.NewIp, 2, 250);
                    string normalizedActualMac = NormalizeMac(actualMac);

                    if (string.IsNullOrWhiteSpace(expectedMac) ||
                        string.IsNullOrWhiteSpace(normalizedActualMac) ||
                        normalizedActualMac == expectedMac)
                    {
                        return new HoneywellBatchVerificationResult
                        {
                            Item = item,
                            Success = true,
                            Message = string.IsNullOrWhiteSpace(actualMac)
                                ? "Verified at requested IP"
                                : "Verified at requested IP (" + actualMac + ")"
                        };
                    }

                    return new HoneywellBatchVerificationResult
                    {
                        Item = item,
                        Success = false,
                        Message = "Requested IP answered by different MAC: " + actualMac
                    };
                }

                await Task.Delay(2500);
            }

            bool oldIpStillReachable = await NetworkTools.IsDeviceReachableAsync(item.OldIp, 1000);
            return new HoneywellBatchVerificationResult
            {
                Item = item,
                Success = false,
                Message = oldIpStillReachable
                    ? "Still answering at old IP " + item.OldIp
                    : "Requested IP " + item.NewIp + " was not confirmed"
            };
        }

        private async Task<string> GetBoschTargetConflictMacAsync(string oldIp, string newIp, string currentMac)
        {
            if (string.Equals(oldIp, newIp, StringComparison.OrdinalIgnoreCase))
                return "";

            NetworkTools.FlushArpEntry(newIp);

            bool targetResponds = await NetworkTools.IsOnlineAsync(newIp, 1000);
            string targetMac = await NetworkTools.GetMacAsync(newIp, 2, 250);

            if (!targetResponds && string.IsNullOrWhiteSpace(targetMac))
                return "";

            string cleanTargetMac = NormalizeMac(targetMac);
            string cleanCurrentMac = NormalizeMac(currentMac);

            if (string.IsNullOrWhiteSpace(cleanCurrentMac))
                cleanCurrentMac = NormalizeMac(await NetworkTools.GetMacAsync(oldIp, 2, 250));

            if (!string.IsNullOrWhiteSpace(cleanTargetMac) &&
                !string.IsNullOrWhiteSpace(cleanCurrentMac) &&
                cleanTargetMac == cleanCurrentMac)
            {
                return "";
            }

            return string.IsNullOrWhiteSpace(targetMac) ? "Unknown" : targetMac;
        }

        // Convenience overload used from batch operations where no unique ID is available.
        private void QueueMacLookup(string ip)
        {
            QueueMacLookup(ip, "");
        }

        private async void batchChangeIPAddressesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<DataGridViewRow> rows = GetSelectedDeviceRows();

            if (rows.Count == 0)
            {
                MessageBox.Show(this,
                    "Select one or more devices first.",
                    "Batch Change IP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            ApplyAiBrandOverridesToSelectedRowsForAction();
            rows = GetSelectedDeviceRows();

            if (rows.Any(row => IsDahuaSwitchRow(GetDataRowFromGridRow(row))))
                return;

            bool batchIsBoschOnly = rows.All(r => IsBoschRow(GetDataRowFromGridRow(r)));
            bool batchIsAxisOnly = rows.All(r => IsAxisRow(GetDataRowFromGridRow(r)));

            using (BatchChangeIpForm form = new BatchChangeIpForm(rows.Count, batchIsBoschOnly ? "service" : batchIsAxisOnly ? "root" : "admin"))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                    return;

                if (!IsValidIPv4Text(form.StartIPAddress) ||
                    !IsValidIPv4Text(form.SubnetMask) ||
                    !IsValidIPv4Text(form.Gateway))
                {
                    MessageBox.Show(this,
                        "Start IP, subnet mask, or gateway is not valid.",
                        "Batch Change IP",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                DialogResult confirm = MessageBox.Show(this,
                    "Batch change " + rows.Count + " selected device(s)?\r\n\r\n" +
                    "First IP: " + form.StartIPAddress + "\r\n" +
                    "Last IP:  " + IncrementIPv4(form.StartIPAddress, rows.Count - 1),
                    "Batch Change IP",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (confirm != DialogResult.Yes)
                    return;

                int success = 0;
                int skipped = 0;
                int conflicts = 0;
                int authFailures = 0;
                int timeouts = 0;
                int otherFailures = 0;

                List<string> conflictReports = new List<string>();
                List<HoneywellBatchIpChangeItem> pendingHoneywellVerifications = new List<HoneywellBatchIpChangeItem>();

                for (int i = 0; i < rows.Count; i++)
                {
                    DataGridViewRow row = rows[i];
                    string oldIp = row.Cells[(int)Columns.IPAddress].Value?.ToString() ?? "";
                    string newIp = IncrementIPv4(form.StartIPAddress, i);

                    DataRow dataRow = GetDataRowFromGridRow(row);
                    bool isHikvision = IsHikvisionRow(dataRow);
                    bool isHoneywell = !isHikvision && IsHoneywellRow(dataRow);
                    bool isUniview = !isHikvision && !isHoneywell && IsUniviewRow(dataRow);
                    bool isDahua = !isHikvision && !isHoneywell && !isUniview && IsDahuaRow(dataRow);
                    bool isBosch = !isHikvision && !isHoneywell && !isUniview && !isDahua && IsBoschRow(dataRow);
                    bool isAxis = !isHikvision && !isHoneywell && !isUniview && !isDahua && !isBosch && IsAxisRow(dataRow);

                    if (string.IsNullOrWhiteSpace(oldIp))
                    {
                        skipped++;
                        continue;
                    }

                    await ShowStatusAsync("Batch IP " + oldIp + " -> " + newIp + " (" + (i + 1) + "/" + rows.Count + ")...");
                    row.Cells[(int)Columns.Ping].Value = "Changing IP...";
                    dataGridView1.Refresh();
                    
                    try
                    {
                        bool isOccupied = false;
                        string conflictingMac = "";
                        
                        if (isBosch)
                        {
                            conflictingMac = await GetBoschTargetConflictMacAsync(oldIp, newIp, row.Cells[(int)Columns.MAC].Value?.ToString() ?? "");
                            isOccupied = !string.IsNullOrWhiteSpace(conflictingMac);
                        }
                        // Check if target IP responds
                        else if (await NetworkTools.IsOnlineAsync(newIp, 1000) || !string.IsNullOrEmpty(conflictingMac = await Task.Run(() => NetworkTools.ForceArp(newIp))))
                        {
                            if (string.IsNullOrEmpty(conflictingMac))
                            {
                                conflictingMac = await Task.Run(() => NetworkTools.ForceArp(newIp));
                            }
                            
                            string currentMac = row.Cells[(int)Columns.MAC].Value?.ToString() ?? "";
                            // Treat as conflict if MAC is successfully resolved and doesn't belong to this device
                            if (!string.IsNullOrEmpty(conflictingMac) && !NormalizeMac(conflictingMac).Equals(NormalizeMac(currentMac), StringComparison.OrdinalIgnoreCase))
                            {
                                isOccupied = true;
                            }
                        }
                        
                        if (isOccupied)
                        {
                            conflicts++;
                            string conflictingVendor = NetworkTools.GetVendor(conflictingMac);
                            if (string.IsNullOrWhiteSpace(conflictingVendor)) conflictingVendor = "Unknown";
                            
                            string conflictDetails = $"Skipped — Target IP {newIp} is already in use by {conflictingMac}";
                            conflictDetails = $"Skipped - IP conflict: requested {newIp} is already in use by {conflictingMac}";
                            row.Cells[(int)Columns.Ping].Value = conflictDetails;
                            await ShowStatusAsync($"IP conflict: requested {newIp} is already in use by {conflictingMac}", 3500);

                            conflictReports.Add(
                                "----------------------------------\r\n\r\n" +
                                "Requested IP with conflict:\r\n" +
                                $"{newIp}\r\n\r\n" +
                                $"{oldIp}\r\n" +
                                "→ Requested:\r\n" +
                                $"{newIp}\r\n\r\n" +
                                "Already used by\r\n\r\n" +
                                "MAC:\r\n" +
                                $"{conflictingMac}\r\n\r\n" +
                                "Vendor:\r\n" +
                                $"{conflictingVendor}\r\n"
                            );

                            continue;
                        }

                        bool ok = false;
                        string message = "";

                        if (isHikvision)
                        {
                            ok = await Task.Run(() =>
                            {
                                string msg;
                                bool result = DeviceSDK.Hikvision.HikvisionNetwork.ChangeNetworkAddress(
                                    oldIp,
                                    form.Username,
                                    form.Password,
                                    newIp,
                                    form.SubnetMask,
                                    form.Gateway,
                                    false,
                                    out msg);
                                message = msg;
                                return result;
                            });
                        }
                        else if (isHoneywell)
                        {
                            var honeywellResult = await ChangeIpForm.ExecuteChangeIpCoreAsync(
                                "Honeywell",
                                dataRow?[(int)Columns.Type]?.ToString() ?? "",
                                oldIp,
                                form.Username,
                                form.Password,
                                newIp,
                                form.SubnetMask,
                                form.Gateway,
                                false,
                                status => { message = status; });

                            ok = honeywellResult.Success;
                            message = honeywellResult.Message;
                        }
                        else if (isUniview)
                        {
                            ok = await DeviceSDK.Uniview.UniviewNetwork.ChangeNetworkAddressAsync(
                                oldIp,
                                form.Username,
                                form.Password,
                                newIp,
                                form.SubnetMask,
                                form.Gateway,
                                false,
                                status => { message = status; });

                            if (!ok)
                                message = "Uniview ONVIF IP change failed. Check credentials and that ONVIF is enabled.";
                        }
                        else if (isDahua)
                        {
                            ok = await DeviceSDK.Dahua.DahuaNetwork.ChangeNetworkAddressAsync(
                                oldIp,
                                form.Username,
                                form.Password,
                                newIp,
                                form.SubnetMask,
                                form.Gateway,
                                false,
                                status => { message = status; });

                            if (!ok)
                                message = "Dahua IP change failed. Check credentials and that ONVIF or Dahua CGI access is enabled.";
                        }
                        else if (isBosch)
                        {
                            var boschResult = await DeviceSDK.Bosch.BoschNetwork.ChangeNetworkAddressAsync(
                                oldIp,
                                form.Username,
                                form.Password,
                                newIp,
                                form.SubnetMask,
                                form.Gateway,
                                false,
                                status => { message = status; });

                            if (boschResult.State == DeviceSDK.Bosch.BoschReadinessState.ConfigurationAccepted)
                            {
                                var finalResult = await DeviceSDK.Bosch.BoschNetwork.VerifyReadinessAsync(
                                    oldIp,
                                    newIp,
                                    boschResult.OriginalMac,
                                    false,
                                    () => TriggerInternalDiscovery(),
                                    (targetIp, targetMac) => IsDeviceDiscovered(targetIp, targetMac),
                                    status => { message = status; },
                                    () => false);

                                ok = finalResult.Success;
                                message = finalResult.Message;
                            }
                            else
                            {
                                ok = boschResult.Success;
                                message = boschResult.Message;
                            }
                        }
                        else if (isAxis)
                        {
                            ok = await DeviceSDK.Axis.AxisNetwork.ChangeNetworkAddressAsync(
                                oldIp,
                                form.Username,
                                form.Password,
                                newIp,
                                form.SubnetMask,
                                form.Gateway,
                                false,
                                status => { message = status; });

                            if (!ok)
                                message = "Axis IP change failed. Check credentials and VAPIX access.";
                        }
                        else
                        {
                            ok = await DeviceSDK.Common.GenericOnvifNetwork.ChangeNetworkAddressAsync(
                                oldIp,
                                form.Username,
                                form.Password,
                                newIp,
                                form.SubnetMask,
                                form.Gateway,
                                false,
                                status => { message = status; });

                            if (!ok)
                                message = "ONVIF IP change failed. Check credentials and that ONVIF is enabled.";
                        }

                        if (ok)
                        {
                            if (isBosch)
                            {
                                success++;
                                await CompleteBoschIpChangeRowAsync(row, oldIp, newIp);
                            }
                            else if (isHoneywell)
                            {
                                RegisterIpChangeGuard(oldIp);
                                RegisterRecentChangedIp(newIp);
                                DeviceSDK.Honeywell.HoneywellActivationCache.Remove(oldIp);
                                DeviceSDK.Honeywell.HoneywellActivationCache.MarkActive(newIp);

                                row.Cells[(int)Columns.IPAddress].Value = newIp;
                                row.Cells[(int)Columns.Ping].Value = "Rebooting...";
                                row.Cells[(int)Columns.Activation].Value = "Active";
                                dataGridView1.Refresh();

                                pendingHoneywellVerifications.Add(new HoneywellBatchIpChangeItem
                                {
                                    Row = row,
                                    OldIp = oldIp,
                                    NewIp = newIp,
                                    ExpectedMac = row.Cells[(int)Columns.MAC].Value?.ToString() ?? "",
                                    UniqueId = dataRow?[(int)Columns.UniqueId]?.ToString() ?? ""
                                });

                                await ShowStatusAsync("Honeywell IP accepted. Waiting briefly before next camera...", 3000);
                                await Task.Delay(3000);
                            }
                            else
                            {
                                success++;
                                await RefreshChangedHikvisionIpRowAsync(row, oldIp, newIp, form.SubnetMask);
                            }
                        }
                        else
                        {
                            if (message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("reachable", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                timeouts++;
                            }
                            else if (message.IndexOf("credential", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                authFailures++;
                            }
                            else
                            {
                                otherFailures++;
                            }
                            row.Cells[(int)Columns.Ping].Value = "IP change failed";
                            await ShowStatusAsync("Failed " + oldIp + ": " + message, 3500);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is TimeoutException)
                        {
                            timeouts++;
                        }
                        else if (ex is UnauthorizedAccessException)
                        {
                            authFailures++;
                        }
                        else
                        {
                            otherFailures++;
                        }
                        row.Cells[(int)Columns.Ping].Value = "IP change failed";
                        await ShowStatusAsync("Failed " + oldIp + ": Exception - " + ex.Message, 3500);
                    }

                    await Task.Delay(500);
                }

                if (pendingHoneywellVerifications.Count > 0)
                {
                    await ShowStatusAsync("Waiting for Honeywell cameras to reboot before verification...");
                    await Task.Delay(20000);
                    await ShowStatusAsync("Verifying Honeywell batch IP changes...");

                    HoneywellBatchVerificationResult[] honeywellResults = await Task.WhenAll(
                        pendingHoneywellVerifications.Select(VerifyHoneywellBatchIpChangeAsync).ToArray());

                    foreach (HoneywellBatchVerificationResult result in honeywellResults)
                    {
                        if (result.Success)
                        {
                            success++;
                            result.Item.Row.Cells[(int)Columns.Ping].Value = "Verified";
                            result.Item.Row.Cells[(int)Columns.Activation].Value = "Active";
                            QueueMacLookup(result.Item.NewIp);
                            QueueOnvifProfileLookup(result.Item.NewIp, result.Item.UniqueId);
                        }
                        else
                        {
                            otherFailures++;
                            if (result.Message.IndexOf("Still answering at old IP", StringComparison.OrdinalIgnoreCase) >= 0)
                                result.Item.Row.Cells[(int)Columns.IPAddress].Value = result.Item.OldIp;
                            result.Item.Row.Cells[(int)Columns.Ping].Value = "IP change failed";
                            await ShowStatusAsync("Honeywell verify failed " + result.Item.OldIp + " -> " + result.Item.NewIp + ": " + result.Message, 2500);
                        }
                    }

                    dataGridView1.Refresh();
                }

                string conflictText = conflictReports.Count > 0 ? "\r\n\r\n" + string.Join("\r\n", conflictReports) + "\r\n----------------------------------" : "";
                
                string summary = $"Selected Devices:\r\n{rows.Count}\r\n\r\nSuccessful:\r\n{success}\r\n\r\nSkipped:\r\n{skipped}\r\n\r\nFailed:\r\n{authFailures + timeouts + otherFailures}\r\n\r\nIP Conflicts:\r\n{conflicts}{conflictText}";
                
                await ShowStatusAsync($"Batch IP finished. Success: {success}, Conflicts: {conflicts}, Failures: {authFailures + timeouts + otherFailures}", 5000);
                MessageBox.Show(this,
                    summary,
                    "Batch Change IP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private async void batchActivateDevicesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<DataGridViewRow> rows = GetSelectedDeviceRows();

            if (rows.Count == 0)
            {
                MessageBox.Show(this,
                    "Select inactive Hikvision, Honeywell, Uniview, or Dahua devices first.",
                    "Batch Activate Devices",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            ApplyAiBrandOverridesToSelectedRowsForAction();
            rows = GetSelectedDeviceRows();

            if (rows.Any(row => IsDahuaSwitchRow(GetDataRowFromGridRow(row))))
                return;

            bool batchContainsAxis = rows.Any(r => IsAxisRow(GetDataRowFromGridRow(r)));

            using (BatchActivationForm form = new BatchActivationForm(rows.Count, batchContainsAxis))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                    return;

                int success = 0;
                int failed = 0;
                int alreadyActive = 0;

                for (int i = 0; i < rows.Count; i++)
                {
                    DataGridViewRow row = rows[i];
                    string ip = row.Cells[(int)Columns.IPAddress].Value?.ToString() ?? "";
                    string activation = row.Cells[(int)Columns.Activation].Value?.ToString() ?? "";

                    DataRow activateDataRow = GetDataRowFromGridRow(row);
                    bool isHikvisionCompatible = IsHikvisionCompatibleRow(activateDataRow);
                    bool isHoneywellActivate = IsHoneywellRow(activateDataRow);
                    bool isUniviewActivate = !isHikvisionCompatible && !isHoneywellActivate && IsUniviewRow(activateDataRow);
                    bool isDahuaActivate = !isHikvisionCompatible && !isHoneywellActivate && !isUniviewActivate && IsDahuaRow(activateDataRow);
                    bool isSunellActivate = !isHikvisionCompatible && !isHoneywellActivate && !isUniviewActivate && !isDahuaActivate && IsSunellRow(activateDataRow);
                    bool isTiandyActivate = !isHikvisionCompatible && !isHoneywellActivate && !isUniviewActivate && !isDahuaActivate && !isSunellActivate && IsTiandyRow(activateDataRow);
                    bool isBoschActivate = !isHikvisionCompatible && !isHoneywellActivate && !isUniviewActivate && !isDahuaActivate && !isSunellActivate && !isTiandyActivate && IsBoschRow(activateDataRow);
                    bool isAxisActivate = !isHikvisionCompatible && !isHoneywellActivate && !isUniviewActivate && !isDahuaActivate && !isSunellActivate && !isTiandyActivate && !isBoschActivate && IsAxisRow(activateDataRow);

                    if ((!isHikvisionCompatible && !isHoneywellActivate && !isUniviewActivate && !isDahuaActivate && !isSunellActivate && !isTiandyActivate && !isBoschActivate && !isAxisActivate) || string.IsNullOrWhiteSpace(ip))
                    {
                        failed++;
                        continue;
                    }

                    if (isHoneywellActivate && !IsHoneywellHc30Or35Row(activateDataRow))
                    {
                        await ShowStatusAsync("Checking Honeywell activation endpoint on " + ip + "...");
                        bool hasActivationEndpoint = await DeviceSDK.Honeywell.HoneywellActivationStatusProbe.HasActivationEndpointAsync(ip);
                        if (!hasActivationEndpoint)
                        {
                            await ShowStatusAsync("Using Honeywell legacy activation on " + ip + "...");
                        }
                    }

                      if (isUniviewActivate && !activation.Equals("Active", StringComparison.OrdinalIgnoreCase))
                      {
                          await ShowStatusAsync("Checking Uniview activation status on " + ip + "...");
                          string probeResult = await DeviceSDK.Uniview.UniviewActivationStatusProbe.GetStatusAsync(ip);
                          if (probeResult == "Active")
                          {
                              activation = "Active";
                          }
                      }

                      if (isBoschActivate && !activation.Equals("Active", StringComparison.OrdinalIgnoreCase))
                      {
                          await ShowStatusAsync("Checking Bosch activation status on " + ip + "...");
                          string probeResult = await DeviceSDK.Bosch.BoschActivationStatusProbe.GetStatusAsync(ip);
                          if (probeResult == "Active")
                          {
                              activation = "Active";
                          }
                      }

                      if (isAxisActivate && !activation.Equals("Active", StringComparison.OrdinalIgnoreCase))
                      {
                          await ShowStatusAsync("Checking Axis activation status on " + ip + "...");
                          string probeResult = await DeviceSDK.Axis.AxisActivationStatusProbe.GetStatusAsync(ip);
                          if (probeResult == "Active")
                          {
                              activation = "Active";
                          }
                      }

                      bool shouldAttemptHoneywellHcActivation =
                          isHoneywellActivate && IsHoneywellHc30Or35Row(activateDataRow);

                      // Honeywell HC30/HC35 rows can be shown as Active after IP work even when
                      // the selected camera still needs first-time activation. Let the camera's
                      // activation endpoint decide; it returns a friendly already-active result.
                      if (activation.Equals("Active", StringComparison.OrdinalIgnoreCase) &&
                          !shouldAttemptHoneywellHcActivation)
                      {
                          row.Cells[(int)Columns.Activation].Value = "Already activated — no action required";
                          alreadyActive++;
                          dataGridView1.Refresh();
                          continue;
                      }

                    await ShowStatusAsync("Activating " + ip + " (" + (i + 1) + "/" + rows.Count + ")...");
                    row.Cells[(int)Columns.Activation].Value = "Activating...";
                    dataGridView1.Refresh();

                    string message = "";
                    bool useHoneywellLegacyActivation = false;
                    if (isHoneywellActivate)
                    {
                        bool hasHcActivationEndpoint = await DeviceSDK.Honeywell.HoneywellActivationStatusProbe.HasActivationEndpointAsync(ip);
                        useHoneywellLegacyActivation = !hasHcActivationEndpoint;
                    }

                    bool ok = await Task.Run(() =>
                    {
                        string msg;
                        bool result;
                        string honeywellLegacyUsername = "admin";
                        string axisUsername = string.IsNullOrWhiteSpace(form.Username) ? "root" : form.Username;
                        if (isHoneywellActivate && UniversalLoginManager.HasUniversalCredentials())
                        {
                            UniversalLoginManager.GetUniversalCredentials(out string savedUsername, out string savedPassword);
                            if (!string.IsNullOrWhiteSpace(savedUsername))
                                honeywellLegacyUsername = savedUsername.Trim();
                        }

                        if (isHoneywellActivate && !useHoneywellLegacyActivation)
                            result = DeviceSDK.Honeywell.HoneywellActivator.ActivateDevice(ip, form.Password, out msg);
                        else if (isHoneywellActivate)
                            result = DeviceSDK.Honeywell.HoneywellLegacyActivator.ActivateDevice(ip, honeywellLegacyUsername, form.Password, out msg);
                        else if (isUniviewActivate)
                            result = DeviceSDK.Uniview.UniviewActivator.ActivateDevice(ip, "admin", form.Password, out msg);
                        else if (isDahuaActivate)
                            result = DeviceSDK.Dahua.DahuaActivator.ActivateDevice(ip, "admin", form.Password, out msg);
                        else if (isSunellActivate)
                            result = DeviceSDK.Sunell.SunellActivator.ActivateDevice(ip, "admin", form.Password, out msg);
                        else if (isTiandyActivate)
                            result = DeviceSDK.Tiandy.TiandyActivator.ActivateDevice(ip, "admin", form.Password, out msg);
                        else if (isBoschActivate)
                            result = DeviceSDK.Bosch.BoschActivator.ActivateDevice(ip, "service", form.Password, out msg);
                        else if (isAxisActivate)
                            result = DeviceSDK.Axis.AxisActivator.ActivateDevice(ip, axisUsername, form.Password, out msg);
                        else
                            result = DeviceSDK.Hikvision.HikvisionActivator.ActivateDevice(ip, form.Password, out msg);

                        message = msg;
                        return result;
                    });

                    string status;

                    if (ok)
                    {
                        status = "Active";

                        if (isHoneywellActivate)
                            DeviceSDK.Honeywell.HoneywellActivationCache.MarkActive(ip);
                    }
                    else
                    {
                        status = isUniviewActivate
                            ? "Unknown"
                            : isDahuaActivate
                            ? await Task.Run(() => DeviceSDK.Dahua.DahuaActivationStatusProbe.GetStatus(ip))
                            : isHoneywellActivate
                            ? await DeviceSDK.Honeywell.HoneywellActivationStatusProbe.GetStatusAsync(ip)
                            : isAxisActivate
                            ? await DeviceSDK.Axis.AxisActivationStatusProbe.GetStatusAsync(ip)
                            : isBoschActivate
                            ? await DeviceSDK.Bosch.BoschActivationStatusProbe.GetStatusAsync(ip)
                            : await DeviceSDK.Hikvision.HikvisionActivationStatusProbe.GetStatusAsync(ip);
                    }

                    row.Cells[(int)Columns.Activation].Value =
                        string.IsNullOrWhiteSpace(status) ? (ok ? "Active" : "Unknown") : status;

                    bool alreadyActiveResult =
                        ok &&
                        isHoneywellActivate &&
                        message.IndexOf("already active", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (alreadyActiveResult)
                    {
                        alreadyActive++;
                        QueueMacLookup(ip);
                    }
                    else if (ok || string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
                    {
                        success++;
                        QueueMacLookup(ip);
                    }
                    else
                    {
                        failed++;
                        await ShowStatusAsync("Activation failed " + ip + ": " + message, 3500);
                    }

                    dataGridView1.Refresh();
                    await Task.Delay(500);
                }

                await ShowStatusAsync("Batch activation finished. Success: " + success + ", Failed: " + failed, 5000);
                MessageBox.Show(this,
                    $"Selected Devices:\r\n{rows.Count}\r\n\r\nActivated:\r\n{success}\r\n\r\nAlready Active:\r\n{alreadyActive}\r\n\r\nFailed:\r\n{failed}",
                    "Batch Activate Devices",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        // -----------------------------------------------------------------------

        private void BuildRightClickMenu()
        {
            rightClickMenu.Items.Clear();

            rightClickMenu.Items.Add("Open selected in browser", null, openSelectedInBrowserToolStripMenuItem_Click);
            rightClickMenu.Items.Add("Open Across Subnet in Browser", null, openAcrossSubnetInBrowserToolStripMenuItem_Click);
            viewMenu = new ToolStripMenuItem("View");
            showLiveByVlcMenuItem = new ToolStripMenuItem("Show Live by VLC...", null, showLiveByVlcToolStripMenuItem_Click);
            showLiveByVlcMenuItem.ShortcutKeyDisplayString = "Alt+L";
            showSelectedByVlcMenuItem = new ToolStripMenuItem("Show Selected by VLC Mosaic", null, showSelectedByVlcToolStripMenuItem_Click);
            showSelectedByVlcMenuItem.ShortcutKeyDisplayString = "Alt+V";
            viewMenu.DropDownItems.Add(showLiveByVlcMenuItem);
            viewMenu.DropDownItems.Add(showSelectedByVlcMenuItem);
            rightClickMenu.Items.Add(viewMenu);
            onvifEventMonitorMenuItem = new ToolStripMenuItem("ONVIF Live Event Monitor...", null, onvifEventMonitorToolStripMenuItem_Click);
            initializeDeviceMenuItem = new ToolStripMenuItem("Initialize / Activate Device...", null, initializeDeviceToolStripMenuItem_Click);
            changeIpMenuItem = new ToolStripMenuItem("Change IP Address...", null, changeIPAddressToolStripMenuItem_Click);
            changeIpAcrossSubnetMenuItem = new ToolStripMenuItem("Change IP Across Subnet...", null, changeIPAcrossSubnetToolStripMenuItem_Click);
            rightClickMenu.Items.Add(onvifEventMonitorMenuItem);
            rightClickMenu.Items.Add(initializeDeviceMenuItem);
            rightClickMenu.Items.Add(changeIpMenuItem);
            rightClickMenu.Items.Add(changeIpAcrossSubnetMenuItem);

            onvifConfigMenu = new ToolStripMenuItem("ONVIF Configuration");
            onvifDateTimeToolStripMenuItem = new ToolStripMenuItem("Date and Time...", null, onvifDateTimeToolStripMenuItem_Click);
            hikvisionOnvifEnableItem = new ToolStripMenuItem("Enable Hikvision ONVIF...", null, singleEnableItem_Click);
            hikvisionOnvifBatchEnableItem = new ToolStripMenuItem("Batch Enable Hikvision ONVIF...", null, batchEnableItem_Click);
            
            onvifMotionDetectionMenu = new ToolStripMenuItem("Motion Detection");
            motionUniviewItem = new ToolStripMenuItem("Uniview...", null, motionUniviewItem_Click);
            motionHikvisionItem = new ToolStripMenuItem("Hikvision (Not implemented yet)") { Enabled = false };
            motionDahuaItem = new ToolStripMenuItem("Dahua (Not implemented yet)") { Enabled = false };
            motionHoneywellItem = new ToolStripMenuItem("Honeywell...", null, motionHoneywellItem_Click);
            motionAxisItem = new ToolStripMenuItem("Axis - Start Motion App...", null, motionAxisItem_Click);
            
            onvifMotionDetectionMenu.DropDownItems.Add(motionUniviewItem);
            onvifMotionDetectionMenu.DropDownItems.Add(motionHikvisionItem);
            onvifMotionDetectionMenu.DropDownItems.Add(motionDahuaItem);
            onvifMotionDetectionMenu.DropDownItems.Add(motionHoneywellItem);
            onvifMotionDetectionMenu.DropDownItems.Add(motionAxisItem);

            onvifConfigMenu.DropDownItems.Add(onvifDateTimeToolStripMenuItem);
            onvifConfigMenu.DropDownItems.Add(new ToolStripSeparator());
            onvifConfigMenu.DropDownItems.Add(hikvisionOnvifEnableItem);
            onvifConfigMenu.DropDownItems.Add(hikvisionOnvifBatchEnableItem);
            onvifConfigMenu.DropDownItems.Add(new ToolStripSeparator());
            onvifConfigMenu.DropDownItems.Add(onvifMotionDetectionMenu);
            rightClickMenu.Items.Add(onvifConfigMenu);


            batchOperationsMenu = new ToolStripMenuItem("Batch Operations");
            batchOperationsMenu.DropDownItems.Add("Batch Activate Selected...", null, batchActivateDevicesToolStripMenuItem_Click);
            batchOperationsMenu.DropDownItems.Add("Batch Change IP Selected...", null, batchChangeIPAddressesToolStripMenuItem_Click);
            batchOperationsMenu.DropDownItems.Add("Batch Change IP Across Subnets...", null, batchChangeIpAcrossSubnetsToolStripMenuItem_Click);
            rightClickMenu.Items.Add(batchOperationsMenu);

            rightClickMenu.Items.Add("Delete Selected Rows", null, deleteSelectedRowsToolStripMenuItem_Click);

            ToolStripMenuItem pingMenu = new ToolStripMenuItem("Ping");
            pingSelectedOnceMenuItem = new ToolStripMenuItem("Ping Selected", null, async (s, e) => await PingSelectedRowsOnceAsync());
            pingMenu.DropDownItems.Add(pingSelectedOnceMenuItem);
            pingMenu.DropDownItems.Add("Ping 5 Times", null, (s, e) => OpenPingMonitor(5, 1000, 1000));
            pingMenu.DropDownItems.Add("Ping 10 Times", null, (s, e) => OpenPingMonitor(10, 1000, 1000));
            pingMenu.DropDownItems.Add("Ping 20 Times", null, (s, e) => OpenPingMonitor(20, 1000, 1000));
            pingMenu.DropDownItems.Add(new ToolStripSeparator());
            pingMenu.DropDownItems.Add("Custom...", null, (s, e) => OpenCustomPingMonitor());
            pingMenu.DropDownItems.Add("Continuous", null, (s, e) => OpenPingMonitor(-1, 1000, 1000));
            pingMenu.DropDownItems.Add("Stop Ping", null, (s, e) => CancelCurrentPing());
            rightClickMenu.Items.Add(pingMenu);

            rightClickMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem copyMenu = new ToolStripMenuItem("Copy");
            copyMenu.DropDownItems.Add("IP Address", null, copyIPAddressToolStripMenuItem_Click);
            copyMenu.DropDownItems.Add("MAC Address", null, copyMACAddressToolStripMenuItem_Click);
            copyMenu.DropDownItems.Add("Vendor", null, copyVendorToolStripMenuItem_Click);
            copyMenu.DropDownItems.Add("Model", null, copyTypeToolStripMenuItem_Click);
            copyMenu.DropDownItems.Add("Device Information", null, copyDeviceInformationToolStripMenuItem_Click);
            copyMenu.DropDownItems.Add(new ToolStripSeparator());
            copyMenu.DropDownItems.Add("Export Selected IP/MAC Table to PDF...", null, exportSelectedIpMacPdfToolStripMenuItem_Click);
            rightClickMenu.Items.Add(copyMenu);

            rightClickMenu.Items.Add(new ToolStripSeparator());

            rightClickMenu.Items.Add("Rescan", null, refreshToolStripMenuItem_Click);
            rightClickMenu.Items.Add("Clear and rescan", null, clearListToolStripMenuItem_Click);
            showUniqueIdColumnMenuItem = new ToolStripMenuItem("Show Unique ID Column");
            showUniqueIdColumnMenuItem.CheckOnClick = true;
            showUniqueIdColumnMenuItem.Click += (s, e) =>
            {
                if (dataGridView1.Columns.Contains(ColumnNames[(int)Columns.UniqueId]))
                    dataGridView1.Columns[ColumnNames[(int)Columns.UniqueId]].Visible = showUniqueIdColumnMenuItem.Checked;
            };
            rightClickMenu.Items.Add(showUniqueIdColumnMenuItem);
            ToolStripMenuItem exportMenu = new ToolStripMenuItem("Export list");
            exportMenu.DropDownItems.Add("CSV", null, exportListToolStripMenuItem_Click);
            exportMenu.DropDownItems.Add("PDF", null, exportListPdfToolStripMenuItem_Click);
            rightClickMenu.Items.Add(exportMenu);

            ApplyMenuTheme(rightClickMenu);
        }
        private void BuildStatusBar()
        {
            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel();
            deviceCountLabel = new ToolStripStatusLabel();
            selectedCountLabel = new ToolStripStatusLabel();
            statusRightSpacerLabel = new ToolStripStatusLabel();

            statusStrip.Dock = DockStyle.Bottom;
            statusStrip.SizingGrip = false;
            statusStrip.Height = 24;

            statusLabel.Text = "Ready";
            statusLabel.Spring = true;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;

            deviceCountLabel.Text = "Discovered devices: 0";
            deviceCountLabel.Spring = false;
            deviceCountLabel.TextAlign = ContentAlignment.MiddleCenter;

            selectedCountLabel.Text = "Selected devices: 0";
            selectedCountLabel.Spring = false;
            selectedCountLabel.TextAlign = ContentAlignment.MiddleCenter;

            statusRightSpacerLabel.Spring = true;
            statusRightSpacerLabel.Text = "";

            statusStrip.Items.Add(statusLabel);
            statusStrip.Items.Add(deviceCountLabel);
            statusStrip.Items.Add(selectedCountLabel);
            statusStrip.Items.Add(statusRightSpacerLabel);

            this.Controls.Add(statusStrip);
            this.Controls.SetChildIndex(statusStrip, 0);
            statusStrip.BringToFront();
            UpdateDeviceCountLabel();
            UpdateSelectedCountLabel();
        }

        private void UpdateDeviceCountLabel()
        {
            if (deviceCountLabel == null)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(UpdateDeviceCountLabel));
                return;
            }

            int totalCount = foundDeviceList != null ? foundDeviceList.Rows.Count : 0;
            int visibleCount = binding != null ? binding.Count : totalCount;
            deviceCountLabel.Text = visibleCount == totalCount
                ? "Discovered devices: " + totalCount
                : "Discovered devices: " + visibleCount + " of " + totalCount;
        }

        private void UpdateSelectedCountLabel()
        {
            if (selectedCountLabel == null)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(UpdateSelectedCountLabel));
                return;
            }

            int count = dataGridView1 != null ? dataGridView1.SelectedRows.Count : 0;
            selectedCountLabel.Text = "Selected devices: " + count;
        }

        private void ApplyMainDesign()
        {
            if (mainDesignApplied)
                return;

            mainDesignApplied = true;
            SuspendLayout();

            AppVisualPalette palette = AppVisualTheme.Current;
            Color navy = palette.Header;
            Color toolbarBack = palette.WindowBackground;
            Color borderBlue = palette.Border;
            Color scanBlue = palette.Accent;
            Color stopRed = palette.Danger;

            BackColor = toolbarBack;
            ForeColor = palette.TextPrimary;
            Font = AppVisualTheme.BodyFont;

            headerPanel = new Panel
            {
                BackColor = navy,
                Height = MainHeaderHeight,
                Left = 0,
                Top = 0,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            toolbarPanel = new Panel
            {
                BackColor = toolbarBack,
                Height = MainToolbarHeight,
                Left = 0,
                Top = MainHeaderHeight,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            toolbarPanel.Paint += (sender, e) =>
            {
                using (Pen pen = new Pen(borderBlue))
                    e.Graphics.DrawLine(pen, 0, toolbarPanel.Height - 1, toolbarPanel.Width, toolbarPanel.Height - 1);
            };

            headerIconBox = new PictureBox
            {
                Image = Icon != null ? Icon.ToBitmap() : null,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Left = 18,
                Top = 11,
                Width = 42,
                Height = 42
            };

            headerTitleLabel = new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                Font = AppVisualTheme.DisplayFont,
                Left = 72,
                Top = 11,
                Text = "Universal Scanner"
            };

            headerSubtitleLabel = new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                ForeColor = palette.HeaderSecondaryText,
                Font = AppVisualTheme.CaptionFont,
                Left = 74,
                Top = 38,
                Text = "Discover - Analyze - Identify"
            };

            headerTimeLabel = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                Font = AppVisualTheme.ClockFont,
                TextAlign = ContentAlignment.MiddleRight
            };

            headerDateLabel = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = palette.HeaderSecondaryText,
                Font = AppVisualTheme.BodyFont,
                TextAlign = ContentAlignment.MiddleRight
            };

            Controls.Add(headerPanel);
            Controls.Add(toolbarPanel);

            headerPanel.Controls.Add(headerIconBox);
            headerPanel.Controls.Add(headerTitleLabel);
            headerPanel.Controls.Add(headerSubtitleLabel);
            headerPanel.Controls.Add(headerTimeLabel);
            headerPanel.Controls.Add(headerDateLabel);
            headerPanel.Controls.Add(menuStrip1);

            headerClockTimer = new System.Windows.Forms.Timer();
            headerClockTimer.Interval = 1000;
            headerClockTimer.Tick += (sender, e) => UpdateHeaderClock();
            UpdateHeaderClock();
            headerClockTimer.Start();

            toolsToolStripMenuItem.Text = "Tools";
            toolsToolStripMenuItem.Image = AppVisualTheme.CreateIcon(AppVisualIcon.Tools, Color.White);
            loginToolStripMenuItem.Image = AppVisualTheme.CreateIcon(AppVisualIcon.Lock, Color.White);
            toolbarPanel.Controls.Add(scanButton);
            toolbarPanel.Controls.Add(newVersion);
            toolbarPanel.Controls.Add(scanSubnetButton);
            toolbarPanel.Controls.Add(subnetTextBox);
            toolbarPanel.Controls.Add(scanProgressBar);
            toolbarPanel.Controls.Add(stopSubnetButton);
            toolbarPanel.Controls.Add(aboutButton);

            StyleMainMenu(navy);
            StyleToolbarButton(scanButton, scanBlue, Color.White, scanBlue, true);
            StyleToolbarButton(scanSubnetButton, scanBlue, Color.White, scanBlue, true);
            StyleToolbarButton(stopSubnetButton, stopRed, Color.White, stopRed, true);
            StyleToolbarButton(aboutButton, palette.Surface, palette.TextPrimary, palette.BorderStrong, false);
            scanButton.Image = AppVisualTheme.CreateIcon(AppVisualIcon.Scan, Color.White);
            scanSubnetButton.Image = AppVisualTheme.CreateIcon(AppVisualIcon.Network, Color.White);
            stopSubnetButton.Image = AppVisualTheme.CreateIcon(AppVisualIcon.Stop, Color.White);
            aboutButton.Image = AppVisualTheme.CreateIcon(AppVisualIcon.Info, palette.TextSecondary);
            StyleMainGrid();
            StyleStatusStrip();

            subnetTextBox.Font = AppVisualTheme.BodyFont;
            subnetTextBox.BackColor = palette.InputBackground;
            subnetTextBox.ForeColor = palette.TextPrimary;
            subnetTextBox.BorderStyle = BorderStyle.FixedSingle;
            scanProgressBar.Style = ProgressBarStyle.Continuous;
            scanProgressBar.BackColor = palette.SurfaceMuted;
            scanProgressBar.ForeColor = palette.Accent;
            newVersion.Font = AppVisualTheme.CaptionFont;
            newVersion.LinkColor = palette.Accent;
            newVersion.ActiveLinkColor = palette.AccentPressed;
            newVersion.VisitedLinkColor = palette.Accent;
            AppVisualTheme.ApplyWindowChrome(this);
            AppVisualTheme.ApplyProgressBarColors(scanProgressBar);

            dataGridView1.BorderStyle = BorderStyle.None;
            dataGridView1.Anchor = AnchorStyles.None;

            Resize += ScannerWindow_DesignResize;
            UpdateMainDesignLayout();
            ResumeLayout(false);
        }

        private void ScannerWindow_DesignResize(object sender, EventArgs e)
        {
            UpdateMainDesignLayout();
        }

        private void UpdateMainDesignLayout()
        {
            if (headerPanel == null || toolbarPanel == null)
                return;

            int width = Math.Max(900, ClientSize.Width);
            int contentTop = MainHeaderHeight + MainToolbarHeight;
            int statusHeight = statusStrip != null ? statusStrip.Height : 24;

            headerPanel.Bounds = new Rectangle(0, 0, width, MainHeaderHeight);
            toolbarPanel.Bounds = new Rectangle(0, MainHeaderHeight, width, MainToolbarHeight);

            if (dataGridView1 != null)
            {
                int advancedHeight = GetAdvancedViewReservedHeight(contentTop, statusHeight);
                dataGridView1.SetBounds(0, contentTop, width, ClientSize.Height - contentTop - statusHeight - advancedHeight);
            }

            menuStrip1.Dock = DockStyle.None;
            int toolsLeft = headerTitleLabel != null
                ? Math.Max(420, headerTitleLabel.Right + 55)
                : 420;
            menuStrip1.Location = new Point(toolsLeft, 15);
            menuStrip1.Height = 36;

            if (headerTimeLabel != null && headerDateLabel != null)
            {
                int clockWidth = 220;
                int clockLeft = Math.Max(menuStrip1.Right + 24, headerPanel.Width - clockWidth - 44);
                headerTimeLabel.SetBounds(clockLeft, 10, clockWidth, 26);
                headerDateLabel.SetBounds(clockLeft, 36, clockWidth, 18);
            }

            scanButton.SetBounds(18, 27, 88, 30);
            newVersion.SetBounds(114, 35, 110, 18);
            scanSubnetButton.SetBounds(212, 27, 116, 30);
            subnetTextBox.SetBounds(334, 31, 100, 23);

            int rightStart = Math.Max(600, toolbarPanel.Width - 190);
            stopSubnetButton.SetBounds(rightStart, 27, 86, 30);
            aboutButton.SetBounds(toolbarPanel.Width - 94, 27, 76, 30);

            int progressLeft = 454;
            int progressRight = Math.Max(progressLeft + 120, stopSubnetButton.Left - 420);
            scanProgressBar.SetBounds(progressLeft, 27, progressRight - progressLeft, 30);

            int reservedAdvancedHeight = GetAdvancedViewReservedHeight(contentTop, statusHeight);
            dataGridView1.SetBounds(0, contentTop, ClientSize.Width, Math.Max(100, ClientSize.Height - contentTop - statusHeight - reservedAdvancedHeight));
            UpdateAdvancedViewLayout(contentTop, ClientSize.Width, statusHeight);
            headerPanel.BringToFront();
            toolbarPanel.BringToFront();
            if (advancedViewPanel != null)
                advancedViewPanel.BringToFront();
            if (statusStrip != null)
                statusStrip.BringToFront();
        }

        private void UpdateHeaderClock()
        {
            if (headerTimeLabel == null || headerDateLabel == null)
                return;

            DateTime now = DateTime.Now;
            headerTimeLabel.Text = now.ToString("hh:mm tt");
            headerDateLabel.Text = now.ToString("dd/MM/yyyy");
        }

        private void StyleMainMenu(Color headerBackColor)
        {
            AppVisualPalette palette = AppVisualTheme.Current;
            if (modernMenuRenderer == null)
                modernMenuRenderer = new ModernMenuRenderer(palette);

            menuStrip1.BackColor = headerBackColor;
            menuStrip1.ForeColor = Color.White;
            menuStrip1.Renderer = modernMenuRenderer;
            menuStrip1.Padding = new Padding(0);
            StyleHeaderMenuItem(toolsToolStripMenuItem);
            StyleHeaderMenuItem(loginToolStripMenuItem);

            foreach (ToolStripItem item in menuStrip1.Items)
            {
                item.ForeColor = Color.White;
                item.BackColor = headerBackColor;
                if (item is ToolStripMenuItem menuItem)
                {
                    menuItem.DropDown.BackColor = palette.Surface;
                    foreach (ToolStripItem dropDownItem in menuItem.DropDownItems)
                    {
                        dropDownItem.ForeColor = palette.TextPrimary;
                        dropDownItem.BackColor = palette.Surface;
                    }

                    ApplyMenuTheme(menuItem.DropDown);
                }
            }

            ApplyMenuTheme(rightClickMenu);
        }

        private void StyleHeaderMenuItem(ToolStripMenuItem item)
        {
            if (item == null)
                return;

            item.Font = AppVisualTheme.MenuHeaderFont;
            item.Padding = new Padding(10, 6, 10, 6);
            item.Margin = new Padding(0, 0, 8, 0);
            item.ImageScaling = ToolStripItemImageScaling.None;
        }

        private void ApplyMenuTheme(ToolStripDropDown menu)
        {
            if (menu == null)
                return;

            AppVisualPalette palette = AppVisualTheme.Current;
            if (modernMenuRenderer == null)
                modernMenuRenderer = new ModernMenuRenderer(palette);

            menu.Renderer = modernMenuRenderer;
            menu.BackColor = palette.Surface;
            menu.ForeColor = palette.TextPrimary;
            menu.Padding = new Padding(6);
            if (menu is ToolStripDropDownMenu dropDownMenu)
            {
                dropDownMenu.ImageScalingSize = new Size(18, 18);
                dropDownMenu.ShowImageMargin = true;
            }

            foreach (ToolStripItem item in menu.Items)
            {
                item.BackColor = palette.Surface;
                item.ForeColor = palette.TextPrimary;
                item.Font = AppVisualTheme.MenuFont;
                item.Padding = new Padding(8, 6, 8, 6);

                if (item is ToolStripMenuItem childMenu)
                    ApplyMenuTheme(childMenu.DropDown);
            }
        }

        private void StyleToolbarButton(Button button, Color backColor, Color foreColor, Color borderColor, bool filled)
        {
            AppVisualPalette palette = AppVisualTheme.Current;
            button.UseVisualStyleBackColor = false;
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = borderColor;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = filled ? palette.AccentHover : palette.SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = filled ? palette.AccentPressed : palette.SurfacePressed;
            button.Font = AppVisualTheme.ButtonFont;
            button.Cursor = Cursors.Hand;
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.Padding = new Padding(5, 0, 5, 0);
        }

        private void StyleMainGrid()
        {
            AppVisualPalette palette = AppVisualTheme.Current;
            dataGridView1.EnableHeadersVisualStyles = false;
            dataGridView1.BackgroundColor = palette.Surface;
            dataGridView1.GridColor = palette.GridLine;
            dataGridView1.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dataGridView1.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            dataGridView1.ColumnHeadersDefaultCellStyle.BackColor = palette.GridHeader;
            dataGridView1.ColumnHeadersDefaultCellStyle.ForeColor = palette.TextPrimary;
            dataGridView1.ColumnHeadersDefaultCellStyle.Font = AppVisualTheme.GridHeaderFont;
            dataGridView1.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.GridHeader;
            dataGridView1.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.TextPrimary;
            dataGridView1.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            dataGridView1.DefaultCellStyle.BackColor = palette.Surface;
            dataGridView1.DefaultCellStyle.ForeColor = palette.TextPrimary;
            dataGridView1.DefaultCellStyle.SelectionBackColor = palette.Selection;
            dataGridView1.DefaultCellStyle.SelectionForeColor = palette.SelectionText;
            dataGridView1.DefaultCellStyle.Font = AppVisualTheme.GridFont;
            dataGridView1.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
            dataGridView1.AlternatingRowsDefaultCellStyle.BackColor = palette.SurfaceAlternate;
            dataGridView1.AlternatingRowsDefaultCellStyle.ForeColor = palette.TextPrimary;
            dataGridView1.RowTemplate.Height = 30;
            dataGridView1.ColumnHeadersHeight = 34;
            dataGridView1.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        }

        private void StyleStatusStrip()
        {
            if (statusStrip == null || statusLabel == null || deviceCountLabel == null || selectedCountLabel == null)
                return;

            AppVisualPalette palette = AppVisualTheme.Current;
            statusStrip.BackColor = palette.SurfaceMuted;
            statusStrip.ForeColor = palette.TextSecondary;
            statusStrip.SizingGrip = true;
            statusLabel.ForeColor = palette.TextSecondary;
            statusLabel.Font = AppVisualTheme.CaptionFont;
            deviceCountLabel.ForeColor = palette.AccentText;
            deviceCountLabel.Font = AppVisualTheme.CaptionStrongFont;
            selectedCountLabel.ForeColor = palette.AccentText;
            selectedCountLabel.Font = AppVisualTheme.CaptionStrongFont;
        }
    }


    public enum AppVisualIcon
    {
        Tools,
        Lock,
        Scan,
        Network,
        Stop,
        Info
    }

    public sealed class AppVisualPalette
    {
        public bool IsDark { get; private set; }
        public Color WindowBackground { get; private set; }
        public Color Surface { get; private set; }
        public Color SurfaceMuted { get; private set; }
        public Color SurfaceAlternate { get; private set; }
        public Color SurfaceHover { get; private set; }
        public Color SurfacePressed { get; private set; }
        public Color InputBackground { get; private set; }
        public Color Header { get; private set; }
        public Color HeaderSecondaryText { get; private set; }
        public Color Accent { get; private set; }
        public Color AccentHover { get; private set; }
        public Color AccentPressed { get; private set; }
        public Color AccentText { get; private set; }
        public Color Danger { get; private set; }
        public Color TextPrimary { get; private set; }
        public Color TextSecondary { get; private set; }
        public Color Border { get; private set; }
        public Color BorderStrong { get; private set; }
        public Color GridLine { get; private set; }
        public Color GridHeader { get; private set; }
        public Color Selection { get; private set; }
        public Color SelectionText { get; private set; }

        public static AppVisualPalette Create(bool dark)
        {
            if (dark)
            {
                return new AppVisualPalette
                {
                    IsDark = true,
                    WindowBackground = Color.FromArgb(23, 26, 31),
                    Surface = Color.FromArgb(32, 36, 43),
                    SurfaceMuted = Color.FromArgb(41, 46, 54),
                    SurfaceAlternate = Color.FromArgb(36, 41, 49),
                    SurfaceHover = Color.FromArgb(48, 55, 65),
                    SurfacePressed = Color.FromArgb(57, 65, 76),
                    InputBackground = Color.FromArgb(27, 31, 37),
                    Header = Color.FromArgb(23, 45, 75),
                    HeaderSecondaryText = Color.FromArgb(188, 204, 224),
                    Accent = Color.FromArgb(0, 120, 212),
                    AccentHover = Color.FromArgb(16, 134, 225),
                    AccentPressed = Color.FromArgb(0, 99, 177),
                    AccentText = Color.FromArgb(96, 205, 255),
                    Danger = Color.FromArgb(196, 43, 28),
                    TextPrimary = Color.FromArgb(243, 246, 250),
                    TextSecondary = Color.FromArgb(174, 183, 194),
                    Border = Color.FromArgb(58, 66, 77),
                    BorderStrong = Color.FromArgb(85, 95, 108),
                    GridLine = Color.FromArgb(53, 60, 70),
                    GridHeader = Color.FromArgb(43, 49, 58),
                    Selection = Color.FromArgb(23, 76, 113),
                    SelectionText = Color.White
                };
            }

            return new AppVisualPalette
            {
                IsDark = false,
                WindowBackground = Color.FromArgb(243, 246, 250),
                Surface = Color.White,
                SurfaceMuted = Color.FromArgb(248, 250, 252),
                SurfaceAlternate = Color.FromArgb(247, 249, 252),
                SurfaceHover = Color.FromArgb(237, 243, 249),
                SurfacePressed = Color.FromArgb(224, 233, 243),
                InputBackground = Color.White,
                Header = Color.FromArgb(28, 61, 108),
                HeaderSecondaryText = Color.FromArgb(211, 224, 239),
                Accent = Color.FromArgb(15, 108, 189),
                AccentHover = Color.FromArgb(0, 95, 184),
                AccentPressed = Color.FromArgb(0, 78, 146),
                AccentText = Color.FromArgb(15, 91, 158),
                Danger = Color.FromArgb(196, 43, 28),
                TextPrimary = Color.FromArgb(24, 33, 47),
                TextSecondary = Color.FromArgb(86, 99, 116),
                Border = Color.FromArgb(215, 222, 232),
                BorderStrong = Color.FromArgb(155, 167, 181),
                GridLine = Color.FromArgb(224, 229, 236),
                GridHeader = Color.FromArgb(237, 242, 248),
                Selection = Color.FromArgb(15, 108, 189),
                SelectionText = Color.White
            };
        }
    }

    public static class AppVisualTheme
    {
        private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmUseImmersiveDarkMode = 20;
        private const int ProgressSetBarColor = 0x0409;
        private const int ProgressSetBackgroundColor = 0x2001;
        private static readonly AppVisualPalette current = AppVisualPalette.Create(IsWindowsDarkTheme());

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr windowHandle, string subApplicationName, string subIdList);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);

        public static AppVisualPalette Current => current;
        public static Font BodyFont => new Font("Segoe UI", 9F, FontStyle.Regular);
        public static Font CaptionFont => new Font("Segoe UI", 8.5F, FontStyle.Regular);
        public static Font CaptionStrongFont => new Font("Segoe UI Semibold", 8.5F, FontStyle.Regular);
        public static Font ButtonFont => new Font("Segoe UI Semibold", 9F, FontStyle.Regular);
        public static Font MenuFont => new Font("Segoe UI", 9.5F, FontStyle.Regular);
        public static Font MenuHeaderFont => new Font("Segoe UI Semibold", 11F, FontStyle.Regular);
        public static Font GridFont => new Font("Segoe UI", 9F, FontStyle.Regular);
        public static Font GridHeaderFont => new Font("Segoe UI Semibold", 9F, FontStyle.Regular);
        public static Font DisplayFont => new Font("Segoe UI Semibold", 16F, FontStyle.Regular);
        public static Font ClockFont => new Font("Segoe UI Semibold", 13F, FontStyle.Regular);

        private static bool IsWindowsDarkTheme()
        {
            string overrideTheme = Environment.GetEnvironmentVariable("UNIVERSALSCANNER_THEME");
            if (string.Equals(overrideTheme, "dark", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(overrideTheme, "light", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key != null ? key.GetValue("AppsUseLightTheme") : null;
                    return value is int && (int)value == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static Bitmap CreateIcon(AppVisualIcon icon, Color color)
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(color, 1.6F))
            using (SolidBrush brush = new SolidBrush(color))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;

                if (icon == AppVisualIcon.Scan)
                {
                    graphics.DrawEllipse(pen, 2.5F, 2.5F, 7.5F, 7.5F);
                    graphics.DrawLine(pen, 9F, 9F, 14F, 14F);
                }
                else if (icon == AppVisualIcon.Network)
                {
                    graphics.DrawEllipse(pen, 6F, 1.5F, 4F, 4F);
                    graphics.DrawEllipse(pen, 1.5F, 10F, 4F, 4F);
                    graphics.DrawEllipse(pen, 10.5F, 10F, 4F, 4F);
                    graphics.DrawLine(pen, 7F, 5F, 4F, 10F);
                    graphics.DrawLine(pen, 9F, 5F, 12F, 10F);
                    graphics.DrawLine(pen, 5.5F, 12F, 10.5F, 12F);
                }
                else if (icon == AppVisualIcon.Stop)
                {
                    graphics.FillRectangle(brush, 3.5F, 3.5F, 9F, 9F);
                }
                else if (icon == AppVisualIcon.Info)
                {
                    graphics.DrawEllipse(pen, 1.5F, 1.5F, 13F, 13F);
                    graphics.FillEllipse(brush, 7F, 4F, 2F, 2F);
                    graphics.DrawLine(pen, 8F, 7.5F, 8F, 12F);
                }
                else if (icon == AppVisualIcon.Lock)
                {
                    graphics.DrawRectangle(pen, 3F, 7F, 10F, 7F);
                    graphics.DrawArc(pen, 5F, 1.5F, 6F, 9F, 180F, 180F);
                }
                else
                {
                    graphics.DrawEllipse(pen, 4.5F, 4.5F, 7F, 7F);
                    for (int i = 0; i < 8; i++)
                    {
                        double angle = i * Math.PI / 4D;
                        float x1 = 8F + (float)Math.Cos(angle) * 5F;
                        float y1 = 8F + (float)Math.Sin(angle) * 5F;
                        float x2 = 8F + (float)Math.Cos(angle) * 7F;
                        float y2 = 8F + (float)Math.Sin(angle) * 7F;
                        graphics.DrawLine(pen, x1, y1, x2, y2);
                    }
                }
            }

            return bitmap;
        }

        public static void Apply(Form form)
        {
            if (form == null)
                return;

            AppVisualPalette palette = Current;
            form.Font = BodyFont;
            form.BackColor = palette.WindowBackground;
            form.ForeColor = palette.TextPrimary;
            ApplyWindowChrome(form);
            ApplyControlTree(form, form, palette);
        }

        public static void ApplyWindowChrome(Form form)
        {
            if (form == null || !Current.IsDark)
                return;

            try
            {
                int enabled = 1;
                IntPtr handle = form.Handle;
                if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
            }
            catch
            {
            }
        }

        public static void ApplyProgressBarColors(ProgressBar progressBar)
        {
            if (progressBar == null)
                return;

            try
            {
                IntPtr handle = progressBar.Handle;
                SetWindowTheme(handle, string.Empty, string.Empty);
                SendMessage(handle, ProgressSetBackgroundColor, IntPtr.Zero, (IntPtr)ColorTranslator.ToWin32(Current.SurfaceMuted));
                SendMessage(handle, ProgressSetBarColor, IntPtr.Zero, (IntPtr)ColorTranslator.ToWin32(Current.Accent));
            }
            catch
            {
            }
        }

        private static void ApplyControlTree(Control parent, Form form, AppVisualPalette palette)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is Button button)
                    StyleDialogButton(button, form, palette);
                else if (control is DataGridView grid)
                    StyleGrid(grid, palette);
                else if (control is TextBoxBase textBox)
                    StyleInput(textBox, palette);
                else if (control is ComboBox comboBox)
                    StyleComboBox(comboBox, palette);
                else if (control is NumericUpDown numericUpDown)
                    StyleInput(numericUpDown, palette);
                else if (control is DateTimePicker dateTimePicker)
                    StyleInput(dateTimePicker, palette);
                else if (control is CheckedListBox checkedListBox)
                    StyleListControl(checkedListBox, palette);
                else if (control is ListBox listBox)
                    StyleListControl(listBox, palette);
                else if (control is ListView listView)
                    StyleListView(listView, palette);
                else if (control is TreeView treeView)
                    StyleTreeView(treeView, palette);
                else if (control is LinkLabel linkLabel)
                    StyleLink(linkLabel, palette);
                else if (control is Label label)
                    StyleLabel(label, palette);
                else if (control is GroupBox groupBox)
                    StyleGroupBox(groupBox, palette);
                else if (control is TabControl tabControl)
                    StyleTabControl(tabControl, palette);
                else if (control is TabPage tabPage)
                    StyleTabPage(tabPage, palette);
                else if (control is CheckBox checkBox)
                    StyleToggle(checkBox, palette);
                else if (control is RadioButton radioButton)
                    StyleToggle(radioButton, palette);
                else if (control is ProgressBar progressBar)
                    StyleProgressBar(progressBar, palette);
                else if (control is MenuStrip menuStrip)
                    StyleToolStrip(menuStrip, palette);
                else if (control is StatusStrip statusStrip)
                    StyleToolStrip(statusStrip, palette);
                else if (control is ToolStrip toolStrip)
                    StyleToolStrip(toolStrip, palette);
                else if (control is Panel panel)
                    StylePanel(panel, palette);

                if (control.ContextMenuStrip != null)
                    StyleContextMenu(control.ContextMenuStrip, palette);

                if (control.HasChildren)
                    ApplyControlTree(control, form, palette);
            }

            if (parent.ContextMenuStrip != null)
                StyleContextMenu(parent.ContextMenuStrip, palette);
        }

        private static void StyleDialogButton(Button button, Form form, AppVisualPalette palette)
        {
            bool isPrimary = ReferenceEquals(form.AcceptButton, button);
            bool isDanger = ContainsAny(button.Text, "delete", "remove", "stop", "disable");
            Color background = isDanger ? palette.Danger : isPrimary ? palette.Accent : palette.Surface;
            Color foreground = isDanger || isPrimary ? Color.White : palette.TextPrimary;
            Color border = isDanger ? palette.Danger : isPrimary ? palette.Accent : palette.BorderStrong;

            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = background;
            button.ForeColor = foreground;
            button.Font = ButtonFont;
            button.FlatAppearance.BorderColor = border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = isDanger ? Color.FromArgb(214, 58, 43) : isPrimary ? palette.AccentHover : palette.SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = isDanger ? Color.FromArgb(164, 35, 23) : isPrimary ? palette.AccentPressed : palette.SurfacePressed;
            button.FlatAppearance.CheckedBackColor = palette.SurfacePressed;
            button.Cursor = button.Enabled ? Cursors.Hand : Cursors.Default;
        }

        private static void StyleGrid(DataGridView grid, AppVisualPalette palette)
        {
            grid.EnableHeadersVisualStyles = false;
            grid.BackgroundColor = palette.Surface;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.GridColor = palette.GridLine;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = palette.GridHeader;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.TextPrimary;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.GridHeader;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.TextPrimary;
            grid.ColumnHeadersDefaultCellStyle.Font = GridHeaderFont;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
            grid.DefaultCellStyle.BackColor = palette.Surface;
            grid.DefaultCellStyle.ForeColor = palette.TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
            grid.DefaultCellStyle.SelectionForeColor = palette.SelectionText;
            grid.DefaultCellStyle.Font = GridFont;
            grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            grid.AlternatingRowsDefaultCellStyle.BackColor = palette.SurfaceAlternate;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = palette.TextPrimary;
            if (grid.RowTemplate.Height < 28)
                grid.RowTemplate.Height = 28;
            if (grid.ColumnHeadersHeight < 32)
                grid.ColumnHeadersHeight = 32;
        }

        private static void StyleInput(Control control, AppVisualPalette palette)
        {
            control.BackColor = palette.InputBackground;
            control.ForeColor = palette.TextPrimary;
            control.Font = BodyFont;
        }

        private static void StyleComboBox(ComboBox comboBox, AppVisualPalette palette)
        {
            comboBox.BackColor = palette.InputBackground;
            comboBox.ForeColor = palette.TextPrimary;
            comboBox.Font = BodyFont;
            if (comboBox.FlatStyle != FlatStyle.System)
                comboBox.FlatStyle = FlatStyle.Flat;
        }

        private static void StyleListControl(ListControl listControl, AppVisualPalette palette)
        {
            listControl.BackColor = palette.InputBackground;
            listControl.ForeColor = palette.TextPrimary;
            listControl.Font = BodyFont;
        }

        private static void StyleListView(ListView listView, AppVisualPalette palette)
        {
            listView.BackColor = palette.InputBackground;
            listView.ForeColor = palette.TextPrimary;
            listView.Font = BodyFont;
            listView.BorderStyle = BorderStyle.FixedSingle;
        }

        private static void StyleTreeView(TreeView treeView, AppVisualPalette palette)
        {
            treeView.BackColor = palette.InputBackground;
            treeView.ForeColor = palette.TextPrimary;
            treeView.Font = BodyFont;
            treeView.BorderStyle = BorderStyle.FixedSingle;
        }

        private static void StyleLink(LinkLabel linkLabel, AppVisualPalette palette)
        {
            linkLabel.Font = BodyFont;
            linkLabel.LinkColor = palette.AccentText;
            linkLabel.ActiveLinkColor = palette.AccentPressed;
            linkLabel.VisitedLinkColor = palette.AccentText;
            linkLabel.BackColor = Color.Transparent;
        }

        private static void StyleLabel(Label label, AppVisualPalette palette)
        {
            label.BackColor = Color.Transparent;
            if (label.Parent != null && label.Parent.BackColor == palette.Header)
            {
                label.ForeColor = label.ForeColor == Color.White ? Color.White : palette.HeaderSecondaryText;
                return;
            }
            if (!IsSemanticColor(label.ForeColor))
                label.ForeColor = palette.TextPrimary;
        }

        private static void StyleGroupBox(GroupBox groupBox, AppVisualPalette palette)
        {
            groupBox.BackColor = palette.Surface;
            groupBox.ForeColor = palette.TextPrimary;
            groupBox.Font = ButtonFont;
        }

        private static void StyleTabControl(TabControl tabControl, AppVisualPalette palette)
        {
            tabControl.Font = BodyFont;
            tabControl.BackColor = palette.WindowBackground;
            tabControl.ForeColor = palette.TextPrimary;
        }

        private static void StyleTabPage(TabPage tabPage, AppVisualPalette palette)
        {
            tabPage.UseVisualStyleBackColor = false;
            tabPage.BackColor = palette.Surface;
            tabPage.ForeColor = palette.TextPrimary;
        }

        private static void StyleToggle(Control control, AppVisualPalette palette)
        {
            control.BackColor = Color.Transparent;
            control.ForeColor = palette.TextPrimary;
            control.Font = BodyFont;
        }

        private static void StyleProgressBar(ProgressBar progressBar, AppVisualPalette palette)
        {
            progressBar.BackColor = palette.SurfaceMuted;
            progressBar.ForeColor = palette.Accent;
            ApplyProgressBarColors(progressBar);
        }

        private static void StylePanel(Panel panel, AppVisualPalette palette)
        {
            if (panel.BackColor == SystemColors.Control || panel.BackColor == Color.Transparent)
                panel.BackColor = palette.Surface;
            else if (panel.BackColor.B > panel.BackColor.R + 20 && panel.BackColor.B > panel.BackColor.G)
                panel.BackColor = palette.Header;
        }

        private static void StyleToolStrip(ToolStrip toolStrip, AppVisualPalette palette)
        {
            toolStrip.Font = BodyFont;
            toolStrip.BackColor = toolStrip is MenuStrip ? palette.Header : palette.SurfaceMuted;
            toolStrip.ForeColor = toolStrip is MenuStrip ? Color.White : palette.TextPrimary;
            toolStrip.Renderer = new ModernMenuRenderer(palette);
            StyleToolStripItems(toolStrip.Items, palette, toolStrip is MenuStrip);
        }

        private static void StyleContextMenu(ContextMenuStrip menu, AppVisualPalette palette)
        {
            StyleDropDown(menu, palette);
        }

        private static void StyleDropDown(ToolStripDropDown menu, AppVisualPalette palette)
        {
            menu.BackColor = palette.Surface;
            menu.ForeColor = palette.TextPrimary;
            menu.Font = MenuFont;
            menu.Renderer = new ModernMenuRenderer(palette);
            menu.Padding = new Padding(6);
            StyleToolStripItems(menu.Items, palette, false);
        }

        private static void StyleToolStripItems(ToolStripItemCollection items, AppVisualPalette palette, bool header)
        {
            foreach (ToolStripItem item in items)
            {
                item.BackColor = header ? palette.Header : palette.Surface;
                item.ForeColor = header ? Color.White : palette.TextPrimary;
                item.Font = header ? MenuHeaderFont : MenuFont;
                if (item is ToolStripMenuItem menuItem)
                    StyleDropDown(menuItem.DropDown, palette);
            }
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            string normalized = (value ?? string.Empty).Replace("&", string.Empty).Trim();
            foreach (string term in terms)
            {
                if (normalized.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool IsSemanticColor(Color color)
        {
            return color.R > color.G + 45 && color.R > color.B + 35
                || color.G > color.R + 35 && color.G > color.B + 20;
        }
    }

    public class ModernMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly AppVisualPalette palette;

        public ModernMenuRenderer(AppVisualPalette palette)
            : base(new ModernMenuColorTable(palette))
        {
            this.palette = palette;
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is MenuStrip)
            {
                using (SolidBrush brush = new SolidBrush(palette.Header))
                    e.Graphics.FillRectangle(brush, e.AffectedBounds);
                return;
            }

            using (SolidBrush brush = new SolidBrush(palette.Surface))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                Rectangle bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                using (Pen pen = new Pen(palette.Border))
                    e.Graphics.DrawRectangle(pen, bounds);
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Rectangle bounds = new Rectangle(Point.Empty, e.Item.Size);

            if (e.ToolStrip is MenuStrip)
            {
                if (e.Item.Selected || (e.Item is ToolStripMenuItem menuItem && menuItem.DropDown.Visible))
                {
                    using (SolidBrush brush = new SolidBrush(palette.Accent))
                        e.Graphics.FillRectangle(brush, bounds);
                }
                else
                {
                    using (SolidBrush brush = new SolidBrush(palette.Header))
                        e.Graphics.FillRectangle(brush, bounds);
                }

                return;
            }

            using (SolidBrush brush = new SolidBrush(e.Item.Selected ? palette.Accent : palette.Surface))
                e.Graphics.FillRectangle(brush, bounds);

            if (e.Item.Selected)
            {
                using (SolidBrush accentBrush = new SolidBrush(palette.AccentText))
                    e.Graphics.FillRectangle(accentBrush, new Rectangle(0, 0, 4, bounds.Height));
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.ToolStrip is MenuStrip || e.Item.Selected)
                e.TextColor = Color.White;
            else
                e.TextColor = palette.TextPrimary;

            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen pen = new Pen(palette.Border))
                e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Selected ? Color.White : palette.TextSecondary;
            base.OnRenderArrow(e);
        }
    }

    public class ModernMenuColorTable : ProfessionalColorTable
    {
        private readonly AppVisualPalette palette;

        public ModernMenuColorTable(AppVisualPalette palette)
        {
            this.palette = palette;
        }

        public override Color MenuItemSelected => palette.SurfaceHover;
        public override Color MenuItemSelectedGradientBegin => palette.SurfaceHover;
        public override Color MenuItemSelectedGradientEnd => palette.SurfaceHover;
        public override Color MenuItemBorder => palette.Accent;
        public override Color MenuBorder => palette.Border;
        public override Color ToolStripDropDownBackground => palette.Surface;
        public override Color ImageMarginGradientBegin => palette.SurfaceMuted;
        public override Color ImageMarginGradientMiddle => palette.SurfaceMuted;
        public override Color ImageMarginGradientEnd => palette.Surface;
        public override Color SeparatorDark => palette.Border;
        public override Color SeparatorLight => palette.Surface;
        public override Color CheckBackground => palette.SurfaceHover;
        public override Color CheckSelectedBackground => palette.SurfaceHover;
        public override Color CheckPressedBackground => palette.SurfacePressed;
        public override Color ButtonSelectedBorder => palette.Accent;
        public override Color ButtonSelectedGradientBegin => palette.Header;
        public override Color ButtonSelectedGradientEnd => palette.Header;
    }

    public class ModernAboutDialog : Form
    {
        private Bitmap logoBitmap;

        public ModernAboutDialog(string productName, string version, string buildDate, string copyright)
        {
            Color navy = Color.FromArgb(28, 61, 108);
            Color accentBlue = Color.FromArgb(0, 116, 181);

            Text = "About";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(430, 285);
            BackColor = Color.FromArgb(245, 248, 252);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            logoBitmap = LoadApplicationIconBitmap();

            Panel header = new Panel
            {
                BackColor = navy,
                Dock = DockStyle.Top,
                Height = 82
            };

            PictureBox logo = new PictureBox
            {
                Image = logoBitmap,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Left = 22,
                Top = 18,
                Width = 46,
                Height = 46
            };

            Label title = new Label
            {
                Text = string.IsNullOrWhiteSpace(productName) ? "Universal Scanner" : productName,
                AutoSize = false,
                Left = 82,
                Top = 18,
                Width = 300,
                Height = 30,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold)
            };

            Label subtitle = new Label
            {
                Text = "Discover - Analyze - Identify",
                AutoSize = false,
                Left = 84,
                Top = 48,
                Width = 300,
                Height = 22,
                ForeColor = Color.FromArgb(211, 224, 239),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            header.Controls.Add(logo);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            Label versionLabel = new Label
            {
                Text = "Version " + version,
                Left = 28,
                Top = 110,
                Width = 370,
                Height = 24,
                ForeColor = Color.FromArgb(29, 39, 53),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };

            Label buildLabel = new Label
            {
                Text = "Build date: " + buildDate,
                Left = 28,
                Top = 139,
                Width = 370,
                Height = 22,
                ForeColor = Color.FromArgb(63, 77, 94)
            };

            Label copyrightLabel = new Label
            {
                Text = "Copyright: " + copyright,
                Left = 28,
                Top = 164,
                Width = 370,
                Height = 22,
                ForeColor = Color.FromArgb(63, 77, 94)
            };

            Label licenseLabel = new Label
            {
                Text = "Program under GNU Lesser General Public License 3.0",
                Left = 28,
                Top = 193,
                Width = 370,
                Height = 22,
                ForeColor = Color.FromArgb(63, 77, 94)
            };

            LinkLabel repositoryLink = new LinkLabel
            {
                Text = "GitHub repository",
                Left = 28,
                Top = 222,
                Width = 220,
                Height = 22,
                LinkColor = accentBlue,
                ActiveLinkColor = navy,
                VisitedLinkColor = accentBlue
            };
            repositoryLink.LinkClicked += (sender, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo("https://github.com/hellsun77/UniversalScanner-2")
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not open browser:\r\n" + ex.Message, "Open Browser", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            Button okButton = new Button
            {
                Text = "OK",
                Left = 322,
                Top = 238,
                Width = 78,
                Height = 30,
                DialogResult = DialogResult.OK,
                BackColor = accentBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                UseVisualStyleBackColor = false
            };
            okButton.FlatAppearance.BorderSize = 0;

            Controls.Add(header);
            Controls.Add(versionLabel);
            Controls.Add(buildLabel);
            Controls.Add(copyrightLabel);
            Controls.Add(licenseLabel);
            Controls.Add(repositoryLink);
            Controls.Add(okButton);

            AcceptButton = okButton;
            AppVisualTheme.Apply(this);
        }

        private static Bitmap LoadApplicationIconBitmap()
        {
            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                {
                    return icon == null ? null : icon.ToBitmap();
                }
            }
            catch
            {
                return null;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (logoBitmap != null)
            {
                logoBitmap.Dispose();
                logoBitmap = null;
            }

            base.OnFormClosed(e);
        }
    }

    public class PdfDeviceExportRow
    {
        public string IPAddress { get; set; }
        public string MACAddress { get; set; }
        public string Vendor { get; set; }
    }

    public static class SimplePdfTableExporter
    {
        private const int PageWidth = 612;
        private const int PageHeight = 792;
        private const int LandscapePageWidth = 1600;
        private const int LandscapePageHeight = 792;
        private const int Margin = 54;
        private const int RowHeight = 24;
        private const int HeaderHeight = 28;

        public static void ExportIpMacTable(string filePath, IList<PdfDeviceExportRow> rows)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is required.", nameof(filePath));

            if (rows == null || rows.Count == 0)
                throw new ArgumentException("At least one device row is required.", nameof(rows));

            string tableName = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(tableName))
                tableName = "Selected Device IP/MAC Table";

            string vendorSummary = BuildVendorSummary(rows);
            List<string> pageStreams = BuildPageStreams(rows, tableName, vendorSummary);
            List<byte[]> objects = new List<byte[]>();

            int catalogObject = 1;
            int pagesObject = 2;
            int fontObject = 3;
            int firstPageObject = 4;
            int firstContentObject = firstPageObject + pageStreams.Count;

            objects.Add(Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));

            StringBuilder kids = new StringBuilder();
            for (int i = 0; i < pageStreams.Count; i++)
                kids.Append(firstPageObject + i).Append(" 0 R ");

            objects.Add(Encoding.ASCII.GetBytes("<< /Type /Pages /Kids [" + kids + "] /Count " + pageStreams.Count + " >>"));
            objects.Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));

            for (int i = 0; i < pageStreams.Count; i++)
            {
                int contentObject = firstContentObject + i;
                string pageObject =
                    "<< /Type /Page /Parent " + pagesObject + " 0 R /MediaBox [0 0 " + PageWidth + " " + PageHeight + "] " +
                    "/Resources << /Font << /F1 " + fontObject + " 0 R >> >> /Contents " + contentObject + " 0 R >>";
                objects.Add(Encoding.ASCII.GetBytes(pageObject));
            }

            foreach (string stream in pageStreams)
            {
                byte[] streamBytes = Encoding.ASCII.GetBytes(stream);
                string header = "<< /Length " + streamBytes.Length + " >>\nstream\n";
                string footer = "\nendstream";
                byte[] objectBytes = CombineBytes(Encoding.ASCII.GetBytes(header), streamBytes, Encoding.ASCII.GetBytes(footer));
                objects.Add(objectBytes);
            }

            WritePdf(filePath, objects, catalogObject);
        }

        public static void ExportFullDeviceList(string filePath, IList<string> headers, IList<List<string>> rows)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is required.", nameof(filePath));

            if (headers == null || headers.Count == 0)
                throw new ArgumentException("At least one column is required.", nameof(headers));

            if (rows == null || rows.Count == 0)
                throw new ArgumentException("At least one row is required.", nameof(rows));

            string tableName = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(tableName))
                tableName = "Device List";

            List<string> pageStreams = BuildFullListPageStreams(headers, rows, tableName);
            List<byte[]> objects = new List<byte[]>();

            int catalogObject = 1;
            int pagesObject = 2;
            int fontObject = 3;
            int firstPageObject = 4;
            int firstContentObject = firstPageObject + pageStreams.Count;

            objects.Add(Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));

            StringBuilder kids = new StringBuilder();
            for (int i = 0; i < pageStreams.Count; i++)
                kids.Append(firstPageObject + i).Append(" 0 R ");

            objects.Add(Encoding.ASCII.GetBytes("<< /Type /Pages /Kids [" + kids + "] /Count " + pageStreams.Count + " >>"));
            objects.Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));

            for (int i = 0; i < pageStreams.Count; i++)
            {
                int contentObject = firstContentObject + i;
                string pageObject =
                    "<< /Type /Page /Parent " + pagesObject + " 0 R /MediaBox [0 0 " + LandscapePageWidth + " " + LandscapePageHeight + "] " +
                    "/Resources << /Font << /F1 " + fontObject + " 0 R >> >> /Contents " + contentObject + " 0 R >>";
                objects.Add(Encoding.ASCII.GetBytes(pageObject));
            }

            foreach (string stream in pageStreams)
            {
                byte[] streamBytes = Encoding.ASCII.GetBytes(stream);
                string header = "<< /Length " + streamBytes.Length + " >>\nstream\n";
                string footer = "\nendstream";
                byte[] objectBytes = CombineBytes(Encoding.ASCII.GetBytes(header), streamBytes, Encoding.ASCII.GetBytes(footer));
                objects.Add(objectBytes);
            }

            WritePdf(filePath, objects, catalogObject);
        }

        private static List<string> BuildFullListPageStreams(IList<string> headers, IList<List<string>> rows, string tableName)
        {
            List<string> pageStreams = new List<string>();
            int fullRowHeight = 22;
            int usableRowsPerPage = Math.Max(1, (LandscapePageHeight - 175) / fullRowHeight);
            int totalPages = (int)Math.Ceiling(rows.Count / (double)usableRowsPerPage);

            for (int pageIndex = 0; pageIndex < totalPages; pageIndex++)
            {
                int start = pageIndex * usableRowsPerPage;
                int count = Math.Min(usableRowsPerPage, rows.Count - start);
                pageStreams.Add(BuildFullListSinglePage(headers, rows, start, count, pageIndex + 1, totalPages, tableName, fullRowHeight));
            }

            return pageStreams;
        }

        private static string BuildFullListSinglePage(IList<string> headers, IList<List<string>> rows, int start, int count, int pageNumber, int totalPages, string tableName, int fullRowHeight)
        {
            StringBuilder stream = new StringBuilder();
            int left = 28;
            int tableTop = LandscapePageHeight - 150;
            int tableWidth = LandscapePageWidth - 56;
            int[] widths = BuildFullListColumnWidths(headers, tableWidth);

            AddFilledRect(stream, 0, LandscapePageHeight - 86, LandscapePageWidth, 86, 0.11, 0.24, 0.42);
            AddFilledRect(stream, 0, LandscapePageHeight - 89, LandscapePageWidth, 3, 0.00, 0.45, 0.71);
            AddRadarLogo(stream, left, LandscapePageHeight - 62);
            AddWhiteText(stream, "Universal Scanner", left + 56, LandscapePageHeight - 34, 17);
            AddLightText(stream, "Discover - Analyze - Identify", left + 58, LandscapePageHeight - 55, 9);
            AddLightText(stream, "Anan Awwad", left + 58, LandscapePageHeight - 72, 8);
            AddLightText(stream, "Page " + pageNumber + " of " + totalPages, LandscapePageWidth - 120, LandscapePageHeight - 34, 8);

            AddText(stream, Truncate(tableName, 80), left, LandscapePageHeight - 112, 15, true);
            AddText(stream, "Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"), left, LandscapePageHeight - 132, 8, false);

            AddFilledRect(stream, left, tableTop - HeaderHeight, tableWidth, HeaderHeight, 0.11, 0.24, 0.42);
            AddRect(stream, left, tableTop - HeaderHeight, tableWidth, HeaderHeight, 0.11, 0.24, 0.42);

            int x = left;
            for (int i = 0; i < headers.Count; i++)
            {
                if (i > 0)
                    AddLine(stream, x, tableTop, x, tableTop - HeaderHeight, 0.28, 0.55, 0.78);

                AddWhiteText(stream, headers[i] ?? "", x + 4, tableTop - 18, 7);
                x += widths[i];
            }

            int y = tableTop - HeaderHeight;
            for (int rowIndex = 0; rowIndex < count; rowIndex++)
            {
                y -= fullRowHeight;
                AddFilledRect(stream, left, y, tableWidth, fullRowHeight, rowIndex % 2 == 0 ? 0.96 : 1.00, rowIndex % 2 == 0 ? 0.985 : 1.00, 1.00);
                AddRect(stream, left, y, tableWidth, fullRowHeight, 0.86, 0.90, 0.94);

                x = left;
                List<string> row = rows[start + rowIndex];
                for (int col = 0; col < headers.Count; col++)
                {
                    if (col > 0)
                        AddLine(stream, x, y + fullRowHeight, x, y, 0.86, 0.90, 0.94);

                    string value = col < row.Count ? row[col] : "";
                    AddText(stream, value, x + 4, y + 7, 6, false);
                    x += widths[col];
                }
            }

            return stream.ToString();
        }

        private static int[] BuildFullListColumnWidths(IList<string> headers, int totalWidth)
        {
            Dictionary<string, int> preferred = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "Vendor", 130 },
                { "Protocol", 100 },
                { "IP address", 120 },
                { "Ping", 70 },
                { "Activation", 95 },
                { "MAC Address", 150 },
                { "OUI Vendor", 230 },
                { "Model", 200 },
                { "ONVIF Profile", 135 },
                { "Unique ID", 390 }
            };

            int[] widths = headers.Select(header => preferred.ContainsKey(header) ? preferred[header] : 70).ToArray();
            int sum = widths.Sum();

            if (sum == totalWidth)
                return widths;

            double scale = totalWidth / (double)sum;
            int adjustedSum = 0;
            for (int i = 0; i < widths.Length; i++)
            {
                widths[i] = Math.Max(35, (int)Math.Floor(widths[i] * scale));
                adjustedSum += widths[i];
            }

            if (widths.Length > 0)
                widths[widths.Length - 1] += totalWidth - adjustedSum;

            return widths;
        }

        private static List<string> BuildPageStreams(IList<PdfDeviceExportRow> rows, string tableName, string vendorSummary)
        {
            List<string> pageStreams = new List<string>();
            int usableRowsPerPage = Math.Max(1, (PageHeight - 250) / RowHeight);
            int totalPages = (int)Math.Ceiling(rows.Count / (double)usableRowsPerPage);

            for (int pageIndex = 0; pageIndex < totalPages; pageIndex++)
            {
                int start = pageIndex * usableRowsPerPage;
                int count = Math.Min(usableRowsPerPage, rows.Count - start);
                pageStreams.Add(BuildSinglePageStream(rows, start, count, pageIndex + 1, totalPages, tableName, vendorSummary));
            }

            return pageStreams;
        }

        private static string BuildSinglePageStream(IList<PdfDeviceExportRow> rows, int start, int count, int pageNumber, int totalPages, string tableName, string vendorSummary)
        {
            StringBuilder stream = new StringBuilder();
            int tableLeft = Margin;
            int tableTop = PageHeight - 220;
            int numberWidth = 58;
            int ipWidth = 150;
            int macWidth = 165;
            int vendorWidth = 127;
            int tableWidth = numberWidth + ipWidth + macWidth + vendorWidth;

            AddFilledRect(stream, 0, PageHeight - 112, PageWidth, 112, 0.11, 0.24, 0.42);
            AddFilledRect(stream, 0, PageHeight - 115, PageWidth, 3, 0.00, 0.45, 0.71);
            AddRadarLogo(stream, Margin, PageHeight - 76);
            AddWhiteText(stream, "Universal Scanner", Margin + 56, PageHeight - 43, 19);
            AddLightText(stream, "Discover - Analyze - Identify", Margin + 58, PageHeight - 66, 10);
            AddLightText(stream, "Anan Awwad", Margin + 58, PageHeight - 85, 9);
            AddLightText(stream, "Page " + pageNumber + " of " + totalPages, PageWidth - 126, PageHeight - 50, 9);

            AddText(stream, Truncate(tableName, 58), Margin, PageHeight - 142, 18, true);
            AddText(stream, "Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Margin, PageHeight - 164, 9, false);
            AddText(stream, Truncate(vendorSummary, 92), Margin, PageHeight - 184, 9, false);

            AddFilledRect(stream, tableLeft, tableTop - HeaderHeight, tableWidth, HeaderHeight, 0.11, 0.24, 0.42);
            AddRect(stream, tableLeft, tableTop - HeaderHeight, tableWidth, HeaderHeight, 0.11, 0.24, 0.42);
            AddLine(stream, tableLeft + numberWidth, tableTop, tableLeft + numberWidth, tableTop - HeaderHeight, 0.28, 0.55, 0.78);
            AddLine(stream, tableLeft + numberWidth + ipWidth, tableTop, tableLeft + numberWidth + ipWidth, tableTop - HeaderHeight, 0.28, 0.55, 0.78);
            AddLine(stream, tableLeft + numberWidth + ipWidth + macWidth, tableTop, tableLeft + numberWidth + ipWidth + macWidth, tableTop - HeaderHeight, 0.28, 0.55, 0.78);
            AddWhiteText(stream, "", tableLeft + 16, tableTop - 19, 10);
            AddWhiteText(stream, "IP Address", tableLeft + numberWidth + 12, tableTop - 19, 10);
            AddWhiteText(stream, "MAC Address", tableLeft + numberWidth + ipWidth + 12, tableTop - 19, 10);
            AddWhiteText(stream, "Vendor", tableLeft + numberWidth + ipWidth + macWidth + 12, tableTop - 19, 10);

            int y = tableTop - HeaderHeight;
            for (int i = 0; i < count; i++)
            {
                PdfDeviceExportRow row = rows[start + i];
                y -= RowHeight;

                if (i % 2 == 0)
                    AddFilledRect(stream, tableLeft, y, tableWidth, RowHeight, 0.96, 0.985, 1.00);
                else
                    AddFilledRect(stream, tableLeft, y, tableWidth, RowHeight, 1.00, 1.00, 1.00);

                AddRect(stream, tableLeft, y, tableWidth, RowHeight, 0.86, 0.90, 0.94);
                AddLine(stream, tableLeft + numberWidth, y + RowHeight, tableLeft + numberWidth, y, 0.86, 0.90, 0.94);
                AddLine(stream, tableLeft + numberWidth + ipWidth, y + RowHeight, tableLeft + numberWidth + ipWidth, y, 0.86, 0.90, 0.94);
                AddLine(stream, tableLeft + numberWidth + ipWidth + macWidth, y + RowHeight, tableLeft + numberWidth + ipWidth + macWidth, y, 0.86, 0.90, 0.94);
                AddText(stream, (start + i + 1).ToString(), tableLeft + 16, y + 8, 9, false);
                AddText(stream, Truncate(row.IPAddress, 22), tableLeft + numberWidth + 10, y + 8, 9, false);
                AddText(stream, Truncate(row.MACAddress, 26), tableLeft + numberWidth + ipWidth + 10, y + 8, 9, false);
                AddText(stream, Truncate(row.Vendor, 18), tableLeft + numberWidth + ipWidth + macWidth + 10, y + 8, 9, false);
            }

            return stream.ToString();
        }

        private static string BuildVendorSummary(IList<PdfDeviceExportRow> rows)
        {
            var groups = rows
                .Select(row => string.IsNullOrWhiteSpace(row.Vendor) ? "Unknown" : row.Vendor.Trim())
                .GroupBy(vendor => vendor, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Key + ": " + group.Count() + " " + (group.Count() == 1 ? "Device" : "Devices"));

            return string.Join(" | ", groups);
        }

        private static void AddRadarLogo(StringBuilder stream, int x, int y)
        {
            int size = 38;
            int centerX = x + size / 2;
            int centerY = y + size / 2;

            AddFilledCircle(stream, centerX, centerY, 19, 0.39, 0.68, 0.48);
            AddCircle(stream, centerX, centerY, 19, 0.73, 0.88, 0.76, 0.8);
            AddCircle(stream, centerX, centerY, 11, 0.73, 0.88, 0.76, 0.6);
            AddLine(stream, centerX, centerY, centerX + 15, centerY + 8, 0.92, 1.00, 0.94);
            AddLine(stream, centerX, centerY - 18, centerX, centerY + 18, 0.73, 0.88, 0.76);
            AddLine(stream, centerX - 18, centerY, centerX + 18, centerY, 0.73, 0.88, 0.76);
            AddFilledCircle(stream, centerX, centerY, 3, 0.93, 1.00, 0.94);
        }

        private static void AddWhiteText(StringBuilder stream, string text, int x, int y, int size)
        {
            stream.Append("BT /F1 ").Append(size).Append(" Tf 1 1 1 rg ")
                .Append(x).Append(' ').Append(y).Append(" Td (").Append(EscapePdfText(text)).Append(") Tj ET\n");
        }

        private static void AddLightText(StringBuilder stream, string text, int x, int y, int size)
        {
            stream.Append("BT /F1 ").Append(size).Append(" Tf 0.83 0.88 0.94 rg ")
                .Append(x).Append(' ').Append(y).Append(" Td (").Append(EscapePdfText(text)).Append(") Tj ET\n");
        }

        private static void AddText(StringBuilder stream, string text, int x, int y, int size, bool bold)
        {
            stream.Append("BT /F1 ").Append(size).Append(" Tf ");
            if (bold)
                stream.Append("0.11 0.24 0.42 rg ");
            else
                stream.Append("0.18 0.24 0.32 rg ");
            stream.Append(x).Append(' ').Append(y).Append(" Td (").Append(EscapePdfText(text)).Append(") Tj ET\n");
        }

        private static void AddFilledRect(StringBuilder stream, int x, int y, int width, int height, double r, double g, double b)
        {
            stream.Append(FormatColor(r, g, b)).Append(" rg ")
                .Append(x).Append(' ').Append(y).Append(' ').Append(width).Append(' ').Append(height).Append(" re f\n");
        }

        private static void AddFilledCircle(StringBuilder stream, int centerX, int centerY, int radius, double r, double g, double b)
        {
            AddCirclePath(stream, centerX, centerY, radius);
            stream.Append(FormatColor(r, g, b)).Append(" rg f\n");
        }

        private static void AddCircle(StringBuilder stream, int centerX, int centerY, int radius, double r, double g, double b, double width)
        {
            AddCirclePath(stream, centerX, centerY, radius);
            stream.Append(FormatColor(r, g, b)).Append(" RG ")
                .Append(width.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" w S\n");
        }

        private static void AddCirclePath(StringBuilder stream, int centerX, int centerY, int radius)
        {
            double c = radius * 0.5522847498;
            double x0 = centerX - radius;
            double x1 = centerX - c;
            double x2 = centerX + c;
            double x3 = centerX + radius;
            double y0 = centerY - radius;
            double y1 = centerY - c;
            double y2 = centerY + c;
            double y3 = centerY + radius;

            stream.Append(FormatNumber(x0)).Append(' ').Append(FormatNumber(centerY)).Append(" m ")
                .Append(FormatNumber(x0)).Append(' ').Append(FormatNumber(y2)).Append(' ')
                .Append(FormatNumber(x1)).Append(' ').Append(FormatNumber(y3)).Append(' ')
                .Append(FormatNumber(centerX)).Append(' ').Append(FormatNumber(y3)).Append(" c ")
                .Append(FormatNumber(x2)).Append(' ').Append(FormatNumber(y3)).Append(' ')
                .Append(FormatNumber(x3)).Append(' ').Append(FormatNumber(y2)).Append(' ')
                .Append(FormatNumber(x3)).Append(' ').Append(FormatNumber(centerY)).Append(" c ")
                .Append(FormatNumber(x3)).Append(' ').Append(FormatNumber(y1)).Append(' ')
                .Append(FormatNumber(x2)).Append(' ').Append(FormatNumber(y0)).Append(' ')
                .Append(FormatNumber(centerX)).Append(' ').Append(FormatNumber(y0)).Append(" c ")
                .Append(FormatNumber(x1)).Append(' ').Append(FormatNumber(y0)).Append(' ')
                .Append(FormatNumber(x0)).Append(' ').Append(FormatNumber(y1)).Append(' ')
                .Append(FormatNumber(x0)).Append(' ').Append(FormatNumber(centerY)).Append(" c\n");
        }

        private static void AddRect(StringBuilder stream, int x, int y, int width, int height, double r, double g, double b)
        {
            stream.Append(FormatColor(r, g, b)).Append(" RG 0.6 w ")
                .Append(x).Append(' ').Append(y).Append(' ').Append(width).Append(' ').Append(height).Append(" re S\n");
        }

        private static void AddLine(StringBuilder stream, int x1, int y1, int x2, int y2, double r, double g, double b)
        {
            stream.Append(FormatColor(r, g, b)).Append(" RG 0.6 w ")
                .Append(x1).Append(' ').Append(y1).Append(" m ")
                .Append(x2).Append(' ').Append(y2).Append(" l S\n");
        }

        private static string FormatColor(double r, double g, double b)
        {
            return r.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " " +
                   g.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " " +
                   b.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string EscapePdfText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return value
                .Replace("\\", "\\\\")
                .Replace("(", "\\(")
                .Replace(")", "\\)");
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string clean = value.Trim();
            if (clean.Length <= maxLength)
                return clean;

            return clean.Substring(0, maxLength - 3) + "...";
        }

        private static void WritePdf(string filePath, List<byte[]> objects, int catalogObject)
        {
            using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                WriteAscii(fileStream, "%PDF-1.4\n");

                List<long> offsets = new List<long>();
                for (int i = 0; i < objects.Count; i++)
                {
                    offsets.Add(fileStream.Position);
                    WriteAscii(fileStream, (i + 1) + " 0 obj\n");
                    fileStream.Write(objects[i], 0, objects[i].Length);
                    WriteAscii(fileStream, "\nendobj\n");
                }

                long xrefOffset = fileStream.Position;
                WriteAscii(fileStream, "xref\n0 " + (objects.Count + 1) + "\n");
                WriteAscii(fileStream, "0000000000 65535 f \n");

                foreach (long offset in offsets)
                    WriteAscii(fileStream, offset.ToString("0000000000") + " 00000 n \n");

                WriteAscii(fileStream,
                    "trailer\n<< /Size " + (objects.Count + 1) + " /Root " + catalogObject + " 0 R >>\n" +
                    "startxref\n" + xrefOffset + "\n%%EOF");
            }
        }

        private static byte[] CombineBytes(params byte[][] chunks)
        {
            int length = chunks.Sum(chunk => chunk.Length);
            byte[] combined = new byte[length];
            int offset = 0;

            foreach (byte[] chunk in chunks)
            {
                Buffer.BlockCopy(chunk, 0, combined, offset, chunk.Length);
                offset += chunk.Length;
            }

            return combined;
        }

        private static void WriteAscii(Stream stream, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    public class CustomPingDialog : Form
    {
        private NumericUpDown countBox;
        private NumericUpDown timeoutBox;
        private NumericUpDown intervalBox;

        public int PingCount => (int)countBox.Value;
        public int TimeoutMs => (int)timeoutBox.Value;
        public int IntervalMs => (int)intervalBox.Value;

        public CustomPingDialog(string ip)
        {
            Text = "Custom Ping";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(360, 260);

            Label targetLabel = new Label();
            targetLabel.Text = "Target IP";
            targetLabel.Left = 20;
            targetLabel.Top = 20;
            targetLabel.Width = 300;

            TextBox targetBox = new TextBox();
            targetBox.Left = 20;
            targetBox.Top = 42;
            targetBox.Width = 310;
            targetBox.Text = ip;
            targetBox.ReadOnly = true;

            Label countLabel = new Label();
            countLabel.Text = "Number of Echo Requests";
            countLabel.Left = 20;
            countLabel.Top = 78;
            countLabel.Width = 300;

            countBox = new NumericUpDown();
            countBox.Left = 20;
            countBox.Top = 100;
            countBox.Width = 310;
            countBox.Minimum = 1;
            countBox.Maximum = 10000;
            countBox.Value = 25;

            Label timeoutLabel = new Label();
            timeoutLabel.Text = "Timeout (ms)";
            timeoutLabel.Left = 20;
            timeoutLabel.Top = 136;
            timeoutLabel.Width = 300;

            timeoutBox = new NumericUpDown();
            timeoutBox.Left = 20;
            timeoutBox.Top = 158;
            timeoutBox.Width = 145;
            timeoutBox.Minimum = 100;
            timeoutBox.Maximum = 30000;
            timeoutBox.Increment = 100;
            timeoutBox.Value = 1000;

            Label intervalLabel = new Label();
            intervalLabel.Text = "Interval (ms)";
            intervalLabel.Left = 185;
            intervalLabel.Top = 136;
            intervalLabel.Width = 145;

            intervalBox = new NumericUpDown();
            intervalBox.Left = 185;
            intervalBox.Top = 158;
            intervalBox.Width = 145;
            intervalBox.Minimum = 100;
            intervalBox.Maximum = 30000;
            intervalBox.Increment = 100;
            intervalBox.Value = 1000;

            Button startButton = new Button();
            startButton.Text = "Start";
            startButton.Left = 174;
            startButton.Top = 210;
            startButton.Width = 75;
            startButton.DialogResult = DialogResult.OK;

            Button cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Left = 255;
            cancelButton.Top = 210;
            cancelButton.Width = 75;
            cancelButton.DialogResult = DialogResult.Cancel;

            AcceptButton = startButton;
            CancelButton = cancelButton;

            Controls.Add(targetLabel);
            Controls.Add(targetBox);
            Controls.Add(countLabel);
            Controls.Add(countBox);
            Controls.Add(timeoutLabel);
            Controls.Add(timeoutBox);
            Controls.Add(intervalLabel);
            Controls.Add(intervalBox);
            Controls.Add(startButton);
            Controls.Add(cancelButton);
            AppVisualTheme.Apply(this);
        }
    }

    public class PingMonitorForm : Form
    {
        private readonly string ip;
        private readonly int count;
        private readonly int timeoutMs;
        private readonly int intervalMs;
        private readonly Func<string, int, Task> statusCallback;
        private readonly Action<string> pingColumnCallback;
        private CancellationTokenSource cancellationTokenSource;
        private DataGridView resultGrid;
        private ProgressBar progressBar;
        private Label progressLabel;
        private Label statsLabel;
        private Button stopButton;
        private Button closeButton;
        private int sent;
        private int received;
        private long min = long.MaxValue;
        private long max = 0;
        private long total = 0;

        public PingMonitorForm(
            string ip,
            int count,
            int timeoutMs,
            int intervalMs,
            Func<string, int, Task> statusCallback,
            Action<string> pingColumnCallback)
        {
            this.ip = ip;
            this.count = count;
            this.timeoutMs = timeoutMs;
            this.intervalMs = intervalMs;
            this.statusCallback = statusCallback;
            this.pingColumnCallback = pingColumnCallback;

            Text = "Ping Monitor - " + ip;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(620, 470);
            MinimumSize = new Size(560, 420);

            BuildUi();

            Shown += async (s, e) => await StartPingAsync();
            FormClosing += (s, e) => CancelPings();
            AppVisualTheme.Apply(this);
        }

        private void BuildUi()
        {
            Label titleLabel = new Label();
            titleLabel.Text = count <= 0
                ? "Continuous ping to " + ip
                : "Pinging " + ip + " - " + count + " requests";
            titleLabel.Left = 12;
            titleLabel.Top = 12;
            titleLabel.Width = 560;
            titleLabel.Font = new Font(Font, FontStyle.Bold);

            resultGrid = new DataGridView();
            resultGrid.Left = 12;
            resultGrid.Top = 42;
            resultGrid.Width = ClientSize.Width - 24;
            resultGrid.Height = ClientSize.Height - 175;
            resultGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            resultGrid.AllowUserToAddRows = false;
            resultGrid.AllowUserToDeleteRows = false;
            resultGrid.ReadOnly = true;
            resultGrid.RowHeadersVisible = false;
            resultGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            resultGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            resultGrid.Columns.Add("Request", "#");
            resultGrid.Columns.Add("Time", "Time");
            resultGrid.Columns.Add("Result", "Result");
            resultGrid.Columns[0].Width = 60;
            resultGrid.Columns[1].Width = 120;

            progressBar = new ProgressBar();
            progressBar.Left = 12;
            progressBar.Top = ClientSize.Height - 120;
            progressBar.Width = ClientSize.Width - 24;
            progressBar.Height = 18;
            progressBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            progressBar.Style = count <= 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            progressBar.Minimum = 0;
            progressBar.Maximum = count <= 0 ? 100 : count;

            progressLabel = new Label();
            progressLabel.Left = 12;
            progressLabel.Top = ClientSize.Height - 96;
            progressLabel.Width = 260;
            progressLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            progressLabel.Text = "Ready";

            statsLabel = new Label();
            statsLabel.Left = 285;
            statsLabel.Top = ClientSize.Height - 96;
            statsLabel.Width = 310;
            statsLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            statsLabel.Text = "Sent: 0 | Received: 0 | Lost: 0";

            stopButton = new Button();
            stopButton.Text = "Stop";
            stopButton.Left = ClientSize.Width - 174;
            stopButton.Top = ClientSize.Height - 50;
            stopButton.Width = 75;
            stopButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            stopButton.Click += (s, e) => CancelPings();

            closeButton = new Button();
            closeButton.Text = "Close";
            closeButton.Left = ClientSize.Width - 93;
            closeButton.Top = ClientSize.Height - 50;
            closeButton.Width = 75;
            closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            closeButton.Click += (s, e) => Close();

            Controls.Add(titleLabel);
            Controls.Add(resultGrid);
            Controls.Add(progressBar);
            Controls.Add(progressLabel);
            Controls.Add(statsLabel);
            Controls.Add(stopButton);
            Controls.Add(closeButton);
        }

        public void CancelPings()
        {
            if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
                cancellationTokenSource.Cancel();
        }

        private async Task StartPingAsync()
        {
            cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = cancellationTokenSource.Token;
            bool continuous = count <= 0;
            int requestNumber = 0;

            try
            {
                using (Ping ping = new Ping())
                {
                    while (!token.IsCancellationRequested && (continuous || requestNumber < count))
                    {
                        requestNumber++;
                        sent++;

                        string progressText = continuous
                            ? "Pinging " + ip + " (" + requestNumber + ")..."
                            : "Pinging " + ip + " (" + requestNumber + "/" + count + ")...";

                        progressLabel.Text = progressText;
                        await statusCallback(progressText, 0);

                        string resultText;

                        try
                        {
                            PingReply reply = await ping.SendPingAsync(ip, timeoutMs);

                            if (reply.Status == IPStatus.Success)
                            {
                                received++;
                                total += reply.RoundtripTime;
                                min = Math.Min(min, reply.RoundtripTime);
                                max = Math.Max(max, reply.RoundtripTime);
                                resultText = "Reply " + reply.RoundtripTime + " ms";
                                pingColumnCallback(reply.RoundtripTime + " ms");
                            }
                            else
                            {
                                resultText = reply.Status.ToString();
                                pingColumnCallback(resultText);
                            }
                        }
                        catch (Exception ex)
                        {
                            resultText = "Failed: " + ex.Message;
                            pingColumnCallback("Failed");
                        }

                        resultGrid.Rows.Add(requestNumber.ToString(), DateTime.Now.ToString("HH:mm:ss"), resultText);
                        resultGrid.FirstDisplayedScrollingRowIndex = resultGrid.Rows.Count - 1;

                        if (!continuous)
                            progressBar.Value = Math.Min(requestNumber, count);

                        UpdateStats();
                        await statusCallback(resultText, 0);

                        if (!continuous && requestNumber >= count)
                            break;

                        await Task.Delay(intervalMs, token);
                    }
                }

                if (token.IsCancellationRequested)
                {
                    progressLabel.Text = "Ping stopped";
                    await statusCallback("Ping stopped", 1500);
                }
                else
                {
                    progressLabel.Text = "Ping completed";
                    await statusCallback("Ping completed", 2000);
                }
            }
            catch (TaskCanceledException)
            {
                progressLabel.Text = "Ping stopped";
                await statusCallback("Ping stopped", 1500);
            }
            finally
            {
                stopButton.Enabled = false;
                closeButton.Enabled = true;
                if (cancellationTokenSource != null)
                {
                    cancellationTokenSource.Dispose();
                    cancellationTokenSource = null;
                }
            }
        }

        private void UpdateStats()
        {
            int lost = sent - received;
            int lossPercent = sent == 0 ? 0 : (int)Math.Round((lost * 100.0) / sent);
            long avg = received == 0 ? 0 : total / received;
            string minText = received == 0 ? "-" : min + " ms";
            string maxText = received == 0 ? "-" : max + " ms";
            string avgText = received == 0 ? "-" : avg + " ms";

            statsLabel.Text = "Sent: " + sent +
                              " | Received: " + received +
                              " | Lost: " + lost + " (" + lossPercent + "%)" +
                              " | Min/Max/Avg: " + minText + " / " + maxText + " / " + avgText;
        }
    }

    public class PingSelectedTarget
    {
        public string Ip { get; private set; }
        public Action<string> PingColumnCallback { get; private set; }

        public PingSelectedTarget(string ip, Action<string> pingColumnCallback)
        {
            Ip = ip;
            PingColumnCallback = pingColumnCallback;
        }
    }

    public static class RecentIpNetworkRegistry
    {
        private static readonly object Locker = new object();
        private static readonly Dictionary<string, string> MasksByIp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void SetSubnetMask(string ip, string subnetMask)
        {
            if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(subnetMask))
                return;

            IPAddress parsedIp;
            IPAddress parsedMask;
            if (!IPAddress.TryParse(ip, out parsedIp) ||
                parsedIp.AddressFamily != AddressFamily.InterNetwork ||
                !IPAddress.TryParse(subnetMask, out parsedMask) ||
                parsedMask.AddressFamily != AddressFamily.InterNetwork)
            {
                return;
            }

            lock (Locker)
            {
                MasksByIp[parsedIp.ToString()] = parsedMask.ToString();
            }
        }

        public static string GetSubnetMask(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return null;

            IPAddress parsedIp;
            if (!IPAddress.TryParse(ip, out parsedIp) ||
                parsedIp.AddressFamily != AddressFamily.InterNetwork)
            {
                return null;
            }

            lock (Locker)
            {
                string mask;
                return MasksByIp.TryGetValue(parsedIp.ToString(), out mask) ? mask : null;
            }
        }
    }

    public class PingSelectedResultsForm : Form
    {
        private readonly List<PingSelectedTarget> targets;
        private readonly Func<string, int, Task> statusCallback;
        private DataGridView resultGrid;
        private ProgressBar progressBar;
        private Label progressLabel;
        private Label statsLabel;
        private Button closeButton;
        private int sent;
        private int received;
        private long min = long.MaxValue;
        private long max = 0;
        private long total = 0;

        public PingSelectedResultsForm(List<PingSelectedTarget> targets, Func<string, int, Task> statusCallback)
        {
            this.targets = targets ?? new List<PingSelectedTarget>();
            this.statusCallback = statusCallback;

            Text = "Ping Selected Devices";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(720, 470);
            MinimumSize = new Size(620, 420);

            BuildUi();
            Shown += async (s, e) => await StartPingAsync();
            AppVisualTheme.Apply(this);
        }

        private void BuildUi()
        {
            Label titleLabel = new Label();
            titleLabel.Text = "Pinging selected devices - " + targets.Count + " requests";
            titleLabel.Left = 12;
            titleLabel.Top = 12;
            titleLabel.Width = 660;
            titleLabel.Font = new Font(Font, FontStyle.Bold);

            resultGrid = new DataGridView();
            resultGrid.Left = 12;
            resultGrid.Top = 42;
            resultGrid.Width = ClientSize.Width - 24;
            resultGrid.Height = ClientSize.Height - 175;
            resultGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            resultGrid.AllowUserToAddRows = false;
            resultGrid.AllowUserToDeleteRows = false;
            resultGrid.ReadOnly = true;
            resultGrid.RowHeadersVisible = false;
            resultGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            resultGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            resultGrid.Columns.Add("Number", "#");
            resultGrid.Columns.Add("IpAddress", "IP Address");
            resultGrid.Columns.Add("Time", "Time");
            resultGrid.Columns.Add("Result", "Result");
            resultGrid.Columns[0].Width = 55;
            resultGrid.Columns[1].Width = 150;
            resultGrid.Columns[2].Width = 110;

            progressBar = new ProgressBar();
            progressBar.Left = 12;
            progressBar.Top = ClientSize.Height - 120;
            progressBar.Width = ClientSize.Width - 24;
            progressBar.Height = 18;
            progressBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Minimum = 0;
            progressBar.Maximum = Math.Max(1, targets.Count);

            progressLabel = new Label();
            progressLabel.Left = 12;
            progressLabel.Top = ClientSize.Height - 96;
            progressLabel.Width = 260;
            progressLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            progressLabel.Text = "Ready";

            statsLabel = new Label();
            statsLabel.Left = 285;
            statsLabel.Top = ClientSize.Height - 96;
            statsLabel.Width = 390;
            statsLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            statsLabel.Text = "Sent: 0 | Received: 0 | Lost: 0";

            closeButton = new Button();
            closeButton.Text = "Close";
            closeButton.Left = ClientSize.Width - 93;
            closeButton.Top = ClientSize.Height - 50;
            closeButton.Width = 75;
            closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            closeButton.Enabled = false;
            closeButton.Click += (s, e) => Close();

            Controls.Add(titleLabel);
            Controls.Add(resultGrid);
            Controls.Add(progressBar);
            Controls.Add(progressLabel);
            Controls.Add(statsLabel);
            Controls.Add(closeButton);
        }

        private async Task StartPingAsync()
        {
            if (targets.Count == 0)
            {
                progressLabel.Text = "No devices selected";
                closeButton.Enabled = true;
                return;
            }

            progressLabel.Text = "Pinging selected devices...";
            if (statusCallback != null)
                await statusCallback("Pinging selected devices once...", 0);

            var tasks = new List<Task>();
            for (int i = 0; i < targets.Count; i++)
            {
                int displayIndex = i + 1;
                PingSelectedTarget target = targets[i];
                resultGrid.Rows.Add(displayIndex.ToString(), target.Ip, "", "Waiting...");
                int rowIndex = resultGrid.Rows.Count - 1;
                tasks.Add(PingTargetAsync(target, rowIndex));
            }

            await Task.WhenAll(tasks);

            progressLabel.Text = "Ping completed";
            closeButton.Enabled = true;
            if (statusCallback != null)
                await statusCallback("Ping selected finished. Replies: " + received + "/" + sent, 3000);
        }

        private async Task PingTargetAsync(PingSelectedTarget target, int rowIndex)
        {
            string resultText;
            bool success = false;
            long roundTrip = 0;

            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = await ping.SendPingAsync(target.Ip, 1000);
                    sent++;

                    if (reply.Status == IPStatus.Success)
                    {
                        success = true;
                        roundTrip = reply.RoundtripTime;
                        received++;
                        total += roundTrip;
                        min = Math.Min(min, roundTrip);
                        max = Math.Max(max, roundTrip);
                        resultText = "Reply " + roundTrip + " ms";
                    }
                    else
                    {
                        resultText = reply.Status.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                sent++;
                resultText = "Failed: " + ex.Message;
            }

            if (target.PingColumnCallback != null)
                target.PingColumnCallback(success ? roundTrip + " ms" : resultText);

            if (!IsDisposed && rowIndex >= 0 && rowIndex < resultGrid.Rows.Count)
            {
                resultGrid.Rows[rowIndex].Cells[2].Value = DateTime.Now.ToString("HH:mm:ss");
                resultGrid.Rows[rowIndex].Cells[3].Value = resultText;
            }

            progressBar.Value = Math.Min(sent, progressBar.Maximum);
            UpdateStats();
        }

        private void UpdateStats()
        {
            int lost = sent - received;
            int lossPercent = sent == 0 ? 0 : (int)Math.Round((lost * 100.0) / sent);
            long avg = received == 0 ? 0 : total / received;
            string minText = received == 0 ? "-" : min + " ms";
            string maxText = received == 0 ? "-" : max + " ms";
            string avgText = received == 0 ? "-" : avg + " ms";

            statsLabel.Text = "Sent: " + sent +
                              " | Received: " + received +
                              " | Lost: " + lost + " (" + lossPercent + "%)" +
                              " | Min/Max/Avg: " + minText + " / " + maxText + " / " + avgText;
        }
    }
}


