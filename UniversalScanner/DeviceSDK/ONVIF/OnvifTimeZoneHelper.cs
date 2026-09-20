using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalScanner.DeviceSDK.ONVIF
{
    public static class OnvifTimeZoneHelper
    {
        private static readonly Dictionary<string, string> _friendlyToPosix = new Dictionary<string, string>
        {
            {"UTC", "GMT0"},
            {"Eastern Standard Time", "EST5EDT"},
            {"Central Standard Time", "CST6CDT"},
            {"Mountain Standard Time", "MST7MDT"},
            {"Pacific Standard Time", "PST8PDT"},
            {"Israel Standard Time", "IST-2IDT"},
            {"Palestine Standard Time", "EET-2EEST"},
            {"Greenwich Mean Time", "GMT0BST"},
            {"Central European Time", "CET-1CEST"},
            {"Eastern European Time", "EET-2EEST"},
            {"China Standard Time", "CST-8"},
            {"Japan Standard Time", "JST-9"},
            {"Korea Standard Time", "KST-9"},
            {"India Standard Time", "IST-5:30"}
        };

        public static List<string> GetFriendlyNames()
        {
            var list = _friendlyToPosix.Keys.ToList();
            list.Sort();
            return list;
        }

        public static string GetPosixFromFriendly(string friendlyName)
        {
            if (string.IsNullOrWhiteSpace(friendlyName)) return "";

            if (_friendlyToPosix.TryGetValue(friendlyName, out string posix))
                return posix;

            if (friendlyName.StartsWith("Camera value: "))
                return friendlyName.Substring(14);

            return friendlyName;
        }

        public static string GetFriendlyFromPosix(string posix)
        {
            if (string.IsNullOrWhiteSpace(posix)) return "";
            
            var match = _friendlyToPosix.FirstOrDefault(x => string.Equals(x.Value, posix, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match.Key))
                return match.Key;

            return $"Camera value: {posix}";
        }
        
        public static bool IsKnownFriendlyName(string name)
        {
            return _friendlyToPosix.ContainsKey(name);
        }
    }
}
