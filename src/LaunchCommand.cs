using System;
using System.Diagnostics;
using System.IO;

namespace EveryGo
{
    /// <summary>启动命令构造（纯函数，便于自检）。</summary>
    internal static class LaunchCommand
    {
        public const string CmdExe = "cmd.exe";

        /// <summary>bat/cmd 命令行：cmd.exe /c ""&lt;完整路径&gt;""，兼容空格与中文路径。</summary>
        public static string BatArguments(string scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath)) throw new ArgumentException("脚本路径为空。", "scriptPath");
            return "/c \"\"" + scriptPath + "\"\"";
        }

        public static string WorkingDirectoryFor(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                string dir = Path.GetDirectoryName(path);
                return dir == null ? "" : dir;
            }
            catch
            {
                return "";
            }
        }

        public static bool IsSupportedAppFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext;
            try
            {
                ext = Path.GetExtension(path);
            }
            catch
            {
                return false;
            }
            return string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".cmd", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsScript(string path)
        {
            string ext;
            try
            {
                ext = Path.GetExtension(path);
            }
            catch
            {
                return false;
            }
            return string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".cmd", StringComparison.OrdinalIgnoreCase);
        }

        public static ProcessStartInfo CreateStartInfo(string path, string consoleMode)
        {
            if (!IsSupportedAppFile(path)) throw new ArgumentException("不支持的应用程序类型：" + path, "path");

            ProcessStartInfo info = new ProcessStartInfo();
            info.WorkingDirectory = WorkingDirectoryFor(path);

            if (!IsScript(path))
            {
                info.FileName = path;
                info.Arguments = "";
                info.UseShellExecute = true;
                return info;
            }

            info.FileName = CmdExe;
            info.Arguments = BatArguments(path);
            if (consoleMode == Config.ConsoleHidden)
            {
                // 隐藏控制台必须走 CreateNoWindow；ShellExecute 无法可靠隐藏。
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WindowStyle = ProcessWindowStyle.Hidden;
            }
            else
            {
                info.UseShellExecute = true;
                info.WindowStyle = consoleMode == Config.ConsoleMinimized
                    ? ProcessWindowStyle.Minimized
                    : ProcessWindowStyle.Normal;
            }
            return info;
        }
    }
}
