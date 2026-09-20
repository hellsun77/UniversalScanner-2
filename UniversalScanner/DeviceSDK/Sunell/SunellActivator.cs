using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UniversalScanner.DeviceSDK.Honeywell;

namespace UniversalScanner.DeviceSDK.Sunell
{
    public static class SunellActivator
    {
        public static bool ActivateDevice(
            string ipAddress,
            string username,
            string newPassword,
            out string message)
        {
            message = "";

            if (string.IsNullOrWhiteSpace(username))
                username = "admin";

            try
            {
                string validationError = ValidatePassword(newPassword);
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    message = validationError;
                    return false;
                }

                StringBuilder debug = new StringBuilder();
                debug.AppendLine("Sunell activation debug");
                debug.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                debug.AppendLine("IP: " + ipAddress);
                debug.AppendLine("Username: " + username);
                debug.AppendLine("Password length: " + (newPassword == null ? 0 : newPassword.Length));
                debug.AppendLine();

                string status = SunellActivationStatusProbe.GetStatus(ipAddress);
                debug.AppendLine("Initial status: " + status);
                if (status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                {
                    message = "Sunell camera is already active.";
                    return true;
                }

                string challengeResponse = SunellWebApi.SendCommand(
                    ipAddress,
                    "11011",
                    "<WebAuthInfo OperatorType=\"1\"/>");
                debug.AppendLine("Challenge response:");
                debug.AppendLine(challengeResponse);
                debug.AppendLine();

                if (SunellWebApi.GetResultCode(challengeResponse) != 0)
                {
                    message = "Sunell activation challenge failed: " + Truncate(challengeResponse, 300);
                    return false;
                }

                string nonce = SunellWebApi.GetAttribute(challengeResponse, "WebAuthInfo", "Nonce");
                string publicKey = SunellWebApi.GetAttribute(challengeResponse, "WebAuthInfo", "PublicKey");

                if (string.IsNullOrWhiteSpace(nonce) || string.IsNullOrWhiteSpace(publicKey))
                {
                    message = "Sunell activation challenge did not return Nonce/PublicKey.";
                    return false;
                }

                string random = GenerateRandomToken();
                string cnonce = Convert.ToBase64String(Encoding.UTF8.GetBytes(random));
                string keyMaterial = Sha256Hex(nonce + cnonce);
                byte[] aesKey = Encoding.UTF8.GetBytes(keyMaterial.Substring(0, 32));
                byte[] aesIv = Encoding.UTF8.GetBytes(random);

                string encryptedPassword = HoneywellRsaHelper.AesCbcEncryptBase64(aesKey, aesIv, newPassword);
                string rsaResponse = HoneywellRsaHelper.EncryptPkcs1Base64(publicKey, encryptedPassword);
                debug.AppendLine("Generated Cnonce: " + cnonce);
                debug.AppendLine("AES ciphertext length: " + encryptedPassword.Length);
                debug.AppendLine("RSA response length: " + rsaResponse.Length);
                debug.AppendLine();

                string activateResponse = SunellWebApi.SendCommand(
                    ipAddress,
                    "11011",
                    "<WebAuthInfo OperatorType=\"3\" User=\"" + SunellWebApi.XmlEscape(username) +
                    "\" Cnonce=\"" + SunellWebApi.XmlEscape(cnonce) +
                    "\" Response=\"" + SunellWebApi.XmlEscape(rsaResponse) + "\"/>");
                debug.AppendLine("Activate response:");
                debug.AppendLine(activateResponse);
                debug.AppendLine();

                int resultCode = SunellWebApi.GetResultCode(activateResponse);
                if (resultCode == 0)
                {
                    message = "Sunell password created successfully.";
                    return true;
                }

                message = "Sunell activation failed. Result Code=" + resultCode + "\r\n\r\n" +
                    Truncate(activateResponse, 600);
                return false;
            }
            catch (Exception ex)
            {
                message = "Sunell activation failed: " + ex.Message;
                return false;
            }
        }

        public static string ValidatePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return "Sunell password is required.";

            if (password.Length > 20)
                return "Sunell password must be 20 characters or fewer.";

            if (Regex.IsMatch(password, @"[\u4e00-\u9fa5\u3000-\u303f\uff01-\uff5e]"))
                return "Sunell password cannot contain Chinese or full-width characters.";

            if (Regex.IsMatch(password.Substring(0, 1), @"[@!<>%&\\""\/,';=|+]"))
                return "Sunell password cannot start with a special character.";

            return "";
        }

        private static string GenerateRandomToken()
        {
            const string alphabet = "abcdef0123456789";
            byte[] random = new byte[16];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
                rng.GetBytes(random);

            StringBuilder sb = new StringBuilder(16);
            for (int i = 0; i < random.Length; i++)
                sb.Append(alphabet[random[i] % alphabet.Length]);

            return sb.ToString();
        }

        private static string Sha256Hex(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value ?? "";

            return value.Substring(0, max) + "...";
        }

    }
}
