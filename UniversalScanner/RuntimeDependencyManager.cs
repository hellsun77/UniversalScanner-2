using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace UniversalScanner
{
    internal static class RuntimeDependencyManager
    {
        private const string ResourcePrefix = "UniversalScanner.SDK.";
        private const string TempRootName = "UniversalScanner";
        private static readonly object SyncRoot = new object();
        private static string extractionRoot;

        private static readonly string[] SdkFiles =
        {
            @"Hikvision\AudioRender.dll",
            @"Hikvision\HCCore.dll",
            @"Hikvision\HCNetSDK.dll",
            @"Hikvision\hlog.dll",
            @"Hikvision\libeay32.dll",
            @"Hikvision\PlayCtrl.dll",
            @"Hikvision\ssleay32.dll",
            @"Hikvision\SuperRender.dll",
            @"Hikvision\HCNetSDKCom\AnalyzeData.dll",
            @"Hikvision\HCNetSDKCom\AudioIntercom.dll",
            @"Hikvision\HCNetSDKCom\HCAlarm.dll",
            @"Hikvision\HCNetSDKCom\HCAlarm.lib",
            @"Hikvision\HCNetSDKCom\HCCoreDevCfg.dll",
            @"Hikvision\HCNetSDKCom\HCDisplay.dll",
            @"Hikvision\HCNetSDKCom\HCGeneralCfgMgr.dll",
            @"Hikvision\HCNetSDKCom\HCGeneralCfgMgr.lib",
            @"Hikvision\HCNetSDKCom\HCIndustry.dll",
            @"Hikvision\HCNetSDKCom\HCPlayBack.dll",
            @"Hikvision\HCNetSDKCom\HCPreview.dll",
            @"Hikvision\HCNetSDKCom\HCPreview.lib",
            @"Hikvision\HCNetSDKCom\HCVoiceTalk.dll",
            @"Hikvision\HCNetSDKCom\libiconv2.dll",
            @"Hikvision\HCNetSDKCom\msvcr90.dll",
            @"Hikvision\HCNetSDKCom\OpenAL32.dll",
            @"Hikvision\HCNetSDKCom\StreamTransClient.dll",
            @"Hikvision\HCNetSDKCom\SystemTransform.dll"
        };

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, int dwFlags);

        public static string SdkRoot
        {
            get
            {
                if (string.IsNullOrWhiteSpace(extractionRoot))
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SDK");

                return Path.Combine(extractionRoot, "SDK");
            }
        }

        public static void Prepare()
        {
            lock (SyncRoot)
            {
                if (!string.IsNullOrWhiteSpace(extractionRoot))
                    return;

                CleanupStaleFolders();

                extractionRoot = Path.Combine(
                    Path.GetTempPath(),
                    TempRootName,
                    Process.GetCurrentProcess().Id + "-" + Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(extractionRoot);
                ExtractSdkFiles(extractionRoot);
            }
        }

        public static void Cleanup()
        {
            lock (SyncRoot)
            {
                if (string.IsNullOrWhiteSpace(extractionRoot))
                    return;

                TryDeleteDirectory(extractionRoot, true);
                extractionRoot = null;
            }
        }

        private static void ExtractSdkFiles(string root)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            foreach (string relativeSdkFile in SdkFiles)
            {
                string resourceName = ResourcePrefix + relativeSdkFile.Replace('\\', '.');
                string outputPath = Path.Combine(root, "SDK", relativeSdkFile);
                string outputDirectory = Path.GetDirectoryName(outputPath);

                if (!string.IsNullOrWhiteSpace(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                using (Stream resourceStream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (resourceStream == null)
                        throw new FileNotFoundException("Embedded SDK resource was not found: " + resourceName);

                    using (FileStream fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        resourceStream.CopyTo(fileStream);
                    }
                }
            }
        }

        private static void CleanupStaleFolders()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), TempRootName);
            if (!Directory.Exists(tempRoot))
                return;

            foreach (string folder in Directory.GetDirectories(tempRoot))
            {
                TryDeleteDirectory(folder, false);
            }
        }

        private static void TryDeleteDirectory(string path, bool scheduleLockedFiles)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
                if (scheduleLockedFiles)
                    ScheduleDirectoryForDeletion(path);
            }
        }

        private static void ScheduleDirectoryForDeletion(string path)
        {
            if (!Directory.Exists(path))
                return;

            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                MoveFileEx(file, null, 0x4);

            foreach (string directory in GetDirectoriesDeepestFirst(path))
                MoveFileEx(directory, null, 0x4);

            MoveFileEx(path, null, 0x4);
        }

        private static IEnumerable<string> GetDirectoriesDeepestFirst(string path)
        {
            List<string> directories = new List<string>(Directory.GetDirectories(path, "*", SearchOption.AllDirectories));
            directories.Sort((left, right) => right.Length.CompareTo(left.Length));
            return directories;
        }
    }
}
