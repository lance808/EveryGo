using System;
using Microsoft.Win32;

namespace EveryGo
{
    /// <summary>只读写 HKCU\...\Run\EveryGo，不碰其他应用的自启项。</summary>
    internal static class Autostart
    {
        public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ValueName = "EveryGo";

        public static string BuildValue(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) throw new ArgumentException("exe 路径为空。", "exePath");
            return "\"" + exePath + "\"";
        }

        public static bool IsCurrent(string registryValue, string exePath)
        {
            if (string.IsNullOrEmpty(registryValue) || string.IsNullOrEmpty(exePath)) return false;
            string value = registryValue.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                value = value.Substring(1, value.Length - 2).Trim();
            }
            return string.Equals(value, exePath.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryIsEnabled(out bool enabled, out string error)
        {
            enabled = false;
            error = "";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return true;
                    enabled = key.GetValue(ValueName) != null;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Logger.Error("读取开机自启状态失败", ex);
                return false;
            }
        }

        public static bool TrySetEnabled(bool enabled, string exePath, out string error)
        {
            error = "";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) throw new Exception("无法打开 HKCU Run 注册表项");
                    if (enabled)
                    {
                        key.SetValue(ValueName, BuildValue(exePath), RegistryValueKind.String);
                        Logger.Info("已开启开机自启：" + exePath);
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                        Logger.Info("已关闭开机自启");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Logger.Error("设置开机自启失败", ex);
                return false;
            }
        }

        /// <summary>启动时若值存在但路径不是当前 exe，自动改写为当前路径。</summary>
        public static void RepairIfNeeded(string exePath, out string notice)
        {
            notice = "";
            if (string.IsNullOrEmpty(exePath)) return;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return;
                    object raw = key.GetValue(ValueName);
                    if (raw == null) return;
                    string value = raw as string;
                    if (value != null && IsCurrent(value, exePath)) return;
                    key.SetValue(ValueName, BuildValue(exePath), RegistryValueKind.String);
                    Logger.Info("已修正开机自启路径：" + (value ?? "(非字符串值)") + " → " + exePath);
                }
            }
            catch (Exception ex)
            {
                notice = "开机自启路径修正失败：" + ex.Message;
                Logger.Error("修正开机自启路径失败", ex);
            }
        }
    }
}
