namespace UniversalScanner.DeviceSDK.Common
{
    public interface IDeviceProvider
    {
        string VendorName { get; }

        bool SupportsVendor(string vendor);

        DeviceInfo GetDeviceInfo(string ipAddress);

        bool CanInitialize(DeviceInfo device);

        bool InitializeDevice(DeviceInfo device, string username, string newPassword, out string message);
    }
}