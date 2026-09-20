using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Linq;

namespace UniversalScanner.DeviceSDK.Hikvision
{
    public class HikvisionChannel
    {
        public int PhysicalChannel { get; set; }
        public string ChannelName { get; set; }
        public List<int> SupportedStreamTypes { get; set; } = new List<int>();
    }

    public static class HikvisionStreamingHelper
    {
        public static async Task<List<HikvisionChannel>> GetChannelsAsync(string ip, string username, string password)
        {
            var channels = new Dictionary<int, HikvisionChannel>();
            string url = $"http://{ip}/ISAPI/Streaming/channels";
            var cookies = new CookieContainer();
            
            var resp = await SendHikvisionRequestAsync("GET", url, username, password, null, "application/xml", cookies);
            
            if (resp.StatusCode == HttpStatusCode.OK && !string.IsNullOrWhiteSpace(resp.Body))
            {
                try
                {
                    var xdoc = XDocument.Parse(resp.Body);
                    var ns = xdoc.Root.GetDefaultNamespace();
                    var list = xdoc.Descendants(ns + "StreamingChannel").Concat(xdoc.Descendants("StreamingChannel"));

                    foreach (var ch in list)
                    {
                        string idStr = (string)ch.Element(ns + "id") ?? (string)ch.Element("id");
                        if (!int.TryParse(idStr, out int streamId) || streamId <= 0)
                            continue;

                        int physicalChannel = streamId / 100;
                        int streamType = streamId % 100;
                        
                        if (physicalChannel <= 0 || streamType <= 0)
                            continue;

                        string name = (string)ch.Element(ns + "channelName") ?? (string)ch.Element("channelName");

                        if (!channels.TryGetValue(physicalChannel, out HikvisionChannel hc))
                        {
                            hc = new HikvisionChannel { PhysicalChannel = physicalChannel };
                            channels[physicalChannel] = hc;
                        }

                        if (!hc.SupportedStreamTypes.Contains(streamType))
                        {
                            hc.SupportedStreamTypes.Add(streamType);
                        }

                        // Prefer main stream name, then sub stream name, then keep existing if empty
                        if (streamType == 1 && !string.IsNullOrWhiteSpace(name))
                            hc.ChannelName = name;
                        else if (string.IsNullOrWhiteSpace(hc.ChannelName) && !string.IsNullOrWhiteSpace(name))
                            hc.ChannelName = name;
                    }
                }
                catch
                {
                    // XML parsing failed, return what we have (empty or partial)
                }
            }
            else if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException("Authentication failed.");
            }
            else
            {
                throw new Exception($"Network or server error: {resp.StatusCode}");
            }

            var resultList = channels.Values.OrderBy(c => c.PhysicalChannel).ToList();
            
            // Finalize names
            foreach (var ch in resultList)
            {
                if (string.IsNullOrWhiteSpace(ch.ChannelName))
                {
                    ch.ChannelName = $"Camera {ch.PhysicalChannel}";
                }
            }

            return resultList;
        }

        private class HikvisionResponse
        {
            public HttpStatusCode StatusCode { get; set; }
            public string Body { get; set; }
            public string WwwAuthenticate { get; set; }
        }

        private static async Task<HikvisionResponse> SendHikvisionRequestAsync(string method, string url, string username, string password, string payload, string contentType, CookieContainer cookies)
        {
            var resp1 = await DoHttpRequestAsync(method, url, null, payload, contentType, cookies);
            
            if (resp1.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(resp1.WwwAuthenticate))
            {
                string wwwAuth = resp1.WwwAuthenticate;
                if (wwwAuth.IndexOf("Digest", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string realm = GetDigestValue(wwwAuth, "realm");
                    string nonce = GetDigestValue(wwwAuth, "nonce");
                    string qop = GetDigestValue(wwwAuth, "qop");
                    string opaque = GetDigestValue(wwwAuth, "opaque");
                    string algorithm = GetDigestValue(wwwAuth, "algorithm");
                    string stale = GetDigestValue(wwwAuth, "stale");

                    string uriPath = new Uri(url).PathAndQuery;
                    string digestHeader = BuildDigestHeader(username, password, realm, nonce, qop, opaque, algorithm, method, uriPath);

                    return await DoHttpRequestAsync(method, url, digestHeader, payload, contentType, cookies);
                }
            }

            return resp1;
        }

        private static async Task<HikvisionResponse> DoHttpRequestAsync(string method, string url, string authHeader, string payload, string contentType, CookieContainer cookies)
        {
            return await Task.Run(() =>
            {
                var result = new HikvisionResponse { StatusCode = HttpStatusCode.InternalServerError, Body = "" };
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = method;
                request.Timeout = 5000;
                request.ReadWriteTimeout = 5000;
                request.AllowAutoRedirect = false;
                request.CookieContainer = cookies;

                if (!string.IsNullOrEmpty(authHeader))
                {
                    request.Headers.Add("Authorization", authHeader);
                }

                if (payload != null)
                {
                    byte[] data = Encoding.UTF8.GetBytes(payload);
                    request.ContentType = contentType;
                    request.ContentLength = data.Length;
                    using (var stream = request.GetRequestStream())
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }

                try
                {
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        result.StatusCode = response.StatusCode;
                        if (!string.IsNullOrEmpty(response.Headers["WWW-Authenticate"]))
                        {
                            result.WwwAuthenticate = response.Headers["WWW-Authenticate"];
                        }
                        using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        {
                            result.Body = reader.ReadToEnd();
                        }
                    }
                }
                catch (WebException ex)
                {
                    if (ex.Response is HttpWebResponse errResp)
                    {
                        result.StatusCode = errResp.StatusCode;
                        if (!string.IsNullOrEmpty(errResp.Headers["WWW-Authenticate"]))
                        {
                            result.WwwAuthenticate = errResp.Headers["WWW-Authenticate"];
                        }
                        try
                        {
                            using (StreamReader reader = new StreamReader(errResp.GetResponseStream(), Encoding.UTF8))
                            {
                                result.Body = reader.ReadToEnd();
                            }
                        }
                        catch { }
                    }
                }
                catch
                {
                    // Other exceptions (e.g. timeout) return InternalServerError
                }

                return result;
            });
        }

        private static string GetDigestValue(string authHeader, string key)
        {
            int idx = authHeader.IndexOf(key + "=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            idx += key.Length + 1;

            if (idx < authHeader.Length && authHeader[idx] == '"')
            {
                idx++;
                int end = authHeader.IndexOf('"', idx);
                if (end > idx)
                {
                    return authHeader.Substring(idx, end - idx);
                }
            }
            else
            {
                int end = authHeader.IndexOf(',', idx);
                if (end < 0) end = authHeader.Length;
                return authHeader.Substring(idx, end - idx).Trim();
            }
            return "";
        }

        private static string BuildDigestHeader(string username, string password, string realm, string nonce, string qop, string opaque, string algorithm, string method, string uri)
        {
            string ha1 = CalculateMd5($"{username}:{realm}:{password}");
            string ha2 = CalculateMd5($"{method}:{uri}");

            string nc = "00000001";
            string cnonce = Guid.NewGuid().ToString("N").Substring(0, 16);

            string response;
            if (!string.IsNullOrEmpty(qop) && qop.Contains("auth"))
            {
                response = CalculateMd5($"{ha1}:{nonce}:{nc}:{cnonce}:auth:{ha2}");
                return $"Digest username=\"{username}\", realm=\"{realm}\", nonce=\"{nonce}\", uri=\"{uri}\", qop=auth, nc={nc}, cnonce=\"{cnonce}\", response=\"{response}\", opaque=\"{opaque}\"";
            }
            else
            {
                response = CalculateMd5($"{ha1}:{nonce}:{ha2}");
                return $"Digest username=\"{username}\", realm=\"{realm}\", nonce=\"{nonce}\", uri=\"{uri}\", response=\"{response}\", opaque=\"{opaque}\"";
            }
        }

        private static string CalculateMd5(string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
