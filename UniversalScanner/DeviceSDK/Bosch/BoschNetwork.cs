using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UniversalScanner.DeviceSDK.Common;

namespace UniversalScanner.DeviceSDK.Bosch
{
    public enum BoschReadinessState
    {
        ConfigurationFailed,
        ConfigurationAccepted,
        DeviceDetected,
        DeviceDetectedWebNotReady,
        FullyReady,
        VerificationTimedOut,
        VerificationCancelled
    }

    public static class BoschNetwork
    {
        public class BoschIpChangeResult
        {
            public bool Success { get; set; }
            public BoschReadinessState State { get; set; }
            public string Message { get; set; }
            public string OriginalMac { get; set; }
        }

        public static async Task<BoschIpChangeResult> ChangeNetworkAddressAsync(
            string currentIp,
            string username,
            string password,
            string newIp,
            string subnetMask,
            string gateway,
            bool enableDhcp,
            Action<string> statusCallback = null)
        {
            statusCallback?.Invoke("Resolving original MAC address...");
            string originalMac = await NetworkTools.GetMacAsync(currentIp);

            // Experimental HTTPS bypass branch (triggered via command line to avoid hardcoded constants)
            bool useHttpsExperiment = Environment.CommandLine.Contains("--bosch-https");

            // 1. Attempt native Bosch RCP
            bool rcpSupported = await TryChangeIpViaRcpAsync(currentIp, username, password, newIp, subnetMask, gateway, enableDhcp, statusCallback, useHttpsExperiment);
            if (rcpSupported)
            {
                return new BoschIpChangeResult 
                { 
                    Success = true, 
                    State = BoschReadinessState.ConfigurationAccepted, 
                    Message = "Bosch network settings were accepted.", 
                    OriginalMac = originalMac 
                };
            }

            // 2. Fallback to GenericOnvifNetwork only if explicitly unsupported
            statusCallback?.Invoke("Native RCP unsupported by firmware. Falling back to ONVIF...");
            try
            {
                bool onvifSuccess = await GenericOnvifNetwork.ChangeNetworkAddressAsync(
                    currentIp,
                    username,
                    password,
                    newIp,
                    subnetMask,
                    gateway,
                    enableDhcp,
                    statusCallback);
                    
                string fallbackMessage = enableDhcp 
                    ? "DHCP enabled via ONVIF. The device may reboot before it answers at the new address." 
                    : "Bosch IP address changed via ONVIF. The device may reboot before it answers at the new address.";
                
                if (!onvifSuccess) 
                    fallbackMessage = "ONVIF IP change failed. Check credentials and that ONVIF is enabled on the device.";

                return new BoschIpChangeResult { Success = onvifSuccess, State = onvifSuccess ? BoschReadinessState.ConfigurationAccepted : BoschReadinessState.ConfigurationFailed, Message = fallbackMessage };
            }
            catch (Exception ex)
            {
                statusCallback?.Invoke("ONVIF fallback failed: " + ex.Message);
                return new BoschIpChangeResult { Success = false, State = BoschReadinessState.ConfigurationFailed, Message = "ONVIF fallback failed: " + ex.Message };
            }
        }

        public static async Task<BoschIpChangeResult> VerifyReadinessAsync(
            string oldIp, 
            string newIp, 
            string originalMac, 
            bool enableDhcp, 
            Action triggerDiscovery,
            Func<string, string, bool> isDeviceDiscovered,
            Action<string> statusCallback, 
            Func<bool> checkCancelled)
        {
            statusCallback?.Invoke("Network settings accepted...\r\nPlease wait while the Bosch camera reboots...\r\nLooking for:\r\n" + newIp + "\r\nExpected MAC:\r\n" + originalMac);
            
            // Trigger the internal discovery API without coupling to the UI
            triggerDiscovery?.Invoke();

            bool success = false;
            // 40 loops of 500ms = 20 seconds maximum timeout
            for (int i = 0; i < 40; i++)
            {
                if (checkCancelled()) 
                    return new BoschIpChangeResult { Success = false, State = BoschReadinessState.VerificationCancelled, Message = "Bosch network settings were accepted, but readiness verification was cancelled. The device list was not updated." };
                
                // Read-only observation of the existing scanner device collection
                if (isDeviceDiscovered != null && isDeviceDiscovered(newIp, originalMac))
                {
                    success = true;
                    break;
                }

                string newIpMac = await NetworkTools.GetMacAsync(newIp, 1, 100);
                if (!string.IsNullOrWhiteSpace(originalMac) &&
                    NormalizeMac(newIpMac) == NormalizeMac(originalMac))
                {
                    success = true;
                    break;
                }
                
                await Task.Delay(500);
            }

            if (success)
            {
                return new BoschIpChangeResult { Success = true, State = BoschReadinessState.FullyReady, Message = "Bosch IP address changed successfully." };
            }
            else
            {
                string oldIpMac = await NetworkTools.GetMacAsync(oldIp, 2, 300);
                string oldIpHint = NormalizeMac(oldIpMac) == NormalizeMac(originalMac)
                    ? " The original camera still appears to answer at " + oldIp + "."
                    : "";

                return new BoschIpChangeResult
                {
                    Success = false,
                    State = BoschReadinessState.VerificationTimedOut,
                    Message = "Bosch network settings were accepted, but the scanner did not rediscover the original camera at " + newIp + " within 20 seconds." + oldIpHint + " The device list was not updated."
                };
            }
        }
        
        internal static string NormalizeMac(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac)) return "";
            return mac.Replace("-", "").Replace(":", "").ToUpperInvariant();
        }



        private static async Task<bool> TryChangeIpViaRcpAsync(
            string currentIp,
            string username,
            string password,
            string newIp,
            string subnetMask,
            string gateway,
            bool enableDhcp,
            Action<string> statusCallback,
            bool useHttps = false)
        {
            string scheme = useHttps ? "https" : "http";
            statusCallback?.Invoke($"Attempting native Bosch RCP IP change via {scheme.ToUpperInvariant()}...");
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => 
                {
                    // Scope certificate bypass strictly to the target device IP for this experiment
                    return msg.RequestUri.Host == currentIp;
                },
                UseCookies = true,
                Credentials = new System.Net.NetworkCredential(username, password)
            };

            using (HttpClient client = new HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

                var commands = new[]
                {
                    $"command=0x0CD8&type=F_FLAG&direction=WRITE&payload={(enableDhcp ? "1" : "0")}",
                    $"command=0x007C&type=P_STRING&direction=WRITE&payload={Uri.EscapeDataString(newIp)}",
                    $"command=0x007D&type=P_STRING&direction=WRITE&payload={Uri.EscapeDataString(subnetMask)}",
                    $"command=0x007F&type=P_STRING&direction=WRITE&payload={Uri.EscapeDataString(gateway)}",
                    $"command=0x092D&type=T_OCTET&direction=WRITE&num=0&payload=0",
                    $"command=0x0811&type=F_FLAG&direction=WRITE&payload=1"
                };

                foreach (var cmd in commands)
                {
                    if (cmd.Contains("0x0811"))
                        statusCallback?.Invoke("Rebooting Bosch camera to apply network settings. Please wait a while for the camera to come back online...");

                    string requestUrl = $"{scheme}://{currentIp}/rcp.xml?{cmd}";
                    HttpResponseMessage response;
                    try
                    {
                        response = await client.GetAsync(requestUrl);
                    }
                    catch (TaskCanceledException)
                    {
                        // A timeout on the final commit, reboot, or IP/Gateway change is expected as the camera drops the connection.
                        if (cmd.Contains("0x092D") || cmd.Contains("0x0811"))
                        {
                            return true;
                        }
                        throw new Exception("Bosch IP change failed: connection timed out.");
                    }
                    catch (Exception ex)
                    {
                        if (cmd.Contains("0x092D") || cmd.Contains("0x0811"))
                        {
                            return true;
                        }
                        throw new Exception("Bosch IP change failed: network error. " + ex.Message);
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        throw new Exception("Bosch IP change failed: authentication rejected.");
                    }
                    if (response.StatusCode == System.Net.HttpStatusCode.NotImplemented || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        return false; // Explicitly unsupported, fallback to ONVIF
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"Bosch IP change failed: camera returned HTTP {(int)response.StatusCode}.");
                    }

                    string xml = await response.Content.ReadAsStringAsync();
                    if (xml.Contains("<error>"))
                    {
                        if (xml.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return false; // Explicitly unsupported, fallback to ONVIF
                        }
                        throw new Exception("Bosch IP change failed: camera returned an error response.");
                    }
                }
                
                return true;
            }
        }
    }
}
