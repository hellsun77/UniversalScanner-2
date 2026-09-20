using System;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace UniversalScanner.DeviceSDK.ONVIF
{
    public static class OnvifSoapAuthHelper
    {
        public static string WrapSoapWithAuth(string bodyXml, string actionUrl, string destinationUrl, string username, string password)
        {
            string messageId = "urn:uuid:" + Guid.NewGuid().ToString("D");
            string safeActionUrl = SecurityElement.Escape(actionUrl ?? string.Empty);
            string safeDestinationUrl = SecurityElement.Escape(destinationUrl ?? string.Empty);
            
            string addressingHeaders = 
                "<a:Action s:mustUnderstand=\"1\">" + safeActionUrl + "</a:Action>" +
                "<a:MessageID>" + messageId + "</a:MessageID>" +
                "<a:ReplyTo>" +
                "<a:Address>http://www.w3.org/2005/08/addressing/anonymous</a:Address>" +
                "</a:ReplyTo>" +
                "<a:To s:mustUnderstand=\"1\">" + safeDestinationUrl + "</a:To>";

            if (string.IsNullOrEmpty(username))
            {
                return "<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:a=\"http://www.w3.org/2005/08/addressing\"><s:Header>" + addressingHeaders + "</s:Header><s:Body>" + bodyXml + "</s:Body></s:Envelope>";
            }
            string text = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            byte[] array = new byte[16];
            using (RNGCryptoServiceProvider rNGCryptoServiceProvider = new RNGCryptoServiceProvider())
            {
                rNGCryptoServiceProvider.GetBytes(array);
            }
            string text2 = Convert.ToBase64String(array);
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            byte[] bytes2 = Encoding.UTF8.GetBytes(password ?? "");
            byte[] array2 = new byte[array.Length + bytes.Length + bytes2.Length];
            Buffer.BlockCopy(array, 0, array2, 0, array.Length);
            Buffer.BlockCopy(bytes, 0, array2, array.Length, bytes.Length);
            Buffer.BlockCopy(bytes2, 0, array2, array.Length + bytes.Length, bytes2.Length);
            string text3;
            using (SHA1 sHA = SHA1.Create())
            {
                text3 = Convert.ToBase64String(sHA.ComputeHash(array2));
            }
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:wsse=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd\" xmlns:wsu=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd\" xmlns:a=\"http://www.w3.org/2005/08/addressing\"><s:Header>" + addressingHeaders + "<wsse:Security s:mustUnderstand=\"1\"><wsse:UsernameToken><wsse:Username>" + SecurityElement.Escape(username ?? "") + "</wsse:Username><wsse:Password Type=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest\">" + text3 + "</wsse:Password><wsse:Nonce EncodingType=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary\">" + text2 + "</wsse:Nonce><wsu:Created>" + text + "</wsu:Created></wsse:UsernameToken></wsse:Security></s:Header><s:Body>" + bodyXml + "</s:Body></s:Envelope>";
        }
    }
}
