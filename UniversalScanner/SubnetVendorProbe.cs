using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalScanner
{
    public class VendorProbeResult
    {
        public string Vendor { get; set; } = "Unknown";
        public string Protocol { get; set; } = "Ping";
        public string Type { get; set; } = "Unknown";
    }

    public static class SubnetVendorProbe
    {
        private static void ObserveFault(Task task)
        {
            task.ContinueWith(
                faultedTask =>
                {
                    AggregateException ignored = faultedTask.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static async Task<string> ReadContentWithCancellationAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            Task<string> readTask = response.Content.ReadAsStringAsync();
            TaskCompletionSource<bool> cancellationSignal =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task completed;

            using (cancellationToken.Register(() => cancellationSignal.TrySetResult(true)))
            {
                completed = await Task.WhenAny(readTask, cancellationSignal.Task);
            }

            if (completed != readTask)
            {
                response.Dispose();
                ObserveFault(readTask);
                throw new OperationCanceledException(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return await readTask;
        }

        public static async Task<VendorProbeResult> DetectAsync(
            string ip,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            string text = "";

            text += await GetWebText(ip, "http", "/", cancellationToken);
            text += await GetWebText(ip, "http", "/ISAPI/System/deviceInfo", cancellationToken);
            text += await GetWebText(ip, "http", "/cgi-bin/magicBox.cgi?action=getSystemInfo", cancellationToken);
            text += await GetWebText(ip, "http", "/system.ini", cancellationToken);
            text += await GetWebText(ip, "https", "/", cancellationToken);

            string lower = text.ToLowerInvariant();

            if (lower.Contains("honeywell") ||
                lower.Contains("hc30") ||
                lower.Contains("hc35") ||
                lower.Contains("hc35w") ||
                lower.Contains("hc30w"))
                return Result("Honeywell", "HTTP", "Honeywell Device");

            if (lower.Contains("hikvision") ||
                lower.Contains("ds-") ||
                lower.Contains("isapi") ||
                lower.Contains("webcomponents"))
                return Result("Hikvision", "HTTP", "Hikvision Device");

            if (lower.Contains("dahua"))
                return Result("Dahua", "HTTP", "Dahua Device");

            if (lower.Contains("uniview") || lower.Contains("unv"))
                return Result("Uniview", "HTTP", ExtractModel(text, "Uniview Device"));

            if (lower.Contains("tiandy"))
                return Result("Tiandy", "HTTP", "Tiandy Device");

            if (lower.Contains("axis"))
                return Result("Axis", "HTTP", "Axis Device");

            if (lower.Contains("hanwha") || lower.Contains("wisenet"))
                return Result("Hanwha", "HTTP", "Hanwha/Wisenet Device");

            if (lower.Contains("bosch"))
                return Result("Bosch", "HTTP", "Bosch Device");

            if (lower.Contains("vivotek"))
                return Result("Vivotek", "HTTP", "Vivotek Device");

            if (lower.Contains("sony"))
                return Result("Sony", "HTTP", "Sony Device");

            if (lower.Contains("panasonic"))
                return Result("Panasonic", "HTTP", "Panasonic Device");

            if (lower.Contains("mobotix"))
                return Result("Mobotix", "HTTP", "Mobotix Device");

            if (lower.Contains("flir"))
                return Result("FLIR", "HTTP", "FLIR Device");

            if (lower.Contains("ubnt") || lower.Contains("ubiquiti"))
                return Result("Ubiquiti", "HTTP", "Ubiquiti Device");

            if (lower.Contains("pima"))
                return Result("PIMA", "HTTP", "Alarm Panel");

            return Result("Unknown", "Ping", "Unknown");
        }

        private static VendorProbeResult Result(string vendor, string protocol, string type)
        {
            return new VendorProbeResult
            {
                Vendor = vendor,
                Protocol = protocol,
                Type = type
            };
        }

        private static string ExtractModel(string text, string fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
                return fallback;

            string[] patterns =
            {
                @"<\s*(?:model|deviceModel|modelNumber|hardware)\s*>\s*([^<]+)\s*<\s*/",
                @"(?:model|deviceModel|modelNumber|hardware)\s*[:=]\s*[""']?([A-Za-z0-9][A-Za-z0-9_.\-]+)"
            };

            foreach (string pattern in patterns)
            {
                Match match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    string value = match.Groups[1].Value.Trim();

                    if (!string.IsNullOrWhiteSpace(value) &&
                        value.IndexOf("Uniview", StringComparison.OrdinalIgnoreCase) < 0 &&
                        value.IndexOf("Network", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return value;
                    }
                }
            }

            return fallback;
        }

        private static async Task<string> GetWebText(
            string ip,
            string scheme,
            string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMilliseconds(800);

                    string url = scheme + "://" + ip + path;
                    using (HttpResponseMessage response = await client.GetAsync(url, cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string body = await ReadContentWithCancellationAsync(response, cancellationToken);
                        string headers = response.Headers.ToString();

                        return headers + "\n" + body + "\n";
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return "";
            }
        }
    }
}
