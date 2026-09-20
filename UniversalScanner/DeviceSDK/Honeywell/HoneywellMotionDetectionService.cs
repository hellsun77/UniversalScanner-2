using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Linq;
using UniversalScanner.DeviceSDK.Honeywell;
using UniversalScanner.DeviceSDK.MotionDetection;

namespace UniversalScanner.DeviceSDK.Honeywell
{
    public class HoneywellMotionDetectionService : IMotionDetectionService
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
                
                XDocument vacDoc = XDocument.Parse(vacResp);
                var configNode = vacDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Configurations");
                if (configNode == null || configNode.Attribute("token") == null)
                    throw new Exception("Could not locate VideoAnalyticsConfiguration token in response.\r\nResponse: " + vacResp);
                
                string vacToken = (string)configNode.Attribute("token");
                _lastVacToken = vacToken;

                // Parse grid dimensions and sensitivity from the CellMotionEngine module
                int? cols = null;
                int? rows = null;
                int? sensitivity = null;

                var engineNodes = vacDoc.Descendants().Where(e => e.Name.LocalName == "AnalyticsModule" && e.Attribute("Type") != null && ((string)e.Attribute("Type")).EndsWith(":CellMotionEngine")).ToList();
                if (engineNodes.Count == 0)
                {
                    // If modules are not inline in VAC, fetch them explicitly
                    string getModsReq = "<tan:GetAnalyticsModules xmlns:tan=\"http://www.onvif.org/ver20/analytics/wsdl\"><tan:ConfigurationToken>" + vacToken + "</tan:ConfigurationToken></tan:GetAnalyticsModules>";
                    string modsResp = await OnvifSoap.SendAsync(ip, username, password, getModsReq, "/onvif/analytics");
                    XDocument modsDoc = XDocument.Parse(modsResp);
                    engineNodes = modsDoc.Descendants().Where(e => e.Name.LocalName == "AnalyticsModule" && e.Attribute("Type") != null && ((string)e.Attribute("Type")).EndsWith(":CellMotionEngine")).ToList();
                }

                if (engineNodes.Count > 1)
                    throw new Exception("Multiple CellMotionEngine modules found for this video configuration. Manual selection is not yet supported.");

                if (engineNodes.Count == 1)
                {
                    var engineNode = engineNodes[0];
                    _lastSensitivityXml = engineNode.ToString(SaveOptions.DisableFormatting); // Cache for rollback

                    var cellLayout = engineNode.Descendants().FirstOrDefault(e => e.Name.LocalName == "CellLayout");
                    if (cellLayout != null)
                    {
                        if (cellLayout.Attribute("Columns") != null) cols = (int)cellLayout.Attribute("Columns");
                        if (cellLayout.Attribute("Rows") != null) rows = (int)cellLayout.Attribute("Rows");
                    }

                    var sensItem = engineNode.Descendants().FirstOrDefault(e => e.Name.LocalName == "SimpleItem" && (string)e.Attribute("Name") == "Sensitivity");
                    if (sensItem != null && sensItem.Attribute("Value") != null)
                    {
                        sensitivity = (int)sensItem.Attribute("Value");
                    }
                }
                else
                {
                    throw new Exception("No matching AnalyticsModule Type=\"tt:CellMotionEngine\"");
                }

                if (cols == null || rows == null || cols <= 0 || rows <= 0)
                    throw new Exception("Could not determine dynamic grid dimensions from camera configuration.");

                // Get Rules
                string getRulesReq = "<tan:GetRules xmlns:tan=\"http://www.onvif.org/ver20/analytics/wsdl\"><tan:ConfigurationToken>" + vacToken + "</tan:ConfigurationToken></tan:GetRules>";
                string rulesResp = await OnvifSoap.SendAsync(ip, username, password, getRulesReq, "/onvif/analytics");

                XDocument rulesDoc = XDocument.Parse(rulesResp);
                var ruleNodes = rulesDoc.Descendants().Where(e => e.Name.LocalName == "Rule" && e.Attribute("Type") != null && ((string)e.Attribute("Type")).EndsWith(":CellMotionDetector")).ToList();
                if (ruleNodes.Count == 0)
                    throw new Exception("Could not find a CellMotionDetector rule in ONVIF rules.\r\nRules Response: " + rulesResp);

                if (ruleNodes.Count > 1)
                    throw new Exception("Multiple CellMotionDetector rules found for this video configuration. Manual selection is not yet supported.");

                var ruleNode = ruleNodes[0];
                _lastActiveCellsXml = ruleNode.ToString(SaveOptions.DisableFormatting); // Cache for rollback

                var cellsItem = ruleNode.Descendants().FirstOrDefault(e => e.Name.LocalName == "SimpleItem" && (string)e.Attribute("Name") == "ActiveCells");
                if (cellsItem == null || cellsItem.Attribute("Value") == null)
                    throw new Exception("Could not find ActiveCells bitmask in motion detector rule.\r\nRule XML: " + _lastActiveCellsXml);

                string activeCellsBase64 = (string)cellsItem.Attribute("Value");
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
                
                string wrappedRuleXml = "<root xmlns:tt=\"http://www.onvif.org/ver10/schema\" xmlns:tan=\"http://www.onvif.org/ver20/analytics/wsdl\">" + _lastActiveCellsXml + "</root>";
                XDocument ruleDoc = XDocument.Parse(wrappedRuleXml);
                var cellsItemToUpdate = ruleDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "SimpleItem" && (string)e.Attribute("Name") == "ActiveCells");
                if (cellsItemToUpdate != null) cellsItemToUpdate.SetAttributeValue("Value", base64Mask);
                string newRuleXml = ruleDoc.Root.Elements().First().ToString(SaveOptions.DisableFormatting);

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
                    string wrappedEngineXml = "<root xmlns:tt=\"http://www.onvif.org/ver10/schema\" xmlns:tan=\"http://www.onvif.org/ver20/analytics/wsdl\">" + _lastSensitivityXml + "</root>";
                    XDocument engineDoc = XDocument.Parse(wrappedEngineXml);
                    var sensItemToUpdate = engineDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "SimpleItem" && (string)e.Attribute("Name") == "Sensitivity");
                    if (sensItemToUpdate != null) sensItemToUpdate.SetAttributeValue("Value", request.Sensitivity.Value.ToString());
                    
                    string newEngineXml = engineDoc.Root.Elements().First().ToString(SaveOptions.DisableFormatting);
                    // Ensure the root element is <tan:AnalyticsModule> for the ModifyAnalyticsModules request
                    newEngineXml = Regex.Replace(newEngineXml, @"^<[^>]*:AnalyticsModule", "<tan:AnalyticsModule");
                    newEngineXml = Regex.Replace(newEngineXml, @"</[^>]*:AnalyticsModule>$", "</tan:AnalyticsModule>");
                    
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
                string failReason = "";
                
                if (caps.SupportsEnableDisable && verifyConfig.Enabled != request.Enabled) 
                {
                    verified = false;
                    failReason = $"Enabled mismatch (Expected: {request.Enabled}, Actual: {verifyConfig.Enabled})";
                }
                
                if (request.Sensitivity.HasValue && verifyConfig.Sensitivity != request.Sensitivity.Value) 
                {
                    verified = false;
                    if (string.IsNullOrEmpty(failReason)) failReason = $"Sensitivity mismatch (Expected: {request.Sensitivity.Value}, Actual: {verifyConfig.Sensitivity})";
                }
                
                string newBase64Mask = EncodeActiveCells(request.ActiveCells);
                
                string wrappedRuleXmlCheck = "<root xmlns:tt=\"http://www.onvif.org/ver10/schema\" xmlns:tan=\"http://www.onvif.org/ver20/analytics/wsdl\">" + _lastActiveCellsXml + "</root>";
                XDocument ruleDocCheck = XDocument.Parse(wrappedRuleXmlCheck);
                var originalCellsItem = ruleDocCheck.Descendants().FirstOrDefault(e => e.Name.LocalName == "SimpleItem" && (string)e.Attribute("Name") == "ActiveCells");
                string originalBase64 = originalCellsItem != null && originalCellsItem.Attribute("Value") != null ? (string)originalCellsItem.Attribute("Value") : "";
                
                // Only verify if the user intentionally sent a modified grid
                if (newBase64Mask != originalBase64)
                {
                    if (verifyConfig.ActiveCells.Length != request.ActiveCells.Length) 
                    {
                        verified = false;
                        if (string.IsNullOrEmpty(failReason)) failReason = $"ActiveCells length mismatch (Expected: {request.ActiveCells.Length}, Actual: {verifyConfig.ActiveCells.Length})";
                    }
                    else
                    {
                        int diffCount = 0;
                        for (int i = 0; i < request.ActiveCells.Length; i++)
                        {
                            if (verifyConfig.ActiveCells[i] != request.ActiveCells[i])
                            {
                                diffCount++;
                            }
                        }
                        if (diffCount > 0)
                        {
                            verified = false;
                            if (string.IsNullOrEmpty(failReason)) failReason = $"ActiveCells content mismatch ({diffCount} cells differ).";
                        }
                    }
                }
                
                if (!verified)
                {
                    var failRes = MotionDetectionOperationResult.Fail("Verification Failed", failReason);
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
                string engineXml = Regex.Replace(_lastSensitivityXml, @"^<[^>]*:AnalyticsModule", "<tan:AnalyticsModule");
                engineXml = Regex.Replace(engineXml, @"</[^>]*:AnalyticsModule>$", "</tan:AnalyticsModule>");
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
