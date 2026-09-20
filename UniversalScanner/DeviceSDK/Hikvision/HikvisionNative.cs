using System;
using System.Runtime.InteropServices;
using System.Text;

namespace UniversalScanner.DeviceSDK.Hikvision
{
    public static class HikvisionNative
    {
        private const int PASSWD_LEN = 16;

        [StructLayout(LayoutKind.Sequential)]
        public struct NET_DVR_ACTIVATECFG
        {
            public uint dwSize;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PASSWD_LEN)]
            public byte[] sPassword;

            public byte byLoginMode;
            public byte byHttps;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 106)]
            public byte[] byRes;

            public static NET_DVR_ACTIVATECFG Create(string password)
            {
                NET_DVR_ACTIVATECFG cfg = new NET_DVR_ACTIVATECFG();

                cfg.dwSize = (uint)Marshal.SizeOf(typeof(NET_DVR_ACTIVATECFG));
                cfg.sPassword = new byte[PASSWD_LEN];
                cfg.byRes = new byte[106];

                byte[] pwdBytes = Encoding.ASCII.GetBytes(password);

                int copyLength = Math.Min(pwdBytes.Length, PASSWD_LEN);
                Array.Copy(pwdBytes, cfg.sPassword, copyLength);

                cfg.byLoginMode = 0;
                cfg.byHttps = 0;

                return cfg;
            }
        }

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool NET_DVR_ActivateDevice(
            string sDVRIP,
            ushort wDVRPort,
            ref NET_DVR_ACTIVATECFG lpActivateCfg);
    }
}