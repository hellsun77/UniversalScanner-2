using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace UniversalScanner
{
    public class RtspLiveViewForm : Form
    {
        private readonly string ipAddress;
        private readonly string _vendor;
        private readonly string _model;
        private TextBox ipBox;
        private TextBox usernameBox;
        private TextBox passwordBox;
        private TextBox pathBox;
        private CheckBox showPasswordBox;
        private Label previewLabel;
        private Button openButton;
        private Button cancelButton;

        private enum HikvisionChannelDetectionState
        {
            NotAttempted,
            InProgress,
            Completed,
            Failed,
            Skipped
        }
        private HikvisionChannelDetectionState _detectionState = HikvisionChannelDetectionState.NotAttempted;
        private bool _isUsingAutomaticDefaultPath = true;
        private bool _internalPathChange = false;

        public RtspLiveViewForm(string ipAddress, string vendor, string model)
        {
            this.ipAddress = ipAddress;
            this._vendor = vendor ?? "";
            this._model = model ?? "";

            Text = "Show Live by VLC";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 305);

            BuildUi();

            ipBox.Text = ipAddress;
            usernameBox.Text = "admin";
            SetPathBoxInternal(GetDefaultRtspPath(vendor, model));
            UpdatePreview();
        }

        private void BuildUi()
        {
            int leftLabel = 22;
            int leftBox = 158;
            int top = 24;
            int gap = 38;

            AddLabel("IP Address:", leftLabel, top);
            ipBox = AddBox(leftBox, top, true);

            top += gap;
            AddLabel("Username:", leftLabel, top);
            usernameBox = AddBox(leftBox, top, false);
            usernameBox.TextChanged += (sender, args) => UpdatePreview();

            top += gap;
            AddLabel("Password:", leftLabel, top);
            passwordBox = AddBox(leftBox, top, false);
            passwordBox.UseSystemPasswordChar = true;
            passwordBox.TextChanged += (sender, args) => UpdatePreview();

            showPasswordBox = new CheckBox();
            showPasswordBox.Left = leftBox;
            showPasswordBox.Top = top + 28;
            showPasswordBox.Width = 160;
            showPasswordBox.Text = "Show password";
            showPasswordBox.CheckedChanged += (sender, args) =>
            {
                passwordBox.UseSystemPasswordChar = !showPasswordBox.Checked;
            };
            Controls.Add(showPasswordBox);

            top += gap + 14;
            AddLabel("RTSP Path:", leftLabel, top);
            pathBox = AddBox(leftBox, top, false);
            pathBox.TextChanged += (sender, args) =>
            {
                if (!_internalPathChange)
                {
                    _isUsingAutomaticDefaultPath = false;
                }
                UpdatePreview();
            };

            previewLabel = new Label();
            previewLabel.Left = 22;
            previewLabel.Top = 190;
            previewLabel.Width = 476;
            previewLabel.Height = 42;
            previewLabel.ForeColor = Color.FromArgb(60, 74, 91);
            previewLabel.Text = "rtsp://";
            Controls.Add(previewLabel);

            openButton = new Button();
            openButton.Text = "Open in VLC";
            openButton.Left = 310;
            openButton.Top = 250;
            openButton.Width = 96;
            openButton.Click += OpenButton_Click;

            cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Left = 418;
            cancelButton.Top = 250;
            cancelButton.Width = 80;
            cancelButton.DialogResult = DialogResult.Cancel;

            Controls.Add(openButton);
            Controls.Add(cancelButton);

            AcceptButton = openButton;
            CancelButton = cancelButton;
        }

        private Label AddLabel(string text, int left, int top)
        {
            Label label = new Label();
            label.Left = left;
            label.Top = top + 4;
            label.Width = 125;
            label.Height = 22;
            label.Text = text;
            Controls.Add(label);
            return label;
        }

        private TextBox AddBox(int left, int top, bool readOnly)
        {
            TextBox box = new TextBox();
            box.Left = left;
            box.Top = top;
            box.Width = 340;
            box.ReadOnly = readOnly;
            Controls.Add(box);
            return box;
        }

        public void PreFillCredentials(string username, string password)
        {
            if (!string.IsNullOrWhiteSpace(username))
                usernameBox.Text = username;
            if (!string.IsNullOrEmpty(password))
                passwordBox.Text = password;
        }

        public async System.Threading.Tasks.Task<bool> TryAutoLaunchAsync(bool quietCredentialFailure = false)
        {
            return await TryOpenStreamAsync(quietCredentialFailure);
        }

        private void SetPathBoxInternal(string newPath)
        {
            _internalPathChange = true;
            pathBox.Text = newPath;
            _internalPathChange = false;
        }

        private async System.Threading.Tasks.Task<bool> TryOpenStreamAsync(bool quietCredentialFailure = false)
        {
            if (string.IsNullOrWhiteSpace(usernameBox.Text))
            {
                if (quietCredentialFailure)
                    return false;

                MessageBox.Show(this, "Username is required.", "Show Live by VLC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(passwordBox.Text))
            {
                if (quietCredentialFailure)
                    return false;

                MessageBox.Show(this, "Password is required.", "Show Live by VLC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string vlcPath = FindVlcPath();
            if (string.IsNullOrWhiteSpace(vlcPath))
            {
                MessageBox.Show(this, "VLC was not found.\r\n\r\nPlease install VLC Media Player, or add vlc.exe to PATH.", "Show Live by VLC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            bool hikvisionCandidate = IsHikvisionRtspCandidate();
            if (_isUsingAutomaticDefaultPath &&
                ShouldTryHikvisionChannelDiscovery(hikvisionCandidate) &&
                _detectionState != HikvisionChannelDetectionState.Completed &&
                _detectionState != HikvisionChannelDetectionState.InProgress)
            {
                _detectionState = HikvisionChannelDetectionState.InProgress;
                openButton.Enabled = false;
                string originalButtonText = openButton.Text;
                openButton.Text = "Detecting...";
                
                System.Collections.Generic.List<UniversalScanner.DeviceSDK.Hikvision.HikvisionChannel> channels = null;
                bool authFailed = false;

                try
                {
                    channels = await UniversalScanner.DeviceSDK.Hikvision.HikvisionStreamingHelper.GetChannelsAsync(ipAddress, usernameBox.Text.Trim(), passwordBox.Text);
                }
                catch (UnauthorizedAccessException)
                {
                    authFailed = true;
                }
                catch
                {
                    // generic fail
                }
                finally
                {
                    openButton.Enabled = true;
                    openButton.Text = originalButtonText;
                }

                if (authFailed)
                {
                    if (!hikvisionCandidate)
                    {
                        _detectionState = HikvisionChannelDetectionState.Skipped;
                        goto OpenCurrentPath;
                    }

                    // The silent Universal Login attempt may fail before the
                    // form is shown. Reset detection so the user retry runs
                    // channel discovery again instead of keeping channel 1.
                    _detectionState = HikvisionChannelDetectionState.NotAttempted;

                    if (quietCredentialFailure)
                        return false;

                    MessageBox.Show(this, "Authentication failed. Username or password was rejected.", "Show Live by VLC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    usernameBox.Focus();
                    return false;
                }

                if (channels != null && channels.Count == 1)
                {
                    // Exactly one physical channel
                    int streamType = 1;
                    if (!channels[0].SupportedStreamTypes.Contains(1) && channels[0].SupportedStreamTypes.Count > 0)
                    {
                        streamType = channels[0].SupportedStreamTypes[0];
                    }
                    int sid = 101;
                    try { sid = checked((channels[0].PhysicalChannel * 100) + streamType); } catch { }
                    SetPathBoxInternal($"/Streaming/Channels/{sid}");
                    _detectionState = HikvisionChannelDetectionState.Completed;
                }
                else if (channels != null && channels.Count > 1)
                {
                    // Show selector
                    using (var sel = new UniversalScanner.Forms.HikvisionChannelSelectForm(channels, false))
                    {
                        if (sel.ShowDialog(this) == DialogResult.OK)
                        {
                            SetPathBoxInternal($"/Streaming/Channels/{sel.SelectedStreamId}");
                            _detectionState = HikvisionChannelDetectionState.Completed;
                        }
                        else
                        {
                            _detectionState = HikvisionChannelDetectionState.NotAttempted;
                            return false;
                        }
                    }
                }
                else
                {
                    if (!hikvisionCandidate)
                    {
                        _detectionState = HikvisionChannelDetectionState.Skipped;
                        goto OpenCurrentPath;
                    }

                    // Query failed or 0 channels
                    _detectionState = HikvisionChannelDetectionState.Failed;
                    using (var sel = new UniversalScanner.Forms.HikvisionChannelSelectForm(null, true))
                    {
                        if (sel.ShowDialog(this) == DialogResult.OK)
                        {
                            SetPathBoxInternal($"/Streaming/Channels/{sel.SelectedStreamId}");
                            _detectionState = HikvisionChannelDetectionState.Completed; // mark completed since they chose something
                        }
                        else
                        {
                            return false; // cancel
                        }
                    }
                }
            }

        OpenCurrentPath:
            try
            {
                string url = BuildRtspUrl();

                // Do not use RTSP OPTIONS as an authentication gate. Some
                // Hikvision/NVR firmware rejects OPTIONS while accepting the
                // DESCRIBE/SETUP/PLAY sequence that VLC uses for playback.

                string un = usernameBox.Text.Trim();
                string pw = passwordBox.Text;

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = vlcPath;
                // Use VLC's RTSP credential options instead of URL user-info.
                // Some DVRs handle channel 1 over UDP but fail later channels
                // unless the RTSP session is explicitly carried over TCP.
                startInfo.Arguments = "--rtsp-tcp --network-caching=500 " +
                    "--rtsp-user " + QuoteArgument(un) + " " +
                    "--rtsp-pwd " + QuoteArgument(pw) + " " +
                    QuoteArgument(url);
                startInfo.UseShellExecute = false;
                Process.Start(startInfo);
                
                DialogResult = DialogResult.OK;
                if (Visible) Close();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to open VLC:\r\n\r\n" + ex.Message, "Show Live by VLC", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private async void OpenButton_Click(object sender, EventArgs e)
        {
            await TryOpenStreamAsync();
        }

        private void UpdatePreview()
        {
            if (previewLabel == null)
                return;

            previewLabel.Text = BuildRtspUrl();
        }

        private string BuildRtspUrl()
        {
            string path = (pathBox?.Text ?? "").Trim();

            if (!path.StartsWith("/", StringComparison.Ordinal))
                path = "/" + path;

            return "rtsp://" + ipAddress + ":554" + path;
        }

        public static string QuoteArgument(string value)
        {
            if (value == null)
                value = "";

            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        public static string GetDefaultRtspPath(string vendor, string model)
        {
            return GetRtspPath(vendor, model, false);
        }

        public static string GetRtspPath(string vendor, string model, bool useSubStream)
        {
            string text = ((vendor ?? "") + " " + (model ?? "")).ToLowerInvariant();

            if (text.Contains("dahua"))
                return "/cam/realmonitor?channel=1&subtype=" + (useSubStream ? "1" : "0");

            if (text.Contains("honeywell"))
            {
                if (text.Contains("hc30") || text.Contains("hc35"))
                    return useSubStream ? "/h264/ch1/sub/av_stream" : "/h264/ch1/main/av_stream";

                return "/cam/realmonitor?channel=1&subtype=" + (useSubStream ? "1" : "0");
            }

            if (text.Contains("uniview"))
                return useSubStream ? "/media/video2" : "/media/video1";

            if (text.Contains("axis"))
                return useSubStream
                    ? "/axis-media/media.amp?videocodec=h264&resolution=640x360"
                    : "/axis-media/media.amp";

            if (IsBoschRtspText(text))
                return useSubStream ? "/?line=2" : "/?line=1";

            if (text.Contains("tiandy"))
                return useSubStream ? "/profile2" : "/profile1";

            if (text.Contains("sunell"))
                return useSubStream ? "/profile2" : "/profile1";

            return useSubStream ? "/Streaming/Channels/102" : "/Streaming/Channels/101";
        }

        private bool IsBoschDevice()
        {
            return IsBoschRtspText(_vendor);
        }

        private bool IsHikvisionRtspCandidate()
        {
            string text = ((_vendor ?? "") + " " + (_model ?? "")).ToLowerInvariant();
            return text.Contains("hikvision") ||
                   text.Contains("hiwatch") ||
                   text.Contains("hik-connect") ||
                   text.Contains("ds-") ||
                   text.Contains("ids-") ||
                   text.Contains("nvr") ||
                   text.Contains("dvr");
        }

        private bool ShouldTryHikvisionChannelDiscovery(bool hikvisionCandidate)
        {
            if (hikvisionCandidate)
                return true;

            string path = (pathBox?.Text ?? "").Trim();
            return path.StartsWith("/Streaming/Channels/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBoschRtspText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.IndexOf("bosch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("vcs video", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("video communication systems", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string FindVlcPath()
        {
            List<string> candidates = new List<string>();
            AddVlcCandidate(candidates, GetVlcAppPathFromRegistry(Registry.LocalMachine));
            AddVlcCandidate(candidates, GetVlcAppPathFromRegistry(Registry.CurrentUser));
            AddVlcCandidate(candidates, GetVlcUninstallPathFromRegistry(Registry.LocalMachine, RegistryView.Registry64));
            AddVlcCandidate(candidates, GetVlcUninstallPathFromRegistry(Registry.LocalMachine, RegistryView.Registry32));
            AddVlcCandidate(candidates, GetVlcUninstallPathFromRegistry(Registry.CurrentUser, RegistryView.Default));
            AddVlcCandidate(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC", "vlc.exe"));
            AddVlcCandidate(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN", "VLC", "vlc.exe"));
            AddVlcCandidate(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VideoLAN", "VLC", "vlc.exe"));
            AddVlcCandidate(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoLAN", "VLC", "vlc.exe"));
            AddVlcCandidate(candidates, FindVlcOnPath());

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return "";
        }

        private static void AddVlcCandidate(List<string> candidates, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            path = path.Trim().Trim('"');

            if (File.Exists(path) && !candidates.Exists(candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase)))
                candidates.Add(path);
        }

        private static string GetVlcAppPathFromRegistry(RegistryKey root)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\vlc.exe"))
                {
                    return key == null ? "" : Convert.ToString(key.GetValue(""));
                }
            }
            catch
            {
                return "";
            }
        }

        private static string GetVlcUninstallPathFromRegistry(RegistryKey root, RegistryView view)
        {
            try
            {
                RegistryHive hive = root.Name == "HKEY_CURRENT_USER" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey uninstallKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstallKey == null)
                        return "";

                    foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                    {
                        using (RegistryKey appKey = uninstallKey.OpenSubKey(subKeyName))
                        {
                            if (appKey == null)
                                continue;

                            string displayName = Convert.ToString(appKey.GetValue("DisplayName"));
                            if (string.IsNullOrWhiteSpace(displayName) ||
                                displayName.IndexOf("VLC media player", StringComparison.OrdinalIgnoreCase) < 0)
                                continue;

                            string installLocation = Convert.ToString(appKey.GetValue("InstallLocation"));
                            if (!string.IsNullOrWhiteSpace(installLocation))
                            {
                                string candidate = Path.Combine(installLocation.Trim().Trim('"'), "vlc.exe");
                                if (File.Exists(candidate))
                                    return candidate;
                            }

                            string iconPath = NormalizeExecutablePath(Convert.ToString(appKey.GetValue("DisplayIcon")));
                            if (File.Exists(iconPath))
                                return iconPath;
                        }
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        private static string FindVlcOnPath()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "where";
                info.Arguments = "vlc.exe";
                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;
                info.CreateNoWindow = true;

                using (Process process = Process.Start(info))
                {
                    if (process == null)
                        return "";

                    string firstLine = process.StandardOutput.ReadLine();
                    process.WaitForExit(1500);

                    if (!string.IsNullOrWhiteSpace(firstLine) && File.Exists(firstLine.Trim()))
                        return firstLine.Trim();
                }
            }
            catch
            {
            }

            return "";
        }

        private static string NormalizeExecutablePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string path = value.Trim().Trim('"');
            int exeIndex = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex >= 0)
                path = path.Substring(0, exeIndex + 4);

            return path.Trim().Trim('"');
        }

        private enum RtspCredentialCheck
        {
            Accepted,
            Rejected,
            Unknown
        }

        public static async System.Threading.Tasks.Task<bool> IsRtspAuthenticationRejectedAsync(string url, string username, string password)
        {
            return await CheckRtspCredentialsAsync(url, username, password) == RtspCredentialCheck.Rejected;
        }

        private static async System.Threading.Tasks.Task<RtspCredentialCheck> CheckRtspCredentialsAsync(string url, string username, string password)
        {
            try
            {
                Uri uri = new Uri(url);
                int port = uri.Port > 0 ? uri.Port : 554;

                string firstResponse = await SendRtspOptionsAsync(uri.Host, port, url, null);
                int firstStatus = ParseRtspStatusCode(firstResponse);

                if (firstStatus >= 200 && firstStatus < 300)
                    return RtspCredentialCheck.Accepted;

                if (firstStatus != 401)
                    return RtspCredentialCheck.Unknown;

                string authenticate = GetHeader(firstResponse, "WWW-Authenticate");
                if (string.IsNullOrWhiteSpace(authenticate))
                    return RtspCredentialCheck.Unknown;

                string authorization = BuildAuthorizationHeader(authenticate, username, password, "OPTIONS", url);
                if (string.IsNullOrWhiteSpace(authorization))
                    return RtspCredentialCheck.Unknown;

                string secondResponse = await SendRtspOptionsAsync(uri.Host, port, url, authorization);
                int secondStatus = ParseRtspStatusCode(secondResponse);

                if (secondStatus >= 200 && secondStatus < 300)
                    return RtspCredentialCheck.Accepted;

                if (secondStatus == 401 || secondStatus == 403)
                    return RtspCredentialCheck.Rejected;
            }
            catch
            {
            }

            return RtspCredentialCheck.Unknown;
        }

        private static async System.Threading.Tasks.Task<string> SendRtspOptionsAsync(string host, int port, string url, string authorization)
        {
            using (TcpClient client = new TcpClient())
            {
                var connectTask = client.ConnectAsync(host, port);
                if (await System.Threading.Tasks.Task.WhenAny(connectTask, System.Threading.Tasks.Task.Delay(1800)) != connectTask)
                    return "";

                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 1800;
                    stream.WriteTimeout = 1800;

                    string request =
                        "OPTIONS " + url + " RTSP/1.0\r\n" +
                        "CSeq: 1\r\n" +
                        "User-Agent: Universal Scanner\r\n";

                    if (!string.IsNullOrWhiteSpace(authorization))
                        request += "Authorization: " + authorization + "\r\n";

                    request += "\r\n";

                    byte[] requestBytes = Encoding.ASCII.GetBytes(request);
                    await stream.WriteAsync(requestBytes, 0, requestBytes.Length);

                    byte[] buffer = new byte[4096];
                    var readTask = stream.ReadAsync(buffer, 0, buffer.Length);
                    if (await System.Threading.Tasks.Task.WhenAny(readTask, System.Threading.Tasks.Task.Delay(1800)) != readTask)
                        return "";

                    int read = readTask.Result;
                    return read <= 0 ? "" : Encoding.ASCII.GetString(buffer, 0, read);
                }
            }
        }

        private static int ParseRtspStatusCode(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return 0;

            Match match = Regex.Match(response, @"^RTSP/\d+\.\d+\s+(\d{3})", RegexOptions.IgnoreCase);
            if (!match.Success)
                return 0;

            int statusCode;
            return int.TryParse(match.Groups[1].Value, out statusCode) ? statusCode : 0;
        }

        private static string GetHeader(string response, string headerName)
        {
            if (string.IsNullOrWhiteSpace(response) || string.IsNullOrWhiteSpace(headerName))
                return "";

            Match match = Regex.Match(response, "^" + Regex.Escape(headerName) + @":\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        private static string BuildAuthorizationHeader(string authenticate, string username, string password, string method, string uri)
        {
            if (authenticate.StartsWith("Basic", StringComparison.OrdinalIgnoreCase))
            {
                string token = Convert.ToBase64String(Encoding.ASCII.GetBytes((username ?? "") + ":" + (password ?? "")));
                return "Basic " + token;
            }

            if (!authenticate.StartsWith("Digest", StringComparison.OrdinalIgnoreCase))
                return "";

            string realm = GetAuthValue(authenticate, "realm");
            string nonce = GetAuthValue(authenticate, "nonce");
            string qop = GetAuthValue(authenticate, "qop");
            string opaque = GetAuthValue(authenticate, "opaque");

            if (string.IsNullOrWhiteSpace(realm) || string.IsNullOrWhiteSpace(nonce))
                return "";

            string nc = "00000001";
            string cnonce = Guid.NewGuid().ToString("N").Substring(0, 16);
            string ha1 = Md5Hex((username ?? "") + ":" + realm + ":" + (password ?? ""));
            string ha2 = Md5Hex(method + ":" + uri);
            bool useQop = !string.IsNullOrWhiteSpace(qop);
            string selectedQop = useQop && qop.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0 ? "auth" : "";
            string response = string.IsNullOrWhiteSpace(selectedQop)
                ? Md5Hex(ha1 + ":" + nonce + ":" + ha2)
                : Md5Hex(ha1 + ":" + nonce + ":" + nc + ":" + cnonce + ":" + selectedQop + ":" + ha2);

            string header =
                "Digest username=\"" + EscapeAuthValue(username ?? "") + "\"" +
                ", realm=\"" + EscapeAuthValue(realm) + "\"" +
                ", nonce=\"" + EscapeAuthValue(nonce) + "\"" +
                ", uri=\"" + EscapeAuthValue(uri) + "\"" +
                ", response=\"" + response + "\"";

            if (!string.IsNullOrWhiteSpace(opaque))
                header += ", opaque=\"" + EscapeAuthValue(opaque) + "\"";

            if (!string.IsNullOrWhiteSpace(selectedQop))
                header += ", qop=" + selectedQop + ", nc=" + nc + ", cnonce=\"" + cnonce + "\"";

            return header;
        }

        private static string GetAuthValue(string header, string name)
        {
            Match quoted = Regex.Match(header, Regex.Escape(name) + "\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            if (quoted.Success)
                return quoted.Groups[1].Value;

            Match bare = Regex.Match(header, Regex.Escape(name) + "\\s*=\\s*([^,\\s]+)", RegexOptions.IgnoreCase);
            return bare.Success ? bare.Groups[1].Value : "";
        }

        private static string EscapeAuthValue(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string Md5Hex(string value)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.ASCII.GetBytes(value ?? ""));
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
