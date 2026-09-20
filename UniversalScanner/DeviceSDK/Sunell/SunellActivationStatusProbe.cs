using System;
using System.Net;
using System.Text;
using System.Xml;

namespace UniversalScanner.DeviceSDK.Sunell
{
    public static class SunellActivationStatusProbe
    {
        public static string GetModel(string ipAddress)
        {
            try
            {
                string response = SunellWebApi.SendCommand(
                    ipAddress,
                    "30541",
                    "<ChannelId Id=\"-1\"/>");

                string model = SunellWebApi.GetAttribute(response, "DeviceSummaryInfo", "ProductModel");
                if (string.IsNullOrWhiteSpace(model))
                    model = SunellWebApi.GetAttribute(response, "DeviceSummaryInfo", "HardwareModel");
                if (string.IsNullOrWhiteSpace(model))
                    model = SunellWebApi.GetAttribute(response, "DeviceSummaryInfo", "DeviceModel");

                return model ?? "";
            }
            catch
            {
                return "";
            }
        }

        public static string GetStatus(string ipAddress)
        {
            try
            {
                string response = SunellWebApi.SendCommand(
                    ipAddress,
                    "30541",
                    "<ChannelId Id=\"-1\"/>");

                string active = SunellWebApi.GetAttribute(response, "DeviceSummaryInfo", "ActivateFlag");
                if (active.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    active.Equals("1", StringComparison.OrdinalIgnoreCase))
                    return "Active";

                if (active.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                    active.Equals("0", StringComparison.OrdinalIgnoreCase))
                    return "Inactive";
            }
            catch
            {
            }

            return "Unknown";
        }
    }

    internal static class SunellWebApi
    {
        public static string SendCommand(string ipAddress, string command, string parametersXml)
        {
            string body =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<CommandHead Command=\"" + XmlEscape(command) + "\" SessionId=\"\">" +
                "<Parameters Version=\"1\">" +
                (parametersXml ?? "") +
                "</Parameters>" +
                "</CommandHead>";

            byte[] payload = Encoding.UTF8.GetBytes(body);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://" + ipAddress + "/action/WEB_JsonAjax");
            request.Method = "POST";
            request.ContentType = "application/xml";
            request.Timeout = 8000;
            request.ReadWriteTimeout = 8000;
            request.ContentLength = payload.Length;

            using (var stream = request.GetRequestStream())
                stream.Write(payload, 0, payload.Length);

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (var responseStream = response.GetResponseStream())
            using (var reader = new System.IO.StreamReader(responseStream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        public static int GetResultCode(string xml)
        {
            string code = GetAttribute(xml, "Result", "Code");
            int value;
            return int.TryParse(code, out value) ? value : int.MinValue;
        }

        public static string GetAttribute(string xml, string elementName, string attributeName)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return "";

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);

            foreach (XmlElement element in doc.GetElementsByTagName("*"))
            {
                if (element.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase))
                    return element.GetAttribute(attributeName) ?? "";
            }

            return "";
        }

        public static string XmlEscape(string value)
        {
            return SecurityElementEscape(value);
        }

        private static string SecurityElementEscape(string value)
        {
            return System.Security.SecurityElement.Escape(value ?? "") ?? "";
        }
    }
}
