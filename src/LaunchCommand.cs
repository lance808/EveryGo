using System;
using System.IO;

namespace EveryGo
{
    /// <summary>启动命令构造（纯函数，便于自检；实际启动在 Phase 2 接入）。</summary>
    internal static class LaunchCommand
    {
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
    }
}
