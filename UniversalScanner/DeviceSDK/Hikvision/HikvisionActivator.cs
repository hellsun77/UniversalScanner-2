using System;

namespace UniversalScanner.DeviceSDK.Hikvision
{
    public static class HikvisionActivator
    {
        public static bool ActivateDevice(
            string ipAddress,
            string newPassword,
            out string message)
        {
            message = "";

            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                message = "IP address is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                message = "Password is empty.";
                return false;
            }

            if (newPassword.Length < 8 || newPassword.Length > 16)
            {
                message = "Hikvision password must be between 8 and 16 characters.";
                return false;
            }

            if (!HikvisionSdk.Initialize())
            {
                message = "Hikvision SDK initialization failed. Error: " +
                          HikvisionSdk.GetLastError();
                return false;
            }

            HikvisionNative.NET_DVR_ACTIVATECFG cfg =
                HikvisionNative.NET_DVR_ACTIVATECFG.Create(newPassword);

            bool ok = HikvisionNative.NET_DVR_ActivateDevice(
                ipAddress,
                8000,
                ref cfg);

            if (!ok)
            {
                message = "Activation failed. SDK Error: " +
                          HikvisionSdk.GetLastError();
                return false;
            }

            message = "Hikvision device activated successfully.";
            return true;
        }
    }
}