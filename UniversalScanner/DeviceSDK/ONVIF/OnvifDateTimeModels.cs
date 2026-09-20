using System;
using System.Collections.Generic;

namespace UniversalScanner.DeviceSDK.ONVIF
{
    public enum OnvifDateTimeType
    {
        Unknown,
        Manual,
        NTP
    }

    public enum OnvifDateTimeFailureType
    {
        None,
        AuthenticationFailed,
        EndpointNotFound,
        Timeout,
        ConnectionFailed,
        PermissionDenied,
        ActionNotSupported,
        SoapFault,
        InvalidResponse,
        UnknownError
    }

    public class OnvifDateTimeConfiguration
    {
        public bool IsSupported { get; set; } = true;
        public bool IsWriteSupported { get; set; } = true;
        public OnvifDateTimeFailureType FailureType { get; set; } = OnvifDateTimeFailureType.None;
        public string ErrorMessage { get; set; } = "";
        public OnvifDateTimeType DateTimeType { get; set; }
        public bool DaylightSavings { get; set; }
        public string TimeZone { get; set; }
        
        public DateTime? LocalTime { get; set; }
        public DateTime? UtcTime { get; set; }

        public bool NtpSupported { get; set; } = true;
        public List<string> NtpServers { get; set; } = new List<string>();
        public bool NtpPortSupported { get; set; } = false;
        public int NtpPort { get; set; } = 123;
        public bool NtpIntervalSupported { get; set; } = false;
        public int NtpInterval { get; set; } = 600;
    }

    public class OnvifManualDateTimeSettings
    {
        public DateTime UtcTime { get; set; }
        public DateTime LocalTime { get; set; }
        public string TimeZone { get; set; }
        public bool DaylightSavings { get; set; }
        public bool IsHikvisionWorkaround { get; set; } = false;
    }

    public class OnvifNtpDateTimeSettings
    {
        public List<string> NtpServers { get; set; } = new List<string>();
        public string TimeZone { get; set; }
        public bool DaylightSavings { get; set; }
        public bool IsHikvisionWorkaround { get; set; } = false;
    }

    public enum BatchCameraStatus
    {
        Pending,
        Connecting,
        Reading,
        Applying,
        Verifying,
        Success,
        Failed,
        AuthenticationFailed,
        Unsupported,
        Cancelled,
        Timeout
    }

    public enum BatchRequestedMode
    {
        SyncToComputer,
        ManualFixed,
        ManualAdvancing,
        NTP
    }

    public class OnvifBatchCameraItem
    {
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public string Vendor { get; set; }
        public string Model { get; set; }
        public BatchCameraStatus Status { get; set; } = BatchCameraStatus.Pending;
        public string CurrentTimeStr { get; set; } = "";
        public BatchRequestedMode RequestedMode { get; set; }
        public string Result { get; set; } = "";
        public string VerifiedTimeStr { get; set; } = "";
        public string DifferenceStr { get; set; } = "";
        public string Error { get; set; } = "";
        public string NtpServer { get; set; } = "";
        public DateTime? BeforeUtc { get; set; }
        public DateTime? AfterUtc { get; set; }
    }
}
