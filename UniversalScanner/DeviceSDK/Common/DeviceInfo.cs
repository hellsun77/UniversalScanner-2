namespace UniversalScanner.DeviceSDK.Common
{
    public class DeviceInfo
    {
        public string Vendor { get; set; }
        public string IPAddress { get; set; }
        public string MACAddress { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string FirmwareVersion { get; set; }
        public int HttpPort { get; set; }
        public int SdkPort { get; set; }
        public DeviceActivationStatus ActivationStatus { get; set; }

        public DeviceInfo()
        {
            Vendor = "";
            IPAddress = "";
            MACAddress = "";
            Model = "";
            SerialNumber = "";
            FirmwareVersion = "";
            HttpPort = 80;
            SdkPort = 8000;
            ActivationStatus = DeviceActivationStatus.Unknown;
        }
    }
}