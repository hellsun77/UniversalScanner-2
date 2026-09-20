using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace UniversalScanner
{
    public class VlcMosaicStream
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string ProbeUrl { get; set; }
    }

    public class VlcMosaicViewForm : Form
    {
        private readonly string vlcPath;
        private readonly List<VlcMosaicStream> streams;
        private readonly List<Process> vlcProcesses = new List<Process>();
        private readonly List<Panel> videoPanels = new List<Panel>();
        private TableLayoutPanel grid;

        [DllImport("user32.dll")]
        private static extern bool SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

        public VlcMosaicViewForm(string vlcPath, IList<VlcMosaicStream> streams)
        {
            this.vlcPath = vlcPath ?? "";
            this.streams = new List<VlcMosaicStream>(streams ?? new List<VlcMosaicStream>());

            Text = "VLC Mosaic View";
            StartPosition = FormStartPosition.CenterParent;
            WindowState = FormWindowState.Maximized;
            MinimumSize = new Size(800, 500);

            BuildUi();

            Shown += (sender, args) => StartStreams();
            FormClosing += (sender, args) => StopStreams();
            Resize += (sender, args) => ResizeEmbeddedPlayers();
        }

        private void BuildUi()
        {
            int count = Math.Max(1, streams.Count);
            int cols = (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling(count / (double)cols);

            grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.BackColor = Color.Black;
            grid.ColumnCount = cols;
            grid.RowCount = rows;
            grid.Padding = new Padding(4);

            for (int c = 0; c < cols; c++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
            for (int r = 0; r < rows; r++)
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));

            for (int i = 0; i < count; i++)
            {
                Panel tile = new Panel();
                tile.Dock = DockStyle.Fill;
                tile.Margin = new Padding(4);
                tile.BackColor = Color.Black;
                tile.BorderStyle = BorderStyle.FixedSingle;

                Label title = new Label();
                title.Dock = DockStyle.Top;
                title.Height = 24;
                title.BackColor = Color.FromArgb(28, 61, 108);
                title.ForeColor = Color.White;
                title.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                title.TextAlign = ContentAlignment.MiddleLeft;
                title.Padding = new Padding(8, 0, 0, 0);
                title.Text = i < streams.Count ? streams[i].Title : "";

                Panel videoPanel = new Panel();
                videoPanel.Dock = DockStyle.Fill;
                videoPanel.BackColor = Color.Black;

                if (i < streams.Count)
                {
                    int streamIndex = i;
                    tile.DoubleClick += (sender, args) => OpenFullscreenStream(streamIndex);
                    title.DoubleClick += (sender, args) => OpenFullscreenStream(streamIndex);
                    videoPanel.DoubleClick += (sender, args) => OpenFullscreenStream(streamIndex);
                }

                tile.Controls.Add(videoPanel);
                tile.Controls.Add(title);
                videoPanels.Add(videoPanel);

                grid.Controls.Add(tile, i % cols, i / cols);
            }

            Controls.Add(grid);
        }

        private void StartStreams()
        {
            for (int i = 0; i < streams.Count && i < videoPanels.Count; i++)
            {
                VlcMosaicStream stream = streams[i];
                Panel panel = videoPanels[i];

                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = vlcPath;
                    startInfo.Arguments =
                        "-I dummy --no-one-instance --no-video-title-show --no-audio " +
                        "--rtsp-tcp --network-caching=500 --no-video-deco " +
                        "--drawable-hwnd " + panel.Handle.ToInt64() + " " +
                        RtspLiveViewForm.QuoteArgument(stream.Url);
                    startInfo.UseShellExecute = false;
                    startInfo.CreateNoWindow = true;
                    startInfo.WindowStyle = ProcessWindowStyle.Hidden;

                    Process process = Process.Start(startInfo);
                    if (process != null)
                    {
                        vlcProcesses.Add(process);
                        TryEmbedProcessWindow(process, panel);
                    }
                }
                catch
                {
                    Label errorLabel = new Label();
                    errorLabel.Dock = DockStyle.Fill;
                    errorLabel.ForeColor = Color.White;
                    errorLabel.TextAlign = ContentAlignment.MiddleCenter;
                    errorLabel.Text = "Unable to open stream";
                    panel.Controls.Add(errorLabel);
                }
            }
        }

        private static void TryEmbedProcessWindow(Process process, Panel panel)
        {
            IntPtr handle = WaitForMainWindow(process, 2500);
            if (handle == IntPtr.Zero)
                return;

            SetParent(handle, panel.Handle);
            MoveWindow(handle, 0, 0, panel.Width, panel.Height, true);
        }

        private void ResizeEmbeddedPlayers()
        {
            foreach (Process process in vlcProcesses)
            {
                try
                {
                    process.Refresh();
                    if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
                        continue;

                    foreach (Panel panel in videoPanels)
                    {
                        if (panel.Handle == IntPtr.Zero)
                            continue;

                        MoveWindow(process.MainWindowHandle, 0, 0, panel.Width, panel.Height, true);
                    }
                }
                catch
                {
                }
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

        private void OpenFullscreenStream(int streamIndex)
        {
            if (streamIndex < 0 || streamIndex >= streams.Count)
                return;

            try
            {
                VlcMosaicStream stream = streams[streamIndex];
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = vlcPath;
                startInfo.Arguments =
                    "--no-one-instance --no-video-title-show --rtsp-tcp --network-caching=500 --fullscreen " +
                    RtspLiveViewForm.QuoteArgument(stream.Url);
                startInfo.UseShellExecute = false;
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Failed to open fullscreen VLC:\r\n\r\n" + ex.Message,
                    "VLC Mosaic View",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void StopStreams()
        {
            foreach (Process process in vlcProcesses)
            {
                try
                {
                    if (process != null && !process.HasExited)
                        process.Kill();
                }
                catch
                {
                }
            }

            vlcProcesses.Clear();
        }
    }
}
