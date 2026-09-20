using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using UniversalScanner.DeviceSDK.ONVIF;

namespace UniversalScanner.DeviceSDK.MotionDetection
{
    public class GenericOnvifMotionDetectionService : IMotionDetectionService
    {
        private static readonly XNamespace NsTrt = "http://www.onvif.org/ver10/media/wsdl";
        private static readonly XNamespace NsTt = "http://www.onvif.org/ver10/schema";
        private static readonly XNamespace NsTan = "http://www.onvif.org/ver20/analytics/wsdl";

        private string _configurationToken;
        private string _originalRuleXml;
        private float _scaleX = 1.0f;
        private float _scaleY = 1.0f;
        private float _translateX = -1.0f;
        private float _translateY = -1.0f;
        private bool _supportsEnableDisable = false;
        private bool _supportsSensitivity = false;

        public MotionDetectionCapabilities GetCapabilities()
        {
            return new MotionDetectionCapabilities
            {
                SupportsEnableDisable = _supportsEnableDisable,
                SupportsGrid = false,
                SupportsSensitivity = _supportsSensitivity,
                SupportsRead = true,
                SupportsWrite = true,
                SupportsVerification = false,
                SupportsFullImage = true,
                SupportsCustomGrid = false,
                SupportsClearArea = true,
                SupportsGridPreview = false,
                SupportsPolygon = true
            };
        }

        public async Task<MotionDetectionConfiguration> ReadConfigurationAsync(string ip, string username, string password)
        {
            var config = new MotionDetectionConfiguration
            {
                IsSupported = false,
                ConfigurationSource = "Generic ONVIF (Polygon Editing)",
                Channel = "1"
            };

            try
            {
                using (var client = new OnvifSoapClient())
                {
                    string mediaUrl = $"http://{ip}/onvif/media_service";
                    string getVacBody = "<GetVideoAnalyticsConfigurations xmlns=\"http://www.onvif.org/ver10/media/wsdl\"/>";
                    string vacResponse = await client.PostSoapAsync(mediaUrl, getVacBody, "http://www.onvif.org/ver10/media/wsdl/GetVideoAnalyticsConfigurations", username, password, CancellationToken.None);

                    if (string.IsNullOrWhiteSpace(vacResponse))
                        return config;

                    var docVac = XDocument.Parse(vacResponse);
                    var vacElement = docVac.Descendants(NsTrt + "Configurations").FirstOrDefault() ??
                                     docVac.Descendants(NsTt + "VideoAnalyticsConfiguration").FirstOrDefault();
                    
                    if (vacElement == null || vacElement.Attribute("token") == null)
                        return config;

                    string token = vacElement.Attribute("token").Value;
                    _configurationToken = token;

                    string analyticsUrl = $"http://{ip}/onvif/analytics_service";
                    string getModulesBody = $"<tan:GetAnalyticsModules xmlns:tan=\"http://www.onvif.org/ver20/analytics/wsdl\"><tan:ConfigurationToken>{token}</tan:ConfigurationToken></tan:GetAnalyticsModules>";
                    string getRulesBody = $"<tan:GetRules xmlns:tan=\"http://www.onvif.org/ver20/analytics/wsdl\"><tan:ConfigurationToken>{token}</tan:ConfigurationToken></tan:GetRules>";

                    string modulesResponse = await client.PostSoapAsync(analyticsUrl, getModulesBody, "http://www.onvif.org/ver20/analytics/wsdl/GetAnalyticsModules", username, password, CancellationToken.None);
                    string rulesResponse = await client.PostSoapAsync(analyticsUrl, getRulesBody, "http://www.onvif.org/ver20/analytics/wsdl/GetRules", username, password, CancellationToken.None);

                    float scaleX = 1.0f;
                    float scaleY = 1.0f;
                    float translateX = -1.0f;
                    float translateY = -1.0f;

                    if (!string.IsNullOrWhiteSpace(modulesResponse))
                    {
                        var docMod = XDocument.Parse(modulesResponse);
                        var engineMod = docMod.Descendants(NsTan + "AnalyticsModule")
                            .FirstOrDefault(e => e.Attribute("Type")?.Value.EndsWith("MotionRegionEngine") == true);

                        if (engineMod != null)
                        {
                            var transform = engineMod.Descendants(NsTt + "Transformation").FirstOrDefault();
                            if (transform != null)
                            {
                                var scale = transform.Element(NsTt + "Scale");
                                if (scale != null)
                                {
                                    float.TryParse(scale.Attribute("x")?.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scaleX);
                                    float.TryParse(scale.Attribute("y")?.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scaleY);
                                }
                                var translate = transform.Element(NsTt + "Translate");
                                if (translate != null)
                                {
                                    float.TryParse(translate.Attribute("x")?.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out translateX);
                                    float.TryParse(translate.Attribute("y")?.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out translateY);
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(rulesResponse))
                    {
                        var docRules = XDocument.Parse(rulesResponse);
                        var regionRule = docRules.Descendants(NsTan + "Rule")
                            .FirstOrDefault(e => e.Attribute("Type")?.Value.EndsWith("MotionRegionDetector") == true);

                        if (regionRule != null)
                        {
                            _originalRuleXml = regionRule.ToString();
                            _scaleX = scaleX;
                            _scaleY = scaleY;
                            _translateX = translateX;
                            _translateY = translateY;

                            var polygonElem = regionRule.Descendants(NsTt + "Polygon").FirstOrDefault();
                            if (polygonElem != null)
                            {
                                var points = new List<PointF>();
                                foreach (var pt in polygonElem.Elements(NsTt + "Point"))
                                {
                                    if (float.TryParse(pt.Attribute("x")?.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                                        float.TryParse(pt.Attribute("y")?.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
                                    {
                                        // Map using the camera's coordinates (which are often -1 to 1 after transform)
                                        float onvifX = (x * scaleX) + translateX;
                                        float onvifY = (y * scaleY) + translateY;
                                        
                                        // Map [-1, 1] to [0, 1] for UI
                                        float uiX = (onvifX + 1.0f) / 2.0f;
                                        float uiY = (onvifY + 1.0f) / 2.0f;
                                        
                                        points.Add(new PointF(uiX, uiY));
                                    }
                                }
                                config.Polygon = points;
                                config.IsSupported = true;
                                
                                XNamespace NsNs2 = "http://www.onvif.org/ver20/analytics";
                                var configElem = regionRule.Descendants(NsNs2 + "MotionRegionConfig").FirstOrDefault();
                                if (configElem != null)
                                {
                                    if (configElem.Attribute("Armed") != null)
                                    {
                                        _supportsEnableDisable = true;
                                        config.Enabled = configElem.Attribute("Armed").Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                                    }
                                    else
                                    {
                                        _supportsEnableDisable = false;
                                        config.Enabled = true;
                                    }

                                    if (configElem.Attribute("Sensitivity") != null)
                                    {
                                        _supportsSensitivity = true;
                                        if (int.TryParse(configElem.Attribute("Sensitivity").Value, out int sens))
                                        {
                                            config.Sensitivity = sens;
                                        }
                                    }
                                    else
                                    {
                                        _supportsSensitivity = false;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                config.IsSupported = false;
            }

            return config;
        }

        public async Task<MotionDetectionOperationResult> ApplyConfigurationAsync(string ip, string username, string password, MotionDetectionApplyRequest request)
        {
            if (string.IsNullOrWhiteSpace(_configurationToken) || string.IsNullOrWhiteSpace(_originalRuleXml) || request.Polygon == null)
                return MotionDetectionOperationResult.Fail("InvalidState", "Original configuration is missing.");

            // Validate polygon
            if (request.Polygon.Count > 0 && request.Polygon.Count < 3)
                return MotionDetectionOperationResult.Fail("Validation", "Polygon must have at least 3 vertices or be completely empty to clear the area.");

            if (request.Polygon.Count > 0)
            {
                for (int i = 0; i < request.Polygon.Count; i++)
                {
                    var pt = request.Polygon[i];
                    if (float.IsNaN(pt.X) || float.IsInfinity(pt.X) || float.IsNaN(pt.Y) || float.IsInfinity(pt.Y))
                        return MotionDetectionOperationResult.Fail("Validation", "Invalid polygon coordinates.");
                    if (pt.X < -0.01f || pt.X > 1.01f || pt.Y < -0.01f || pt.Y > 1.01f)
                        return MotionDetectionOperationResult.Fail("Validation", "Coordinates must be between 0.0 and 1.0.");
                    if (i > 0 && Math.Abs(pt.X - request.Polygon[i - 1].X) < 0.0001f && Math.Abs(pt.Y - request.Polygon[i - 1].Y) < 0.0001f)
                        return MotionDetectionOperationResult.Fail("Validation", "Duplicate consecutive vertices are not allowed.");
                }

                float area = 0;
                for (int i = 0; i < request.Polygon.Count; i++)
                {
                    var pt1 = request.Polygon[i];
                    var pt2 = request.Polygon[(i + 1) % request.Polygon.Count];
                    area += pt1.X * pt2.Y - pt2.X * pt1.Y;
                }
                if (Math.Abs(area / 2f) < 0.0001f)
                    return MotionDetectionOperationResult.Fail("Validation", "Polygon area must be greater than zero.");
            }

            try
            {
                var ruleElem = XElement.Parse(_originalRuleXml);
                var polygonElem = ruleElem.Descendants(NsTt + "Polygon").FirstOrDefault();
                if (polygonElem == null)
                    return MotionDetectionOperationResult.Fail("ParseError", "Could not find tt:Polygon in original rule.");

                XNamespace NsNs2 = "http://www.onvif.org/ver20/analytics";
                var configElem = ruleElem.Descendants(NsNs2 + "MotionRegionConfig").FirstOrDefault();
                if (configElem != null)
                {
                    if (configElem.Attribute("Armed") != null)
                    {
                        configElem.SetAttributeValue("Armed", request.Enabled ? "true" : "false");
                    }
                    if (_supportsSensitivity && request.Sensitivity.HasValue)
                    {
                        configElem.SetAttributeValue("Sensitivity", request.Sensitivity.Value.ToString());
                    }
                }

                polygonElem.RemoveNodes();

                foreach (var pt in request.Polygon)
                {
                    float onvifX = (pt.X * 2.0f) - 1.0f;
                    float onvifY = (pt.Y * 2.0f) - 1.0f;
                    float cameraX = (onvifX - _translateX) / _scaleX;
                    float cameraY = (onvifY - _translateY) / _scaleY;
                    polygonElem.Add(new XElement(NsTt + "Point",
                        new XAttribute("x", cameraX.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new XAttribute("y", cameraY.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    ));
                }

                ruleElem.Attributes().Where(a => a.IsNamespaceDeclaration && a.Name.LocalName == "tan").ToList().ForEach(a => a.Remove());
                string modifyRulesBody = $@"
  <tan:ModifyRules xmlns:tan=""http://www.onvif.org/ver20/analytics/wsdl"" xmlns:tt=""http://www.onvif.org/ver10/schema"" xmlns:ns2=""http://www.onvif.org/ver20/analytics"" xmlns:hon=""http://www.honeywell.com/onvif/extension/ver10/wsdl"">
    <tan:ConfigurationToken>{_configurationToken}</tan:ConfigurationToken>
    {ruleElem.ToString()}
  </tan:ModifyRules>";

                using (var client = new OnvifSoapClient())
                {
                    string analyticsUrl = $"http://{ip}/onvif/analytics_service";
                    string response = await client.PostSoapAsync(analyticsUrl, modifyRulesBody, "http://www.onvif.org/ver20/analytics/wsdl/ModifyRules", username, password, CancellationToken.None);
                    if (string.IsNullOrWhiteSpace(response))
                        return MotionDetectionOperationResult.Fail("NetworkError", "Empty response from ModifyRules.");
                }

                var verifyConfig = await ReadConfigurationAsync(ip, username, password);
                if (!verifyConfig.IsSupported || verifyConfig.Polygon == null || verifyConfig.Polygon.Count != request.Polygon.Count)
                    return MotionDetectionOperationResult.Fail("Verification Failed", "Read-back polygon point count mismatch.");

                if (_supportsEnableDisable && verifyConfig.Enabled != request.Enabled)
                {
                    return MotionDetectionOperationResult.Fail("Verification Failed", "Read-back Armed state did not match the requested value.");
                }

                if (_supportsSensitivity && request.Sensitivity.HasValue && verifyConfig.Sensitivity != request.Sensitivity)
                {
                    return MotionDetectionOperationResult.Fail("Verification Failed", "Read-back Sensitivity did not match the requested value.");
                }

                for (int i = 0; i < request.Polygon.Count; i++)
                {
                    var reqPt = request.Polygon[i];
                    var verPt = verifyConfig.Polygon[i];
                    if (Math.Abs(reqPt.X - verPt.X) > 0.05f || Math.Abs(reqPt.Y - verPt.Y) > 0.05f)
                        return MotionDetectionOperationResult.Fail("Verification Failed", "Read-back polygon coordinates did not match.");
                }

                return new MotionDetectionOperationResult { Success = true, VerifiedConfiguration = verifyConfig };
            }
            catch (Exception ex)
            {
                return MotionDetectionOperationResult.Fail("Exception", ex.Message);
            }
        }

        public async Task<MotionDetectionOperationResult> RestoreRollbackAsync(string ip, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(_configurationToken) || string.IsNullOrWhiteSpace(_originalRuleXml))
                return MotionDetectionOperationResult.Fail("InvalidState", "No rollback configuration available.");

            try
            {
                var rElem = XElement.Parse(_originalRuleXml);
                rElem.Attributes().Where(a => a.IsNamespaceDeclaration && a.Name.LocalName == "tan").ToList().ForEach(a => a.Remove());
                string modifyRulesBody = $@"
  <tan:ModifyRules xmlns:tan=""http://www.onvif.org/ver20/analytics/wsdl"" xmlns:tt=""http://www.onvif.org/ver10/schema"" xmlns:ns2=""http://www.onvif.org/ver20/analytics"" xmlns:hon=""http://www.honeywell.com/onvif/extension/ver10/wsdl"">
    <tan:ConfigurationToken>{_configurationToken}</tan:ConfigurationToken>
    {rElem.ToString()}
  </tan:ModifyRules>";

                using (var client = new OnvifSoapClient())
                {
                    string analyticsUrl = $"http://{ip}/onvif/analytics_service";
                    string response = await client.PostSoapAsync(analyticsUrl, modifyRulesBody, "http://www.onvif.org/ver20/analytics/wsdl/ModifyRules", username, password, CancellationToken.None);
                    if (string.IsNullOrWhiteSpace(response))
                        return MotionDetectionOperationResult.Fail("NetworkError", "Empty response from ModifyRules during rollback.");
                }
                return new MotionDetectionOperationResult { Success = true };
            }
            catch (Exception ex)
            {
                return MotionDetectionOperationResult.Fail("Exception", ex.Message);
            }
        }
    }
}
