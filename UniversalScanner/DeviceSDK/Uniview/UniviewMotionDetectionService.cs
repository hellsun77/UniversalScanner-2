using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using UniversalScanner.DeviceSDK.Honeywell;
using UniversalScanner.DeviceSDK.MotionDetection;

namespace UniversalScanner.DeviceSDK.Uniview
{
    public class UniviewMotionDetectionService : IMotionDetectionService
    {
        private string _lastVacToken = null;
        private string _lastActiveCellsXml = null;
        private string _lastSensitivityXml = null;

        private bool _supportsCustomGrid = true;
        private bool _supportsClearArea = true;

        public MotionDetectionCapabilities GetCapabilities()
        {
            return new MotionDetectionCapabilities
            {
                SupportsEnableDisable = false, // The enable/disable mechanism has not yet been proven
                SupportsGrid = true,
                SupportsSensitivity = true,
                SupportsRead = true,
                SupportsWrite = true,
                SupportsVerification = true,
                SupportsFullImage = true,
                SupportsCustomGrid = _supportsCustomGrid,
                SupportsClearArea = _supportsClearArea,
                SupportsGridPreview = true
            };
        }

        public async Task<MotionDetectionConfiguration> ReadConfigurationAsync(string ip, string username, string password)
        {
            try
            {
                // Get VideoAnalyticsConfigurations
                string getVacReq = "<GetVideoAnalyticsConfigurations xmlns=\"http://www.onvif.org/ver10/media/wsdl\" />";
                string vacResp = await OnvifSoap.SendAsync(ip, username, password, getVacReq, "/onvif/media");
                
                Match tokenMatch = Regex.Match(vacResp, @"<trt:Configurations[^>]*token=""([^""]+)""");
                if (!tokenMatch.Success)
                    throw new Exception("Could not locate VideoAnalyticsConfiguration token in response.");
                
                string vacToken = tokenMatch.Groups[1].Value;
                _lastVacToken = vacToken;

                // Parse grid dimensions and sensitivity from the CellMotionEngine module
                int? cols = null;
                int? rows = null;
                int? sensitivity = null;

                Match engineMatch = Regex.Match(vacResp, @"(?s)<tt:AnalyticsModule[^>]*Name=""MyCellMotion""[^>]*>.*?</tt:AnalyticsModule>");
                if (engineMatch.Success)
                {
                    string engineXml = engineMatch.Groups[0].Value;
                    _lastSensitivityXml = engineXml; // Cache for rollback

                    Match layoutMatch = Regex.Match(engineXml, @"<tt:CellLayout[^>]*Columns=""(\d+)""[^>]*Rows=""(\d+)""");
                    if (layoutMatch.Success)
                    {
                        cols = int.Parse(layoutMatch.Groups[1].Value);
                        rows = int.Parse(layoutMatch.Groups[2].Value);
                    }

                    Match sensMatch = Regex.Match(engineXml, @"<tt:SimpleItem[^>]*Value=""(\d+)""[^>]*Name=""Sensitivity""");
                    if (sensMatch.Success)
                    {
                        sensitivity = int.Parse(sensMatch.Groups[1].Value);
                    }
                }

                if (cols == null || rows == null || cols <= 0 || rows <= 0)
                    throw new Exception("Could not determine dynamic grid dimensions from camera configuration.");

                // Get Rules
                string getRulesReq = "<tan:GetRules xmlns:tan=\"http://www.onvif.org/ver20/analytics/wsdl\"><tan:ConfigurationToken>" + vacToken + "</tan:ConfigurationToken></tan:GetRules>";
                string rulesResp = await OnvifSoap.SendAsync(ip, username, password, getRulesReq, "/onvif/analytics");

                Match ruleMatch = Regex.Match(rulesResp, @"(?s)<tan:Rule[^>]*Name=""MyMotionDetectorRule""[^>]*Type=""tt:CellMotionDetector"">.*?</tan:Rule>");
                if (!ruleMatch.Success)
                    throw new Exception("Could not find MyMotionDetectorRule in ONVIF rules.");

                string ruleXml = ruleMatch.Groups[0].Value;
                _lastActiveCellsXml = ruleXml; // Cache for rollback

                Match cellsMatch = Regex.Match(ruleXml, @"<tt:SimpleItem[^>]*Value=""([^""]*)""[^>]*Name=""ActiveCells""");
                if (!cellsMatch.Success)
                    throw new Exception("Could not find ActiveCells bitmask in motion detector rule.");

                string activeCellsBase64 = cellsMatch.Groups[1].Value;
                bool[] grid = DecodeActiveCells(activeCellsBase64, cols.Value, rows.Value);

                bool isEnabled = false;
                bool isFullImage = true;
                foreach (bool cell in grid)
                {
                    if (cell) isEnabled = true;
                    else isFullImage = false;
                }

                return new MotionDetectionConfiguration
                {
                    Enabled = isEnabled,
                    Columns = cols,
                    Rows = rows,
                    ActiveCells = grid,
                    Sensitivity = sensitivity,
                    SensitivityMinimum = 0,
                    SensitivityMaximum = 100,
                    ConfigurationSource = "ONVIF VideoAnalytics",
                    Channel = "1",
                    IsFullImage = isEnabled && isFullImage,
                    IsSupported = true
                };
            }
            catch (Exception ex)
            {
                return new MotionDetectionConfiguration { IsSupported = false };
            }
        }

        public async Task<MotionDetectionOperationResult> ApplyConfigurationAsync(string ip, string username, string password, MotionDetectionApplyRequest request)
        {
            if (string.IsNullOrEmpty(_lastVacToken) || string.IsNullOrEmpty(_lastActiveCellsXml) || string.IsNullOrEmpty(_lastSensitivityXml))
                return MotionDetectionOperationResult.Fail("State Error", "Original configuration state is missing. Please read configuration first.");

            if (request.ActiveCells == null)
                return MotionDetectionOperationResult.Fail("Invalid Request", "ActiveCells grid cannot be null.");

            try
            {
                // Update Rule (ActiveCells)
                string base64Mask = EncodeActiveCells(request.ActiveCells);
                string newRuleXml = Regex.Replace(_lastActiveCellsXml, @"(<tt:SimpleItem[^>]*Value="")([^""]*)(""[^>]*Name=""ActiveCells"")", "${1}" + base64Mask + "${3}");

                string modRulesReq = "<tan:ModifyRules xmlns:tan=\"http://www.onvif.org/ver20/analytics/wsdl\" xmlns:tt=\"http://www.onvif.org/ver10/schema\"><tan:ConfigurationToken>" + _lastVacToken + "</tan:ConfigurationToken>" + newRuleXml + "</tan:ModifyRules>";
                string modRulesResp = await OnvifSoap.SendAsync(ip, username, password, modRulesReq, "/onvif/analytics");

                if (modRulesResp.Contains("Fault>"))
                {
                    _supportsCustomGrid = false;
                    _supportsClearArea = false;
                    return MotionDetectionOperationResult.Fail("Custom motion-region write rejected by camera", "Camera rejected the ModifyRules request for the motion grid.");
                }

                // Update Module (Sensitivity)
                if (request.Sensitivity.HasValue)
                {
                    string newEngineXml = Regex.Replace(_lastSensitivityXml, @"(<tt:SimpleItem[^>]*Value="")(\d+)(""[^>]*Name=""Sensitivity"")", "${1}" + request.Sensitivity.Value + "${3}");
                    // Uniview Analytics Engine Modify requires <tan:AnalyticsModule> instead of <tt:AnalyticsModule>
                    newEngineXml = newEngineXml.Replace("<tt:AnalyticsModule", "<tan:AnalyticsModule").Replace("</tt:AnalyticsModule>", "</tan:AnalyticsModule>");
                    
                    string modEngineReq = "<tan:ModifyAnalyticsModules xmlns:tan=\"http://www.onvif.org/ver20/analytics/wsdl\" xmlns:tt=\"http://www.onvif.org/ver10/schema\"><tan:ConfigurationToken>" + _lastVacToken + "</tan:ConfigurationToken>" + newEngineXml + "</tan:ModifyAnalyticsModules>";
                    string modEngineResp = await OnvifSoap.SendAsync(ip, username, password, modEngineReq, "/onvif/analytics");

                    if (modEngineResp.Contains("Fault>"))
                        return MotionDetectionOperationResult.Fail("SOAP Fault", "Camera rejected the ModifyAnalyticsModules request for sensitivity.");
                }

                // Verify
                var verifyConfig = await ReadConfigurationAsync(ip, username, password);
                if (!verifyConfig.IsSupported)
                    return MotionDetectionOperationResult.Fail("Verification Failed", "Failed to read back configuration for verification.");

                var caps = GetCapabilities();
                bool verified = true;
                
                if (caps.SupportsEnableDisable && verifyConfig.Enabled != request.Enabled) 
                    verified = false;
                
                if (request.Sensitivity.HasValue && verifyConfig.Sensitivity != request.Sensitivity.Value) 
                    verified = false;
                
                string newBase64Mask = EncodeActiveCells(request.ActiveCells);
                Match cellsMatch = Regex.Match(_lastActiveCellsXml, @"<tt:SimpleItem[^>]*Value=""([^""]*)""[^>]*Name=""ActiveCells""");
                string originalBase64 = cellsMatch.Success ? cellsMatch.Groups[1].Value : "";
                
                // Only verify if the user intentionally sent a modified grid
                if (newBase64Mask != originalBase64)
                {
                    if (verifyConfig.ActiveCells.Length != request.ActiveCells.Length) verified = false;
                    else
                    {
                        for (int i = 0; i < request.ActiveCells.Length; i++)
                        {
                            if (verifyConfig.ActiveCells[i] != request.ActiveCells[i])
                            {
                                verified = false;
                                break;
                            }
                        }
                    }
                }

                if (!verified)
                {
                    var failRes = MotionDetectionOperationResult.Fail("Verification Failed", "The camera accepted the command but the values did not persist.");
                    failRes.VerifiedConfiguration = verifyConfig;
                    return failRes;
                }

                return new MotionDetectionOperationResult
                {
                    Success = true,
                    VerifiedConfiguration = verifyConfig
                };
            }
            catch (Exception ex)
            {
                return MotionDetectionOperationResult.Fail("Network Error", ex.Message);
            }
        }

        public async Task<MotionDetectionOperationResult> RestoreRollbackAsync(string ip, string username, string password)
        {
            if (string.IsNullOrEmpty(_lastVacToken) || string.IsNullOrEmpty(_lastActiveCellsXml) || string.IsNullOrEmpty(_lastSensitivityXml))
                return MotionDetectionOperationResult.Fail("State Error", "Original configuration state is missing.");

            try
            {
                // Restore Rule
                string modRulesReq = "<tan:ModifyRules xmlns:tan=\"http://www.onvif.org/ver20/analytics/wsdl\" xmlns:tt=\"http://www.onvif.org/ver10/schema\"><tan:ConfigurationToken>" + _lastVacToken + "</tan:ConfigurationToken>" + _lastActiveCellsXml + "</tan:ModifyRules>";
                await OnvifSoap.SendAsync(ip, username, password, modRulesReq, "/onvif/analytics");

                // Restore Engine
                string engineXml = _lastSensitivityXml.Replace("<tt:AnalyticsModule", "<tan:AnalyticsModule").Replace("</tt:AnalyticsModule>", "</tan:AnalyticsModule>");
                string modEngineReq = "<tan:ModifyAnalyticsModules xmlns:tan=\"http://www.onvif.org/ver20/analytics/wsdl\" xmlns:tt=\"http://www.onvif.org/ver10/schema\"><tan:ConfigurationToken>" + _lastVacToken + "</tan:ConfigurationToken>" + engineXml + "</tan:ModifyAnalyticsModules>";
                await OnvifSoap.SendAsync(ip, username, password, modEngineReq, "/onvif/analytics");

                return new MotionDetectionOperationResult { Success = true };
            }
            catch (Exception ex)
            {
                return MotionDetectionOperationResult.Fail("Rollback Error", ex.Message);
            }
        }

        private bool[] DecodeActiveCells(string base64, int cols, int rows)
        {
            int totalBits = cols * rows;
            bool[] grid = new bool[totalBits];
            if (string.IsNullOrEmpty(base64)) return grid;

            byte[] bytes = Convert.FromBase64String(base64);
            int bitIndex = 0;

            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                for (int bit = 7; bit >= 0; bit--)
                {
                    if (bitIndex < totalBits)
                    {
                        grid[bitIndex] = ((b >> bit) & 1) == 1;
                        bitIndex++;
                    }
                }
            }

            return grid;
        }

        private string EncodeActiveCells(bool[] grid)
        {
            int numBytes = (int)Math.Ceiling(grid.Length / 8.0);
            byte[] bytes = new byte[numBytes];

            for (int i = 0; i < grid.Length; i++)
            {
                if (grid[i])
                {
                    int byteIndex = i / 8;
                    int bitIndex = 7 - (i % 8);
                    bytes[byteIndex] |= (byte)(1 << bitIndex);
                }
            }
            return Convert.ToBase64String(bytes);
        }
    }
}
