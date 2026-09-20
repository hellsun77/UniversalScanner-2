using PreviewDemo;
using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace UniversalScanner.DeviceSDK.Hikvision
{
    public static class HikvisionNetwork
    {
        private const uint NET_DVR_GET_NETCFG_V30 = 1000;
        private const uint NET_DVR_SET_NETCFG_V30 = 1001;
        private const int NET_DVR_SET_NETCFG_CHANNEL = 0;

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool NET_DVR_GetDVRConfig(
            int lUserID,
            uint dwCommand,
            int lChannel,
            IntPtr lpOutBuffer,
            uint dwOutBufferSize,
            ref uint lpBytesReturned);

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool NET_DVR_SetDVRConfig(
            int lUserID,
            uint dwCommand,
            int lChannel,
            IntPtr lpInBuffer,
            uint dwInBufferSize);

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool NET_DVR_Logout_V30(int lUserID);

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern bool NET_DVR_RebootDVR(int lUserID);

        public static bool ChangeNetworkAddress(
            string currentIp,
            string username,
            string password,
            string newIp,
            string subnetMask,
            string gateway,
            bool enableDhcp,
            out string message)
        {
            message = "";

            if (string.IsNullOrWhiteSpace(currentIp))
            {
                message = "Current IP address is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                message = "Username is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                message = "Admin password is required.";
                return false;
            }

            if (!enableDhcp)
            {
                if (!IsValidIPv4(newIp))
                {
                    message = "New IP address is not valid.";
                    return false;
                }

                if (!IsValidIPv4(subnetMask))
                {
                    message = "Subnet mask is not valid.";
                    return false;
                }

                if (!IsValidIPv4(gateway))
                {
                    message = "Gateway is not valid.";
                    return false;
                }
            }

            if (!HikvisionSdk.Initialize())
            {
                message = "Hikvision SDK initialization failed. Error: " + HikvisionSdk.GetLastError();
                return false;
            }

            int userId = Login(currentIp, username, password, out message);

            if (userId < 0)
                return false;

            try
            {
                CHCNetSDK.NET_DVR_NETCFG_V30 cfg;
                string getMessage;

                if (!GetNetConfig(userId, out cfg, out getMessage))
                {
                    message = getMessage;
                    return false;
                }

                cfg.dwSize = (uint)Marshal.SizeOf(typeof(CHCNetSDK.NET_DVR_NETCFG_V30));
                cfg.byUseDhcp = enableDhcp ? (byte)1 : (byte)0;

                EnsureNetConfigArrays(ref cfg);

                if (!enableDhcp)
                {
                    cfg.struEtherNet[0].struDVRIP = CreateIpAddress(newIp);
                    cfg.struEtherNet[0].struDVRIPMask = CreateIpAddress(subnetMask);
                    cfg.struGatewayIpAddr = CreateIpAddress(gateway);
                }

                string setMessage;

                if (!SetNetConfig(userId, ref cfg, out setMessage))
                {
                    message = setMessage;
                    return false;
                }

                // Reboot is required for the new network configuration to become active on the NIC.
                // Without this call the camera keeps running on the old IP even though the stored
                // config already reflects the new address (which is why SADP shows the new IP but
                // ping/web/MAC all still point to the old one).
                NET_DVR_RebootDVR(userId);

                message = enableDhcp
                    ? "DHCP enabled. The device is rebooting to apply the new configuration. Wait ~60 seconds, then rescan."
                    : "IP address changed. The device is rebooting to apply the new configuration. Wait ~60 seconds, then rescan.";

                return true;
            }
            finally
            {
                NET_DVR_Logout_V30(userId);
            }
        }

        private static int Login(string ip, string username, string password, out string message)
        {
            message = "";

            // First try the modern V40 login.
            CHCNetSDK.NET_DVR_USER_LOGIN_INFO loginInfo = new CHCNetSDK.NET_DVR_USER_LOGIN_INFO();
            loginInfo.sDeviceAddress = new byte[CHCNetSDK.NET_DVR_DEV_ADDRESS_MAX_LEN];
            loginInfo.sUserName = new byte[CHCNetSDK.NET_DVR_LOGIN_USERNAME_MAX_LEN];
            loginInfo.sPassword = new byte[CHCNetSDK.NET_DVR_LOGIN_PASSWD_MAX_LEN];
            loginInfo.byRes3 = new byte[119];

            CopyText(loginInfo.sDeviceAddress, ip);
            CopyText(loginInfo.sUserName, username);
            CopyText(loginInfo.sPassword, password);

            loginInfo.wPort = 8000;
            loginInfo.byUseTransport = 0;
            loginInfo.bUseAsynLogin = false;
            loginInfo.byLoginMode = 0;
            loginInfo.byHttps = 0;

            CHCNetSDK.NET_DVR_DEVICEINFO_V40 deviceInfoV40 = CreateDeviceInfoV40();

            int userId = CHCNetSDK.NET_DVR_Login_V40(ref loginInfo, ref deviceInfoV40);

            if (userId >= 0)
                return userId;

            uint v40Error = HikvisionSdk.GetLastError();

            // Some devices/firmware versions reject V40 login but accept V30.
            // SADP-era devices commonly work better through V30.
            CHCNetSDK.NET_DVR_DEVICEINFO_V30 deviceInfoV30 = new CHCNetSDK.NET_DVR_DEVICEINFO_V30();
            deviceInfoV30.sSerialNumber = new byte[CHCNetSDK.SERIALNO_LEN];
            deviceInfoV30.byRes2 = new byte[9];

            userId = CHCNetSDK.NET_DVR_Login_V30(
                ip,
                8000,
                username,
                password,
                ref deviceInfoV30);

            if (userId >= 0)
                return userId;

            uint v30Error = HikvisionSdk.GetLastError();

            message =
                "Login failed.\r\n\r\n" +
                "V40 SDK Error: " + v40Error + "\r\n" +
                "V30 SDK Error: " + v30Error + "\r\n\r\n" +
                "Confirm that the device IP is correct, SDK port is 8000, and the admin password is correct.";

            return -1;
        }

        private static bool GetNetConfig(
            int userId,
            out CHCNetSDK.NET_DVR_NETCFG_V30 cfg,
            out string message)
        {
            message = "";
            cfg = new CHCNetSDK.NET_DVR_NETCFG_V30();

            int size = Marshal.SizeOf(typeof(CHCNetSDK.NET_DVR_NETCFG_V30));
            IntPtr buffer = Marshal.AllocHGlobal(size);
            uint returned = 0;

            try
            {
                for (int i = 0; i < size; i++)
                    Marshal.WriteByte(buffer, i, 0);

                bool ok = NET_DVR_GetDVRConfig(
                    userId,
                    NET_DVR_GET_NETCFG_V30,
                    NET_DVR_SET_NETCFG_CHANNEL,
                    buffer,
                    (uint)size,
                    ref returned);

                if (!ok)
                {
                    message = "Failed to read Hikvision network configuration. SDK Error: " + HikvisionSdk.GetLastError();
                    return false;
                }

                cfg = (CHCNetSDK.NET_DVR_NETCFG_V30)Marshal.PtrToStructure(
                    buffer,
                    typeof(CHCNetSDK.NET_DVR_NETCFG_V30));

                EnsureNetConfigArrays(ref cfg);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool SetNetConfig(
            int userId,
            ref CHCNetSDK.NET_DVR_NETCFG_V30 cfg,
            out string message)
        {
            message = "";

            int size = Marshal.SizeOf(typeof(CHCNetSDK.NET_DVR_NETCFG_V30));
            IntPtr buffer = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(cfg, buffer, false);

                bool ok = NET_DVR_SetDVRConfig(
                    userId,
                    NET_DVR_SET_NETCFG_V30,
                    NET_DVR_SET_NETCFG_CHANNEL,
                    buffer,
                    (uint)size);

                if (!ok)
                {
                    message = "Failed to write Hikvision network configuration. SDK Error: " + HikvisionSdk.GetLastError();
                    return false;
                }

                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static CHCNetSDK.NET_DVR_DEVICEINFO_V40 CreateDeviceInfoV40()
        {
            CHCNetSDK.NET_DVR_DEVICEINFO_V40 info = new CHCNetSDK.NET_DVR_DEVICEINFO_V40();
            info.struDeviceV30.sSerialNumber = new byte[CHCNetSDK.SERIALNO_LEN];
            info.struDeviceV30.byRes2 = new byte[9];
            info.byRes2 = new byte[243];
            return info;
        }

        private static void EnsureNetConfigArrays(ref CHCNetSDK.NET_DVR_NETCFG_V30 cfg)
        {
            if (cfg.struEtherNet == null || cfg.struEtherNet.Length != CHCNetSDK.MAX_ETHERNET)
                cfg.struEtherNet = new CHCNetSDK.NET_DVR_ETHERNET_V30[CHCNetSDK.MAX_ETHERNET];

            for (int i = 0; i < cfg.struEtherNet.Length; i++)
            {
                EnsureIpAddress(ref cfg.struEtherNet[i].struDVRIP);
                EnsureIpAddress(ref cfg.struEtherNet[i].struDVRIPMask);

                if (cfg.struEtherNet[i].byMACAddr == null || cfg.struEtherNet[i].byMACAddr.Length != CHCNetSDK.MACADDR_LEN)
                    cfg.struEtherNet[i].byMACAddr = new byte[CHCNetSDK.MACADDR_LEN];

                if (cfg.struEtherNet[i].byRes == null || cfg.struEtherNet[i].byRes.Length != 2)
                    cfg.struEtherNet[i].byRes = new byte[2];
            }

            if (cfg.struRes1 == null || cfg.struRes1.Length != 2)
                cfg.struRes1 = new CHCNetSDK.NET_DVR_IPADDR[2];

            for (int i = 0; i < cfg.struRes1.Length; i++)
                EnsureIpAddress(ref cfg.struRes1[i]);

            EnsureIpAddress(ref cfg.struAlarmHostIpAddr);
            EnsureIpAddress(ref cfg.struDnsServer1IpAddr);
            EnsureIpAddress(ref cfg.struDnsServer2IpAddr);
            EnsureIpAddress(ref cfg.struMulticastIpAddr);
            EnsureIpAddress(ref cfg.struGatewayIpAddr);
            EnsureIpAddress(ref cfg.struPPPoE.struPPPoEIP);

            if (cfg.byRes2 == null || cfg.byRes2.Length != 4)
                cfg.byRes2 = new byte[4];

            if (cfg.byIpResolver == null || cfg.byIpResolver.Length != CHCNetSDK.MAX_DOMAIN_NAME)
                cfg.byIpResolver = new byte[CHCNetSDK.MAX_DOMAIN_NAME];

            if (cfg.struPPPoE.sPPPoEUser == null || cfg.struPPPoE.sPPPoEUser.Length != CHCNetSDK.NAME_LEN)
                cfg.struPPPoE.sPPPoEUser = new byte[CHCNetSDK.NAME_LEN];

            if (cfg.struPPPoE.sPPPoEPassword == null)
                cfg.struPPPoE.sPPPoEPassword = "";

            if (cfg.byRes == null || cfg.byRes.Length != 64)
                cfg.byRes = new byte[64];
        }

        private static void EnsureIpAddress(ref CHCNetSDK.NET_DVR_IPADDR ip)
        {
            if (ip.sIpV4 == null || ip.sIpV4.Length != 16)
                ip.sIpV4 = new byte[16];

            if (ip.byRes == null || ip.byRes.Length != 128)
                ip.byRes = new byte[128];
        }

        private static CHCNetSDK.NET_DVR_IPADDR CreateIpAddress(string ipText)
        {
            CHCNetSDK.NET_DVR_IPADDR ip = new CHCNetSDK.NET_DVR_IPADDR();
            ip.sIpV4 = new byte[16];
            ip.byRes = new byte[128];

            CopyText(ip.sIpV4, ipText);

            return ip;
        }

        private static void CopyText(byte[] destination, string text)
        {
            if (destination == null)
                return;

            Array.Clear(destination, 0, destination.Length);

            if (string.IsNullOrEmpty(text))
                return;

            byte[] data = Encoding.ASCII.GetBytes(text);
            int length = Math.Min(data.Length, destination.Length - 1);
            Array.Copy(data, destination, length);
        }

        private static bool IsValidIPv4(string value)
        {
            IPAddress ip;

            return IPAddress.TryParse(value, out ip) &&
                   ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        }
    }
}
