using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UniversalScanner
{
    static class Program
    {
        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(HandleRef hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr WindowHandle);

        public const int SW_RESTORE = 9;

        static bool checkAlreadyRunning(bool foregroundExisting = false)
        {
            Process[] running =
                Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly().Location));

            if (running.Length <= 1)
                return false;

            if (!foregroundExisting)
                return true;

            foreach (Process p in running)
            {
                IntPtr hWnd = p.MainWindowHandle;

                if (hWnd != IntPtr.Zero)
                {
                    ShowWindowAsync(new HandleRef(null, hWnd), SW_RESTORE);
                    SetForegroundWindow(p.MainWindowHandle);
                }
            }

            return true;
        }

        public static ScanEngine[] Engines { get; private set; }

        [STAThread]
        static void Main()
        {
            ScannerWindow viewer;
            SplashForm splash;

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Application.ThreadException += Application_ThreadException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            if (checkAlreadyRunning(true))
            {
                Application.Exit();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            splash = new SplashForm();
            splash.Show();
            splash.Refresh();

            splash.SetStatus("Starting Universal Scanner...");
            Application.DoEvents();

            try
            {
                splash.SetStatus("Preparing runtime files...");
                Application.DoEvents();
                RuntimeDependencyManager.Prepare();
                Application.ApplicationExit += (sender, args) => RuntimeDependencyManager.Cleanup();
                AppDomain.CurrentDomain.ProcessExit += (sender, args) => RuntimeDependencyManager.Cleanup();
            }
            catch (Exception ex)
            {
                splash.Close();
                splash.Dispose();
                MessageBox.Show("Failed to prepare runtime files:\r\n\r\n" + ex.Message,
                    "Universal Scanner", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                viewer = new ScannerWindow();

                splash.SetStatus("Loading scan engines...");
                Application.DoEvents();

                Engines = new ScanEngine[]
                {
                    new SSDP(),
                    new Wsdiscovery(),
                    new Dahua1(),
                    new Dahua2(),
                    new Hikvision(),
                    new HoneywellMdns(),
                    new Tiandy(),
                    new Axis(),
                    new Bosch(),
                    new GoogleCast(),
                    new Hanwha(),
                    new Vivotek(),
                    new Sony(),
                    new Ubiquiti(),
                    new _360Vision(),
                    new NiceVision(),
                    new Panasonic(),
                    new Arecont(),
                    new GigEVision(),
                    new Vstarcam(),
                    new Eaton(),
                    new Foscam(),
                    null,
                    null,
                    new Lantronix(),
                    new Microchip(),
                    new Advantech(),
                    new EdenOptima(),
                    null,
                    new CyberPower()
                };

                splash.SetStatus("Registering protocols...");
                Application.DoEvents();

                for (uint i = 0; i < Engines.Length; i++)
                {
                    if (Engines[i] != null)
                    {
                        Engines[i].registerViewer(viewer, i + 1);
                        PortProvider.getInstance().reserveUDP(Engines[i].getUsedPort());
                    }
                }

                splash.SetStatus("Starting listeners...");
                Application.DoEvents();

                for (uint i = 0; i < Engines.Length; i++)
                {
                    if (Engines[i] != null)
                    {
                        Engines[i].listen();
                    }
                }

                splash.SetStatus("Opening application...");
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);

                splash.Close();
                splash.Dispose();

                Application.Run(viewer);
            }
            catch (Exception ex)
            {
                try
                {
                    splash.Close();
                    splash.Dispose();
                }
                catch
                {
                }

                ShowFatalStartupError(ex);
            }
        }

        private static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            ShowFatalStartupError(e.Exception);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ShowFatalStartupError(e.ExceptionObject as Exception);
        }

        private static void ShowFatalStartupError(Exception ex)
        {
            string logPath = WriteCrashLog(ex);
            string detail = ex == null ? "Unknown error." : ex.Message;
            MessageBox.Show(
                "Universal Scanner encountered a startup error:\r\n\r\n" + detail +
                "\r\n\r\nA crash log was written to:\r\n" + logPath,
                "Universal Scanner",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private static string WriteCrashLog(Exception ex)
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UniversalScanner");
            string path = Path.Combine(folder, "startup-crash.log");

            try
            {
                Directory.CreateDirectory(folder);
                File.AppendAllText(path,
                    "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===" + Environment.NewLine +
                    (ex == null ? "Unknown error" : ex.ToString()) +
                    Environment.NewLine + Environment.NewLine);
            }
            catch
            {
            }

            return path;
        }
    }
}
