using System;
using System.IO;
using System.Reflection;

namespace EveryGo
{
    /// <summary>
    /// 数据目录解析：优先 exe 同目录（便携），不可写时回退到 %APPDATA%\EveryGo。
    /// </summary>
    internal sealed class AppPaths
    {
        public string ExeDir { get; private set; }
        public string DataDir { get; private set; }
        public bool IsPortable { get; private set; }

        public string ConfigFile { get { return Path.Combine(DataDir, "config.json"); } }
        public string BackupFile { get { return Path.Combine(DataDir, "config.bak.json"); } }
        public string LogFile { get { return Path.Combine(DataDir, "EveryGo.log"); } }
        public string IconsDir { get { return Path.Combine(DataDir, "icons"); } }
        public string BackgroundDir { get { return Path.Combine(DataDir, "background"); } }

        private AppPaths()
        {
        }

        public static AppPaths Resolve()
        {
            AppPaths paths = new AppPaths();
            paths.ExeDir = GetExeDir();

            string portableDir = Path.Combine(paths.ExeDir, "EveryGo.data");
            if (TryPrepareWritableDir(portableDir))
            {
                paths.DataDir = portableDir;
                paths.IsPortable = true;
            }
            else
            {
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(roaming))
                {
                    roaming = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                }
                string fallbackDir = Path.Combine(roaming, "EveryGo");
                Directory.CreateDirectory(fallbackDir);
                paths.DataDir = fallbackDir;
                paths.IsPortable = false;
            }

            Directory.CreateDirectory(paths.DataDir);
            return paths;
        }

        /// <summary>仅供自检使用：指定数据目录，不触发便携/AppData 回退逻辑。</summary>
        public static AppPaths ForDirectory(string dataDir)
        {
            if (string.IsNullOrEmpty(dataDir)) throw new ArgumentException("数据目录为空。", "dataDir");
            AppPaths paths = new AppPaths();
            paths.ExeDir = Path.GetDirectoryName(dataDir);
            paths.DataDir = dataDir;
            paths.IsPortable = true;
            Directory.CreateDirectory(dataDir);
            return paths;
        }

        private static string GetExeDir()
        {
            string exePath = null;
            try
            {
                exePath = Assembly.GetExecutingAssembly().Location;
            }
            catch
            {
            }
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = AppDomain.CurrentDomain.BaseDirectory;
            }
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = Environment.CurrentDirectory;
            }
            string dir = Path.GetDirectoryName(exePath);
            return string.IsNullOrEmpty(dir) ? Environment.CurrentDirectory : dir;
        }

        private static bool TryPrepareWritableDir(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write-probe");
                File.WriteAllText(probe, "probe");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class Logger
    {
        private static readonly object SyncRoot = new object();
        private static string _filePath;

        public static string FilePath { get { return _filePath; } }

        public static void Initialize(string filePath)
        {
            _filePath = filePath;
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
        }

        private static void Write(string level, string message, Exception exception)
        {
            try
            {
                lock (SyncRoot)
                {
                    if (string.IsNullOrEmpty(_filePath)) return;
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                        + " [" + level + "] " + message;
                    if (exception != null)
                    {
                        line += Environment.NewLine + exception;
                    }
                    File.AppendAllText(_filePath, line + Environment.NewLine, new System.Text.UTF8Encoding(false));
                }
            }
            catch
            {
                // 日志写入失败时没有可靠的落点，避免二次异常。
            }
        }
    }
}
