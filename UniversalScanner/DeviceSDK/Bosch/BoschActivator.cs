using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace UniversalScanner.DeviceSDK.Bosch
{
    public static class BoschActivator
    {
        public static bool ActivateDevice(string ip, string username, string password, out string message)
        {
            if (password.Length < 8)
            {
                message = "Password must be at least 8 characters long for Bosch devices.";
                return false;
            }
            if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || !password.Any(c => !char.IsLetterOrDigit(c)))
            {
                message = "Bosch password must contain at least one uppercase, lowercase, number, and special character.";
                return false;
            }

            try
            {
                message = ActivateDeviceAsync(ip, username, password).GetAwaiter().GetResult();
                return message == "Bosch device activated successfully.";
            }
            catch (Exception ex)
            {
                message = "Bosch activation failed: " + ex.Message;
                return false;
            }
        }

        private static async Task<string> ActivateDeviceAsync(string ip, string username, string password)
        {
            string url = $"http://{ip}/rcp.xml";
            
            try
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true,
                    UseCookies = true
                };
                using (HttpClient client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    
                    var initResponse = await client.GetAsync($"http://{ip}/");
                    
                    List<byte> payloadBytes = new List<byte>();
                    payloadBytes.AddRange(new byte[] { 0, 0 }); // length placeholder
                    payloadBytes.AddRange(new byte[] { 0, 0 }); // tag 0 (user entry)

                    byte[] bName = Encoding.UTF8.GetBytes(username);
                    payloadBytes.Add((byte)(((bName.Length + 4) >> 8) & 0xFF));
                    payloadBytes.Add((byte)((bName.Length + 4) & 0xFF));
                    payloadBytes.AddRange(new byte[] { 0, 1 }); // tag 1 (username)
                    payloadBytes.AddRange(bName);

                    byte[] bPwd = Encoding.UTF8.GetBytes(password);
                    payloadBytes.Add((byte)(((bPwd.Length + 4) >> 8) & 0xFF));
                    payloadBytes.Add((byte)((bPwd.Length + 4) & 0xFF));
                    payloadBytes.AddRange(new byte[] { 0, 4 }); // tag 4 (password)
                    payloadBytes.AddRange(bPwd);

                    int totalLen = payloadBytes.Count;
                    payloadBytes[0] = (byte)((totalLen >> 8) & 0xFF);
                    payloadBytes[1] = (byte)(totalLen & 0xFF);

                    string hexPayload = "0x" + BitConverter.ToString(payloadBytes.ToArray()).Replace("-", "").ToUpperInvariant();

                    string requestUrl = $"http://{ip}/rcp.xml?command=0x0BDA&type=P_OCTET&direction=WRITE&num=1&payload={hexPayload}";
                    
                    client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
                    var response = await client.GetAsync(requestUrl);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string xml = await response.Content.ReadAsStringAsync();
                        if (xml.Contains("<error>"))
                        {
                            return "Bosch activation failed: camera returned an error response.";
                        }
                        
                        await Task.Delay(1500);
                        string status = await BoschActivationStatusProbe.GetStatusAsync(ip);
                        
                        if (status.StartsWith("Inactive"))
                        {
                            return "Bosch activation failed: camera still requires password setup.";
                        }
                        
                        return "Bosch device activated successfully.";
                    }
                    else
                    {
                        return $"Bosch activation failed: device returned HTTP {(int)response.StatusCode}";
                    }
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
