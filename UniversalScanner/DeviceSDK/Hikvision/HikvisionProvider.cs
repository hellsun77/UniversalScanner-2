using System;
using UniversalScanner.DeviceSDK.Common;

namespace UniversalScanner.DeviceSDK.Hikvision
{
    public class HikvisionProvider : IDeviceProvider
    {
        public string VendorName => "Hikvision";

        public bool SupportsVendor(string vendor)
        {
            return vendor != null &&
                   vendor.Equals("Hikvision", StringComparison.OrdinalIgnoreCase);
        }

        public DeviceInfo GetDeviceInfo(string ipAddress)
        {
            return new DeviceInfo()
            {
                Vendor = VendorName,
                IPAddress = ipAddress,
                ActivationStatus = DeviceActivationStatus.Unknown
            };
        }

        public bool CanInitialize(DeviceInfo device)
        {
            return device != null &&
                   device.ActivationStatus == DeviceActivationStatus.Inactive;
        }

        public bool InitializeDevice(
            DeviceInfo device,
            string username,
            string newPassword,
            out string message)
        {
            message = "Hikvision activation is not implemented yet.";
            return false;
        }
    }
}