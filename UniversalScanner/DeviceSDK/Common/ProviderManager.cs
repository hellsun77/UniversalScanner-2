using System.Collections.Generic;
using System.Linq;
using UniversalScanner.DeviceSDK.Hikvision;

namespace UniversalScanner.DeviceSDK.Common
{
    public static class ProviderManager
    {
        private static readonly List<IDeviceProvider> Providers =
            new List<IDeviceProvider>()
            {
                new HikvisionProvider()
            };

        public static IDeviceProvider GetProvider(string vendor)
        {
            return Providers.FirstOrDefault(
                p => p.SupportsVendor(vendor));
        }
    }
}