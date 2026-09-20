using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using global::UniversalScanner.DeviceSDK.MotionDetection;

namespace UniversalScanner.DeviceSDK.Honeywell
{
    public class HoneywellWebRuntimeMotionService : IMotionDetectionService
    {
        public static async Task<bool> IsSupportedAsync(string ip)
        {
            try
            {
                string url = $"http://{ip}/action/WEB_JsonAjaxAuth";
                var request = WebRequest.Create(url) as HttpWebRequest;
                if (request == null) return false;
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = 2000;
                
                string payload = "{\"cmd\":\"userLogin\",\"data\":{\"userName\":\"\",\"password\":\"\",\"userType\":0}}";
                byte[] data = Encoding.UTF8.GetBytes(payload);
                request.ContentLength = data.Length;
                
                using (var stream = await request.GetRequestStreamAsync())
                {
                    await stream.WriteAsync(data, 0, data.Length);
                }
                
                using (var response = await request.GetResponseAsync() as HttpWebResponse)
                {
                    return response != null;
                }
            }
            catch (WebException ex)
            {
                if (ex.Response is HttpWebResponse httpResponse)
                {
                    if (httpResponse.StatusCode == HttpStatusCode.OK || httpResponse.StatusCode == HttpStatusCode.Unauthorized || httpResponse.StatusCode == HttpStatusCode.Forbidden || httpResponse.StatusCode == HttpStatusCode.BadRequest)
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        private string _activeSessionId;
        private string _activeToken;
        private string _lastOriginalPayload;

        public MotionDetectionCapabilities GetCapabilities()
        {
            return new MotionDetectionCapabilities
            {
                SupportsEnableDisable = true,
                SupportsGrid = true,
                SupportsSensitivity = true,
                SupportsRead = true,
                SupportsWrite = true,
                SupportsVerification = true,
                SupportsFullImage = true,
                SupportsCustomGrid = true,
                SupportsClearArea = true,
                SupportsGridPreview = true,
                SupportsPolygon = false
            };
        }

        public async Task<MotionDetectionConfiguration> ReadConfigurationAsync(string ip, string username, string password)
        {
            return await Task.Run(() =>
            {
                if (!EnsureAuthenticated(ip, username, password, out string error))
                    return new MotionDetectionConfiguration { IsSupported = false };

                string payload = "{\"cmd\":\"getMotionAlarm\",\"session\":\"" + _activeSessionId + "\",\"data\":{\"cameraId\":1}}";
                string response;
                string newTok;
                if (!PostJson(ip, "/action/WEB_JsonAjax", payload, _activeToken, out response, out newTok, out error))
                {
                    // Retry once on failure
                    if (EnsureAuthenticated(ip, username, password, out error, force: true))
                    {
                        payload = "{\"cmd\":\"getMotionAlarm\",\"session\":\"" + _activeSessionId + "\",\"data\":{\"cameraId\":1}}";
                        if (!PostJson(ip, "/action/WEB_JsonAjax", payload, _activeToken, out response, out newTok, out error))
                            return new MotionDetectionConfiguration { IsSupported = false };
                    }
                    else
                    {
                        return new MotionDetectionConfiguration { IsSupported = false };
                    }
                }

                if (!string.IsNullOrEmpty(newTok)) _activeToken = newTok;
                
                string dataStr = ExtractJsonObject(response, "data");
                if (string.IsNullOrEmpty(dataStr))
                    return new MotionDetectionConfiguration { IsSupported = false };

                _lastOriginalPayload = dataStr;

                bool enabled = ExtractJsonBool(dataStr, "alarmEnableFlag") ?? false;
                int rawSensitivity = ExtractJsonNumber(dataStr, "sensitivity") ?? 5;
                int minSens = ExtractJsonNumber(dataStr, "minSensitivity") ?? 1;
                int maxSens = ExtractJsonNumber(dataStr, "maxSensitivity") ?? 10;
                
                int logicalSensitivity = rawSensitivity;
                int logicalMin = minSens;
                int logicalMax = maxSens;
                
                if (logicalSensitivity < logicalMin) logicalSensitivity = logicalMin;
                if (logicalSensitivity > logicalMax) logicalSensitivity = logicalMax;
                
                string areaData = ExtractJsonString(dataStr, "areaData");
                bool isFullImage = false;
                bool[] activeCells = new bool[396];
                
                if (!string.IsNullOrEmpty(areaData) && areaData.Length >= 396)
                {
                    bool allZeros = true;
                    bool allOnes = true;
                    for (int i = 0; i < 396; i++)
                    {
                        bool selected = (areaData[i] != '0');
                        activeCells[i] = selected;
                        if (selected) allZeros = false;
                        else allOnes = false;
                    }
                    isFullImage = allOnes;
                }

                return new MotionDetectionConfiguration
                {
                    Enabled = enabled,
                    Sensitivity = logicalSensitivity, // RESTORED
                    SensitivityMinimum = logicalMin,
                    SensitivityMaximum = logicalMax,
                    Columns = 22,
                    Rows = 18,
                    ActiveCells = activeCells,
                    IsSupported = true,
                    ConfigurationSource = "Honeywell Web Runtime API",
                    IsFullImage = isFullImage
                };
            });
        }

        public async Task<MotionDetectionOperationResult> ApplyConfigurationAsync(string ip, string username, string password, MotionDetectionApplyRequest request)
        {
            return await Task.Run(() =>
            {
                if (!EnsureAuthenticated(ip, username, password, out string error))
                    return MotionDetectionOperationResult.Fail("Authentication Error", "Failed to login: " + error);

                // 1. Call getMotionAlarm
                string getPayload = "{\"cmd\":\"getMotionAlarm\",\"session\":\"" + _activeSessionId + "\",\"data\":{\"cameraId\":1}}";
                string getResp;
                string newTok;
                if (!PostJson(ip, "/action/WEB_JsonAjax", getPayload, _activeToken, out getResp, out newTok, out error))
                {
                    // Retry once
                    if (EnsureAuthenticated(ip, username, password, out error, force: true))
                    {
                        getPayload = "{\"cmd\":\"getMotionAlarm\",\"session\":\"" + _activeSessionId + "\",\"data\":{\"cameraId\":1}}";
                        if (!PostJson(ip, "/action/WEB_JsonAjax", getPayload, _activeToken, out getResp, out newTok, out error))
                            return MotionDetectionOperationResult.Fail("Communication Error", "Failed to read configuration: " + error);
                    }
                    else
                    {
                        return MotionDetectionOperationResult.Fail("Authentication Error", "Failed to login during retry.");
                    }
                }
                
                if (!string.IsNullOrEmpty(newTok)) _activeToken = newTok;
                string dataStr = ExtractJsonObject(getResp, "data");
                if (string.IsNullOrEmpty(dataStr))
                    return MotionDetectionOperationResult.Fail("Parse Error", "Failed to parse motion alarm data.");

                _lastOriginalPayload = dataStr;

                // 3. Modify only the requested settings
                string modifiedData = dataStr;
                modifiedData = ReplaceJsonBool(modifiedData, "alarmEnableFlag", request.Enabled);
                if (request.Sensitivity.HasValue)
                {
                    modifiedData = ReplaceJsonNumber(modifiedData, "sensitivity", request.Sensitivity.Value);
                }

                if (request.ActiveCells != null && request.ActiveCells.Length == 0) // Clear Area
                {
                    modifiedData = ReplaceJsonString(modifiedData, "areaData", new string('0', 400));
                }
                else if (request.ActiveCells != null) // Full Image / Custom
                {
                    StringBuilder sb = new StringBuilder(400);
                    for (int i = 0; i < request.ActiveCells.Length && i < 396; i++)
                    {
                        sb.Append(request.ActiveCells[i] ? '1' : '0');
                    }
                    while (sb.Length < 400)
                    {
                        sb.Append('0');
                    }
                    modifiedData = ReplaceJsonString(modifiedData, "areaData", sb.ToString());
                }

                // 4. Send the complete object
                string setPayload = "{\"cmd\":\"setMotionAlarm\",\"session\":\"" + _activeSessionId + "\",\"data\":" + modifiedData + "}";
                
                string setResp;
                if (!PostJson(ip, "/action/WEB_JsonAjax", setPayload, _activeToken, out setResp, out newTok, out error))
                {
                    RestoreRollbackSync(ip);
                    return MotionDetectionOperationResult.Fail("Write Error", "Failed to write configuration: " + error);
                }
                
                if (!string.IsNullOrEmpty(newTok)) _activeToken = newTok;
                
                int? setCode = ExtractJsonNumber(setResp, "code");
                if (setCode != 0)
                {
                    RestoreRollbackSync(ip);
                    return MotionDetectionOperationResult.Fail("Write Error", "Camera rejected configuration with code: " + setCode);
                }

                // 5. Verify
                if (!PostJson(ip, "/action/WEB_JsonAjax", getPayload, _activeToken, out getResp, out newTok, out error))
                {
                    return MotionDetectionOperationResult.Fail("Verification Error", "Failed to read configuration for verification: " + error);
                }
                
                if (!string.IsNullOrEmpty(newTok)) _activeToken = newTok;
                
                string verifyData = ExtractJsonObject(getResp, "data");
                bool verifyEnabled = ExtractJsonBool(verifyData, "alarmEnableFlag") ?? false;
                
                if (verifyEnabled != request.Enabled)
                {
                    RestoreRollbackSync(ip);
                    return MotionDetectionOperationResult.Fail("Verification Error", "Camera did not persist the applied configuration.");
                }

                int verifyRawSens = ExtractJsonNumber(verifyData, "sensitivity") ?? 5;
                int verifyMinSens = ExtractJsonNumber(verifyData, "minSensitivity") ?? 1;
                int verifyMaxSens = ExtractJsonNumber(verifyData, "maxSensitivity") ?? 10;
                int verifyLogicalSens = verifyRawSens;
                int verifyLogicalMax = verifyMaxSens;

                if (verifyLogicalSens < 1) verifyLogicalSens = 1;
                if (verifyLogicalSens > verifyLogicalMax) verifyLogicalSens = verifyLogicalMax;

                var config = new MotionDetectionConfiguration
                {
                    Enabled = verifyEnabled,
                    Sensitivity = verifyLogicalSens,
                    SensitivityMinimum = 1,
                    SensitivityMaximum = verifyLogicalMax,
                    Columns = 22,
                    Rows = 18,
                    IsSupported = true,
                    ConfigurationSource = "Honeywell Web Runtime API"
                };

                return new MotionDetectionOperationResult { Success = true, VerifiedConfiguration = config };
            });
        }

        public async Task<MotionDetectionOperationResult> RestoreRollbackAsync(string ip, string username, string password)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrEmpty(_lastOriginalPayload) || string.IsNullOrEmpty(_activeSessionId))
                    return MotionDetectionOperationResult.Fail("Rollback Error", "No original payload available for rollback.");
                
                string setPayload = "{\"cmd\":\"setMotionAlarm\",\"session\":\"" + _activeSessionId + "\",\"data\":" + _lastOriginalPayload + "}";
                if (PostJson(ip, "/action/WEB_JsonAjax", setPayload, _activeToken, out string setResp, out string newTok, out string error))
                {
                    if (!string.IsNullOrEmpty(newTok)) _activeToken = newTok;
                    return new MotionDetectionOperationResult { Success = true };
                }
                return MotionDetectionOperationResult.Fail("Rollback Error", "Failed to rollback: " + error);
            });
        }
        
        private void RestoreRollbackSync(string ip)
        {
            if (!string.IsNullOrEmpty(_lastOriginalPayload) && !string.IsNullOrEmpty(_activeSessionId))
            {
                string setPayload = "{\"cmd\":\"setMotionAlarm\",\"session\":\"" + _activeSessionId + "\",\"data\":" + _lastOriginalPayload + "}";
                PostJson(ip, "/action/WEB_JsonAjax", setPayload, _activeToken, out string setResp, out string newTok, out string error);
                if (!string.IsNullOrEmpty(newTok)) _activeToken = newTok;
            }
        }

        private bool EnsureAuthenticated(string ip, string username, string password, out string error, bool force = false)
        {
            error = "";
            if (!force && !string.IsNullOrEmpty(_activeSessionId) && !string.IsNullOrEmpty(_activeToken))
                return true;

            string urlAuth = "https://" + ip + "/action/WEB_JsonAjaxAuth";

            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            string getStateBody = "{\"cmd\":\"getState\"}";
            if (!PostJson(ip, "/action/WEB_JsonAjaxAuth", getStateBody, "", out string getStateResp, out string newTok, out error))
                return false;

            string nonce = ExtractJsonString(getStateResp, "Nonce");
            string publicKey = ExtractJsonString(getStateResp, "PublicKey");
            if (string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(publicKey))
            {
                error = "getState response missing Nonce/PublicKey";
                return false;
            }
            if (!string.IsNullOrEmpty(newTok)) _activeToken = newTok;

            string r = HoneywellRsaHelper.GenerateCnonceRaw();
            byte[] iv = Encoding.UTF8.GetBytes(r.Substring(0, 16));
            string cnonce = Convert.ToBase64String(Encoding.ASCII.GetBytes(r));

            string hashHex = HoneywellRsaHelper.Sha256Hex(username + nonce + cnonce);
            byte[] aesKey = Encoding.UTF8.GetBytes(hashHex.Substring(0, 32));

            string aesCipherB64;
            string responseField;
            try
            {
                aesCipherB64 = HoneywellRsaHelper.AesCbcEncryptBase64(aesKey, iv, password);
                responseField = HoneywellRsaHelper.EncryptPkcs1Base64(publicKey, aesCipherB64);
            }
            catch (Exception ex)
            {
                error = "Encryption failed: " + ex.Message;
                return false;
            }

            string loginBody =
                "{\"cmd\":\"userLogin\",\"data\":{" +
                "\"user\":\"" + JsonEscape(username) + "\"," +
                "\"cnonce\":\"" + JsonEscape(cnonce) + "\"," +
                "\"response\":\"" + JsonEscape(responseField) + "\"}}";

            if (!PostJson(ip, "/action/WEB_JsonAjaxAuth", loginBody, _activeToken, out string loginResp, out newTok, out error))
                return false;

            if (!string.IsNullOrEmpty(newTok)) _activeToken = newTok;

            int? code = ExtractJsonNumber(loginResp, "code");
            if (code != 0)
            {
                error = "Authentication failed with code " + code;
                return false;
            }

            string dataStr = ExtractJsonObject(loginResp, "data");
            _activeSessionId = ExtractJsonString(dataStr, "key");
            if (string.IsNullOrEmpty(_activeSessionId))
            {
                error = "No session key returned in userLogin.";
                return false;
            }

            return true;
        }

        private bool PostJson(string ip, string path, string body, string token, out string responseBody, out string newToken, out string error)
        {
            responseBody = "";
            newToken = "";
            error = "";
            string url = "https://" + ip + path;

            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/xml";
                request.Accept = "application/json, text/javascript, */*; q=0.01";
                request.ContentLength = bodyBytes.Length;
                request.Timeout = 10000;
                request.ReadWriteTimeout = 10000;
                request.AllowAutoRedirect = false;
                request.KeepAlive = false;
                
                if (!string.IsNullOrEmpty(token))
                    request.Headers["token"] = token;

                using (Stream stream = request.GetRequestStream())
                    stream.Write(bodyBytes, 0, bodyBytes.Length);

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    newToken = response.Headers["token"];
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                        responseBody = reader.ReadToEnd();
                }
                return true;
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse)
            {
                HttpWebResponse errResponse = (HttpWebResponse)ex.Response;
                newToken = errResponse.Headers["token"];
                try
                {
                    using (StreamReader reader = new StreamReader(errResponse.GetResponseStream()))
                        responseBody = reader.ReadToEnd();
                }
                catch { }
                error = "HTTP " + (int)errResponse.StatusCode;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string ExtractJsonString(string json, string field)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(field)) return null;
            Match m = Regex.Match(json, "\"" + Regex.Escape(field) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) return null;
            return m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static int? ExtractJsonNumber(string json, string field)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(field)) return null;
            Match m = Regex.Match(json, "\"" + Regex.Escape(field) + "\"\\s*:\\s*(-?[0-9]+)");
            return m.Success ? (int?)int.Parse(m.Groups[1].Value) : null;
        }
        
        private static bool? ExtractJsonBool(string json, string field)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(field)) return null;
            Match m = Regex.Match(json, "\"" + Regex.Escape(field) + "\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            return m.Success ? (bool?)(m.Groups[1].Value.ToLowerInvariant() == "true") : null;
        }

        private static string ExtractJsonObject(string json, string field)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(field)) return null;
            int start = json.IndexOf("\"" + field + "\"");
            if (start < 0) return null;
            
            start = json.IndexOf(":", start);
            if (start < 0) return null;

            int objStart = json.IndexOf("{", start);
            if (objStart < 0) return null;

            int braceCount = 1;
            for (int i = objStart + 1; i < json.Length; i++)
            {
                if (json[i] == '{') braceCount++;
                else if (json[i] == '}') braceCount--;

                if (braceCount == 0)
                    return json.Substring(objStart, i - objStart + 1);
            }
            return null;
        }

        private static string ReplaceJsonString(string json, string field, string newValue)
        {
            return Regex.Replace(json, "(\"" + Regex.Escape(field) + "\"\\s*:\\s*)\"((?:[^\"\\\\]|\\\\.)*)\"", "${1}\"" + JsonEscape(newValue) + "\"");
        }
        
        private static string ReplaceJsonNumber(string json, string field, int newValue)
        {
            return Regex.Replace(json, "(\"" + Regex.Escape(field) + "\"\\s*:\\s*)(-?[0-9]+)", "${1}" + newValue);
        }
        
        private static string ReplaceJsonBool(string json, string field, bool newValue)
        {
            string valStr = newValue ? "true" : "false";
            return Regex.Replace(json, "(\"" + Regex.Escape(field) + "\"\\s*:\\s*)(true|false)", "${1}" + valStr, RegexOptions.IgnoreCase);
        }
    }
}
