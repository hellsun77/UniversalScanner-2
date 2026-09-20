using System;
using System.Security.Cryptography;
using System.Text;

namespace UniversalScanner
{
    public static class UniversalLoginManager
    {
        private const string ProtectedPrefix = "dpapi:";

        public static bool HasUniversalCredentials()
        {
            string username;
            string password;
            GetUniversalCredentials(out username, out password);
            return !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);
        }

        public static void GetUniversalCredentials(out string username, out string password)
        {
            Config config = Config.getInstance();
            username = config.UniversalUsername ?? "";
            password = Unprotect(config.UniversalPassword);
        }

        public static void SetUniversalCredentials(string username, string password)
        {
            Config config = Config.getInstance();
            config.UniversalUsername = (username ?? "").Trim();
            config.UniversalPassword = Protect(password ?? "");
        }

        public static void ClearUniversalCredentials()
        {
            Config config = Config.getInstance();
            config.UniversalUsername = "";
            config.UniversalPassword = "";
        }

        private static string Protect(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            byte[] plain = Encoding.UTF8.GetBytes(value);
            byte[] protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            return ProtectedPrefix + Convert.ToBase64String(protectedBytes);
        }

        private static string Unprotect(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            try
            {
                if (!value.StartsWith(ProtectedPrefix, StringComparison.OrdinalIgnoreCase))
                    return value;

                string payload = value.Substring(ProtectedPrefix.Length);
                byte[] protectedBytes = Convert.FromBase64String(payload);
                byte[] plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return "";
            }
        }
    }
}
