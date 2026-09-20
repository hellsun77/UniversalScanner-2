using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace UniversalScanner
{
    public class NetworkProfileManager
    {
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();

        public void SaveProfile(NetworkProfile profile, string filePath)
        {
            if (profile == null)
                throw new System.ArgumentNullException("profile");

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = serializer.Serialize(profile);
            File.WriteAllText(filePath, PrettyPrintJson(json), Encoding.UTF8);
        }

        public NetworkProfile LoadProfile(string filePath)
        {
            string json = File.ReadAllText(filePath, Encoding.UTF8);
            string normalizedJson = NormalizeJsonForProfile(serializer, json);
            NetworkProfile profile = serializer.Deserialize<NetworkProfile>(normalizedJson);
            if (profile == null)
                throw new IOException("The selected profile file is empty or invalid.");

            if (profile.Addresses == null)
                profile.Addresses = new List<NetworkProfileAddress>();

            if (profile.DnsServers == null)
                profile.DnsServers = new List<string>();

            return profile;
        }

        /// <summary>
        /// Normalizes JSON to fix PowerShell 5.1 ConvertTo-Json single-element array unwrapping.
        /// Converts a single Addresses object into a single-element array,
        /// and a single DnsServers string into a single-element array.
        /// </summary>
        internal static string NormalizeJsonForProfile(JavaScriptSerializer serializer, string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return json;

            Dictionary<string, object> dict = serializer.Deserialize<Dictionary<string, object>>(json);
            if (dict == null)
                return json;

            // Normalize Addresses: single object → single-element array
            if (dict.ContainsKey("Addresses") && dict["Addresses"] != null)
            {
                object addresses = dict["Addresses"];
                if (addresses is Dictionary<string, object>)
                {
                    ArrayList list = new ArrayList();
                    list.Add(addresses);
                    dict["Addresses"] = list;
                }
                else if (addresses is ArrayList list && list.Count == 1 && list[0] is Dictionary<string, object>)
                {
                    // Already an array with one element — leave as-is (already correct)
                }
            }

            // Normalize DnsServers: single string → single-element array
            if (dict.ContainsKey("DnsServers") && dict["DnsServers"] != null)
            {
                object dns = dict["DnsServers"];
                if (dns is string)
                {
                    ArrayList list = new ArrayList();
                    list.Add(dns);
                    dict["DnsServers"] = list;
                }
            }

            // Normalize Gateway: single string (unlikely but handle for safety)
            if (dict.ContainsKey("Gateway") && dict["Gateway"] != null)
            {
                object gateway = dict["Gateway"];
                if (gateway is ArrayList list && list.Count > 0)
                {
                    dict["Gateway"] = list[0]?.ToString() ?? "";
                }
            }

            return serializer.Serialize(dict);
        }

        private static string PrettyPrintJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "";

            StringBuilder sb = new StringBuilder();
            int indent = 0;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (escape)
                {
                    sb.Append(c);
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    sb.Append(c);
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    sb.Append(c);
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    sb.Append(c);
                    continue;
                }

                if (c == '{' || c == '[')
                {
                    sb.Append(c);
                    sb.AppendLine();
                    indent++;
                    AppendIndent(sb, indent);
                }
                else if (c == '}' || c == ']')
                {
                    sb.AppendLine();
                    indent--;
                    AppendIndent(sb, indent);
                    sb.Append(c);
                }
                else if (c == ',')
                {
                    sb.Append(c);
                    sb.AppendLine();
                    AppendIndent(sb, indent);
                }
                else if (c == ':')
                {
                    sb.Append(": ");
                }
                else if (!char.IsWhiteSpace(c))
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static void AppendIndent(StringBuilder sb, int indent)
        {
            for (int i = 0; i < indent; i++)
                sb.Append("  ");
        }
    }
}
