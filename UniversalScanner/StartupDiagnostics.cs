using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Linq;

namespace UniversalScanner
{
    public static class StartupDiagnostics
    {
        public static string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_diagnostic.log");
        
        public static Guid ScanSessionId = Guid.NewGuid();
        public static DateTime StartTime = DateTime.Now;
        public static Stopwatch AppLifetime = Stopwatch.StartNew();

        public static ConcurrentDictionary<string, int> Counters = new ConcurrentDictionary<string, int>();
        public static ConcurrentDictionary<string, long> Timers = new ConcurrentDictionary<string, long>();
        
        public static ConcurrentDictionary<string, byte> InFlightProbes = new ConcurrentDictionary<string, byte>();

        public static ConcurrentBag<string> SlowOperations = new ConcurrentBag<string>();
        
        public static long MaxUiDelayMs = 0;
        public static long TotalUiDelayMs = 0;
        public static int UiTicks = 0;
        public static long FirstResponsiveTimeMs = -1;
        
        private static object _logLock = new object();
        private static Stopwatch _lastWrite = Stopwatch.StartNew();

        public static void Increment(string name)
        {
            Counters.AddOrUpdate(name, 1, (k, v) => v + 1);
        }

        public static void AddTimer(string name, long elapsedMs)
        {
            Timers.AddOrUpdate(name, elapsedMs, (k, v) => v + elapsedMs);
        }

        public static void LogSlowOperation(string type, string ip, string brand, long durationMs, bool success)
        {
            if (durationMs > 500)
            {
                SlowOperations.Add($"{DateTime.Now:HH:mm:ss.fff} | Type: {type} | IP: {ip} | Brand: {brand} | Duration: {durationMs}ms | Success: {success} | Thread: {Thread.CurrentThread.ManagedThreadId}");
            }
        }

        public static bool TryStartProbe(string key)
        {
            bool added = InFlightProbes.TryAdd(key, 1);
            if (!added) Increment("RepeatedProbesPrevented");
            return added; // We don't permanently block, we just count them if we want, or actually block. The prompt says "Number of repeated probes prevented... Do not permanently block future manual rescans. The guard must apply only while the current probe is running."
        }

        public static void EndProbe(string key)
        {
            InFlightProbes.TryRemove(key, out _);
        }

        public static void WriteLog(int uniqueDevices = 0)
        {
#if DEBUG
            if (_lastWrite.ElapsedMilliseconds < 3000) return;
            
            lock (_logLock)
            {
                if (_lastWrite.ElapsedMilliseconds < 3000) return;
                _lastWrite.Restart();

                try {
                    int workerThreads, ioThreads;
                    ThreadPool.GetAvailableThreads(out workerThreads, out ioThreads);
                    int maxWorkerThreads, maxIoThreads;
                    ThreadPool.GetMaxThreads(out maxWorkerThreads, out maxIoThreads);
                    int activeTasks = maxWorkerThreads - workerThreads;

                    using (var writer = new StreamWriter(LogPath, false))
                    {
                        writer.WriteLine($"--- DIAGNOSTIC REPORT ---");
                        writer.WriteLine($"App Start: {StartTime:yyyy-MM-dd HH:mm:ss.fff}");
                        writer.WriteLine($"Scan Session ID: {ScanSessionId}");
                        writer.WriteLine($"Total Uptime (ms): {AppLifetime.ElapsedMilliseconds}");
                        writer.WriteLine($"Unique Devices Discovered: {uniqueDevices}");
                        writer.WriteLine($"Active Worker Tasks: {activeTasks}");
                        writer.WriteLine($"ThreadPool Available Workers: {workerThreads}");
                        writer.WriteLine($"Max UI Heartbeat Delay: {MaxUiDelayMs} ms");
                        writer.WriteLine($"Avg UI Heartbeat Delay: {(UiTicks > 0 ? TotalUiDelayMs / UiTicks : 0)} ms");
                        writer.WriteLine($"First Responsive Time (ms): {(FirstResponsiveTimeMs > 0 ? FirstResponsiveTimeMs.ToString() : "Not yet responsive")}");
                        
                        writer.WriteLine("\n--- COUNTERS ---");
                        foreach (var kv in Counters.OrderBy(k => k.Key)) writer.WriteLine($"{kv.Key}: {kv.Value}");
                        
                        writer.WriteLine("\n--- TIMERS (Total ms) ---");
                        foreach (var kv in Timers.OrderBy(k => k.Key)) writer.WriteLine($"{kv.Key}: {kv.Value}");
                        
                        writer.WriteLine("\n--- SLOW OPERATIONS (>500ms) ---");
                        foreach (var op in SlowOperations.OrderBy(x => x)) writer.WriteLine(op);
                    }
                } 
                catch 
                {
                    // Ignore I/O errors here
                }
            }
#endif
        }
    }
}
