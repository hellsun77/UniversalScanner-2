using System;
using System.IO;
using System.Threading;

namespace UniversalScanner
{
    public static class OnvifTraceLogger
    {
        private static readonly object _lock = new object();
        private static long _requestCounter = 0;
        public static AsyncLocal<string> CurrentCorrelationId = new AsyncLocal<string>();

        public static void SetCorrelationId(string prefix)
        {
            CurrentCorrelationId.Value = $"{prefix}-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 4)}";
        }

        public static string GetCorrelationId()
        {
            return CurrentCorrelationId.Value ?? $"BG-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 4)}";
        }

        public static void LogRequest(
            string component,
            string className,
            string methodName,
            string endpoint,
            string soapAction,
            string httpMethod,
            string authType,
            string username,
            string passwordSource,
            int passwordLength,
            string httpStatus,
            string soapFault,
            int retryNumber,
            bool newClientInstance,
            bool credentialsCopied,
            long durationMs)
        {
            try
            {
                long reqNum = Interlocked.Increment(ref _requestCounter);
                string corrId = GetCorrelationId();
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                int threadId = Thread.CurrentThread.ManagedThreadId;

                string logEntry = $"============================================================\r\n" +
                                  $"Request #{reqNum}\r\n" +
                                  $"Correlation ID: {corrId}\r\n" +
                                  $"Timestamp: {timestamp}\r\n" +
                                  $"Thread ID: {threadId}\r\n" +
                                  $"Duration: {durationMs} ms\r\n" +
                                  $"Component: {component}\r\n" +
                                  $"Class: {className}\r\n" +
                                  $"Method: {methodName}\r\n" +
                                  $"Endpoint: {endpoint}\r\n" +
                                  $"SOAP Action: {soapAction}\r\n" +
                                  $"HTTP Method: {httpMethod}\r\n" +
                                  $"Authentication Type: {authType}\r\n" +
                                  $"Username: {username}\r\n" +
                                  $"Password Source: {passwordSource}\r\n" +
                                  $"Password Length: {passwordLength}\r\n" +
                                  $"HTTP Status: {httpStatus}\r\n" +
                                  $"SOAP Fault: {soapFault}\r\n" +
                                  $"Retry Number: {retryNumber}\r\n" +
                                  $"New Client Instance: {newClientInstance}\r\n" +
                                  $"Credentials Copied: {credentialsCopied}\r\n\r\n";

                lock (_lock)
                {
                    File.AppendAllText(@"UniversalScanner\bin\Release\OnvifTrace.log", logEntry);
                }
            }
            catch { }
        }
    }
}
