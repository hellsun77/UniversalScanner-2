using System;
using System.IO;
using System.Runtime.InteropServices;

namespace UniversalScanner.DeviceSDK.Hikvision
{
    public static class HikvisionSdk
    {
        private static bool initialized = false;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool NET_DVR_Init();

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool NET_DVR_Cleanup();

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint NET_DVR_GetLastError();

        public static bool IsInitialized
        {
            get { return initialized; }
        }

        public static bool Initialize()
        {
            if (initialized)
                return true;

            string sdkPath = Path.Combine(RuntimeDependencyManager.SdkRoot, "Hikvision");

            if (!Directory.Exists(sdkPath))
                return false;

            SetDllDirectory(sdkPath);

            initialized = NET_DVR_Init();

            return initialized;
        }

        public static uint GetLastError()
        {
            try
            {
                return NET_DVR_GetLastError();
            }
            catch
            {
                return 0;
            }
        }

        public static void Cleanup()
        {
            if (!initialized)
                return;

            NET_DVR_Cleanup();
            initialized = false;
        }
    }
}
