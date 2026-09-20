using System;

namespace UniversalScanner.DeviceSDK.Hikvision
{
    public class ProvisionResult
    {
        public bool Success { get; set; }
        public bool IsPartialSuccess { get; set; }
        public string Stage { get; set; }
        public int HttpStatusCode { get; set; }
        public string HikvisionStatusCode { get; set; }
        public string HikvisionStatusString { get; set; }
        public string HikvisionSubStatusCode { get; set; }
        public string ErrorMessage { get; set; }
    }
}
