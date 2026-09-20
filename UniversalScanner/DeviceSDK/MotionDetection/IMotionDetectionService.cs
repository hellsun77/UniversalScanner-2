using System;
using System.Collections.Generic;

namespace UniversalScanner.DeviceSDK.MotionDetection
{
    public class MotionDetectionConfiguration
    {
        public bool Enabled { get; set; }
        public int? Columns { get; set; }
        public int? Rows { get; set; }
        public bool[] ActiveCells { get; set; }
        public int? Sensitivity { get; set; }
        public int? SensitivityMinimum { get; set; }
        public int? SensitivityMaximum { get; set; }
        public string ConfigurationSource { get; set; }
        public string Channel { get; set; }
        public bool IsFullImage { get; set; }
        public bool IsSupported { get; set; }
        public System.Collections.Generic.List<System.Drawing.PointF> Polygon { get; set; }
        
        public MotionDetectionConfiguration()
        {
            IsSupported = true;
        }
    }

    public class MotionDetectionCapabilities
    {
        public bool SupportsEnableDisable { get; set; }
        public bool SupportsGrid { get; set; }
        public bool SupportsSensitivity { get; set; }
        public bool SupportsRead { get; set; }
        public bool SupportsWrite { get; set; }
        public bool SupportsVerification { get; set; }
        
        public bool SupportsFullImage { get; set; }
        public bool SupportsCustomGrid { get; set; }
        public bool SupportsClearArea { get; set; }
        public bool SupportsGridPreview { get; set; }
        public bool SupportsPolygon { get; set; }
    }

    public class MotionDetectionApplyRequest
    {
        public bool Enabled { get; set; }
        public bool[] ActiveCells { get; set; }
        public int? Sensitivity { get; set; }
        public System.Collections.Generic.List<System.Drawing.PointF> Polygon { get; set; }
    }

    public class MotionDetectionOperationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string ErrorClassification { get; set; }
        public MotionDetectionConfiguration VerifiedConfiguration { get; set; }

        public static MotionDetectionOperationResult Fail(string classification, string message)
        {
            return new MotionDetectionOperationResult
            {
                Success = false,
                ErrorClassification = classification,
                ErrorMessage = message
            };
        }
    }

    public interface IMotionDetectionService
    {
        MotionDetectionCapabilities GetCapabilities();
        System.Threading.Tasks.Task<MotionDetectionConfiguration> ReadConfigurationAsync(string ip, string username, string password);
        System.Threading.Tasks.Task<MotionDetectionOperationResult> ApplyConfigurationAsync(string ip, string username, string password, MotionDetectionApplyRequest request);
        System.Threading.Tasks.Task<MotionDetectionOperationResult> RestoreRollbackAsync(string ip, string username, string password);
    }
}
