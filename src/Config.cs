using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace EveryGo
{
    public sealed class AppEntry
    {
        public string id { get; set; }
        public string path { get; set; }
        public string name { get; set; }

        public AppEntry()
        {
            id = "";
            path = "";
            name = "";
        }

        public AppEntry(string idValue, string pathValue, string nameValue)
        {
            id = idValue ?? "";
            path = pathValue ?? "";
            name = nameValue ?? "";
        }
    }

    public sealed class WindowConfig
    {
        public int? x { get; set; }
        public int? y { get; set; }
        public bool topmost { get; set; }

        public WindowConfig()
        {
            x = null;
            y = null;
            topmost = true;
        }
    }

    public sealed class AppearanceConfig
    {
        public string type { get; set; }
        public string color { get; set; }
        public int opacity { get; set; }
        public string imagePath { get; set; }
        public int imageOpacity { get; set; }
        public int imageBlur { get; set; }
        public int tintOpacity { get; set; }
        public bool glass { get; set; }

        public AppearanceConfig()
        {
            type = Config.AppearanceSolid;
            color = Config.DefaultColor;
            opacity = Config.DefaultOpacity;
            imagePath = "";
            imageOpacity = 100;
            imageBlur = 0;
            tintOpacity = 20;
            glass = true;
        }

        public void Normalize()
        {
            if (type != Config.AppearanceImage && type != Config.AppearanceSolid)
            {
                type = Config.AppearanceSolid;
            }
            if (imagePath == null) imagePath = "";
            if (type == Config.AppearanceImage && imagePath.Length == 0)
            {
                type = Config.AppearanceSolid;
            }
            if (!Config.IsValidHexColor(color))
            {
                color = Config.DefaultColor;
            }
            else
            {
                color = color.ToUpperInvariant();
            }
            opacity = Config.ClampInt(opacity, 0, 100);
            imageOpacity = Config.ClampInt(imageOpacity, 0, 100);
            imageBlur = Config.ClampInt(imageBlur, 0, 24);
            tintOpacity = Config.ClampInt(tintOpacity, 0, 100);
        }
    }

    public sealed class Config
    {
        public const int SchemaVersion = 1;

        public const string DefaultColor = "#12141A";
        public const int DefaultOpacity = 82;

        public const string CloseExit = "exit";
        public const string CloseTray = "tray";

        public const string ConsoleShow = "show";
        public const string ConsoleMinimized = "minimized";
        public const string ConsoleHidden = "hidden";

        public const string AppearanceSolid = "solid";
        public const string AppearanceImage = "image";

        public static readonly int[] GridColumnOptions = new int[] { 4, 5, 6 };

        public int version { get; set; }
        public List<AppEntry> apps { get; set; }
        public WindowConfig window { get; set; }
        public string closeAction { get; set; }
        public AppearanceConfig appearance { get; set; }
        public string lastBrowseDir { get; set; }
        public int gridColumns { get; set; }
        public string consoleMode { get; set; }

        public Config()
        {
            version = SchemaVersion;
            apps = new List<AppEntry>();
            window = new WindowConfig();
            closeAction = CloseExit;
            appearance = new AppearanceConfig();
            lastBrowseDir = "";
            gridColumns = 4;
            consoleMode = ConsoleMinimized;
        }

        public void Normalize()
        {
            version = SchemaVersion;

            if (apps == null) apps = new List<AppEntry>();
            List<AppEntry> cleaned = new List<AppEntry>();
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < apps.Count; i++)
            {
                AppEntry app = apps[i];
                if (app == null) continue;
                if (app.path == null) app.path = "";
                app.path = app.path.Trim();
                if (app.path.Length == 0) continue;
                if (ContainsPath(cleaned, app.path)) continue;

                if (app.name == null) app.name = "";
                app.name = app.name.Trim();
                if (app.name.Length == 0) app.name = NameFromPath(app.path);

                if (app.id == null) app.id = "";
                app.id = app.id.Trim();
                if (app.id.Length == 0 || ids.Contains(app.id)) app.id = CreateId();
                ids.Add(app.id);
                cleaned.Add(app);
            }
            apps = cleaned;

            if (window == null) window = new WindowConfig();
            if (window.x.HasValue != window.y.HasValue)
            {
                window.x = null;
                window.y = null;
            }

            if (closeAction != CloseTray) closeAction = CloseExit;

            if (appearance == null) appearance = new AppearanceConfig();
            appearance.Normalize();

            if (!IsValidGridColumnCount(gridColumns)) gridColumns = 4;
            if (!IsValidConsoleMode(consoleMode)) consoleMode = ConsoleMinimized;
            if (lastBrowseDir == null) lastBrowseDir = "";
        }

        public static bool ContainsPath(IEnumerable<AppEntry> apps, string path)
        {
            if (apps == null) return false;
            string target = NormalizePathForCompare(path);
            if (target.Length == 0) return false;
            foreach (AppEntry app in apps)
            {
                if (app == null) continue;
                if (string.Equals(NormalizePathForCompare(app.path), target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static string NormalizePathForCompare(string path)
        {
            if (path == null) return "";
            string value = path.Trim();
            if (value.Length == 0) return "";
            try
            {
                value = Path.GetFullPath(value);
            }
            catch
            {
                // 非法路径按原字符串比较。
            }
            return value.Replace('/', '\\');
        }

        public static string NameFromPath(string path)
        {
            if (path == null) return "";
            try
            {
                return Path.GetFileNameWithoutExtension(path);
            }
            catch
            {
                return "";
            }
        }

        public static string CreateId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        public static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static bool IsValidHexColor(string value)
        {
            if (value == null || value.Length != 7 || value[0] != '#') return false;
            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                bool isDigit = c >= '0' && c <= '9';
                bool isUpper = c >= 'A' && c <= 'F';
                bool isLower = c >= 'a' && c <= 'f';
                if (!isDigit && !isUpper && !isLower) return false;
            }
            return true;
        }

        public static bool IsValidGridColumnCount(int value)
        {
            return value == 4 || value == 5 || value == 6;
        }

        public static bool IsValidConsoleMode(string value)
        {
            return value == ConsoleShow || value == ConsoleMinimized || value == ConsoleHidden;
        }

        public static int IndexOfGridColumnOption(int columns)
        {
            for (int i = 0; i < GridColumnOptions.Length; i++)
            {
                if (GridColumnOptions[i] == columns) return i;
            }
            return 0;
        }
    }

    /// <summary>配置读写：原子写入、保留 bak、损坏回退。</summary>
    internal sealed class ConfigStore
    {
        private readonly AppPaths _paths;

        public ConfigStore(AppPaths paths)
        {
            if (paths == null) throw new ArgumentNullException("paths");
            _paths = paths;
        }

        public AppPaths Paths { get { return _paths; } }

        public Config Load(out string notice)
        {
            notice = "";
            if (!File.Exists(_paths.ConfigFile))
            {
                Config defaults = new Config();
                Save(defaults);
                Logger.Info("未找到配置文件，已按默认值创建：" + _paths.ConfigFile);
                return defaults;
            }

            try
            {
                string text = File.ReadAllText(_paths.ConfigFile, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new InvalidDataException("配置文件为空");
                }
                JavaScriptSerializer serializer = CreateSerializer();
                Config config = serializer.Deserialize<Config>(text);
                if (config == null)
                {
                    throw new InvalidDataException("配置内容为空");
                }
                config.Normalize();
                return config;
            }
            catch (Exception ex)
            {
                Logger.Warn("读取配置失败：" + ex.Message);
                string corruptPath = MoveAsideCorruptFile();
                Config fresh = new Config();
                try
                {
                    Save(fresh);
                }
                catch (Exception saveEx)
                {
                    Logger.Error("损坏配置回退时写入默认配置失败", saveEx);
                    throw;
                }
                notice = corruptPath != null
                    ? "配置文件损坏，已备份为 " + Path.GetFileName(corruptPath) + "，并以默认值启动。"
                    : "配置文件损坏，已重置为默认值。";
                Logger.Warn(notice);
                return fresh;
            }
        }

        public void Save(Config config)
        {
            if (config == null) throw new ArgumentNullException("config");
            config.Normalize();

            string json;
            try
            {
                json = CreateSerializer().Serialize(config);
            }
            catch (Exception ex)
            {
                Logger.Error("配置序列化失败", ex);
                throw new IOException("配置序列化失败：" + ex.Message, ex);
            }

            try
            {
                Directory.CreateDirectory(_paths.DataDir);
                string tempFile = _paths.ConfigFile + ".tmp";
                File.WriteAllText(tempFile, json, new UTF8Encoding(false));

                if (File.Exists(_paths.ConfigFile))
                {
                    try
                    {
                        File.Replace(tempFile, _paths.ConfigFile, _paths.BackupFile, true);
                    }
                    catch (Exception replaceEx)
                    {
                        // 某些文件系统不支持 Replace，退化为复制 + 备份。
                        Logger.Warn("原子替换失败，改用复制写入：" + replaceEx.Message);
                        File.Copy(_paths.ConfigFile, _paths.BackupFile, true);
                        File.Copy(tempFile, _paths.ConfigFile, true);
                        try { File.Delete(tempFile); } catch { }
                    }
                }
                else
                {
                    File.Move(tempFile, _paths.ConfigFile);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("保存配置失败：" + _paths.ConfigFile, ex);
                throw new IOException("保存配置失败：" + ex.Message, ex);
            }
        }

        private string MoveAsideCorruptFile()
        {
            try
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string target;
                int index = 0;
                do
                {
                    string suffix = index == 0 ? "" : "-" + index.ToString();
                    target = Path.Combine(_paths.DataDir, "config.corrupt-" + stamp + suffix + ".json");
                    index++;
                }
                while (File.Exists(target));

                File.Move(_paths.ConfigFile, target);
                return target;
            }
            catch (Exception ex)
            {
                Logger.Warn("备份损坏配置失败：" + ex.Message);
                return null;
            }
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            serializer.RecursionLimit = 100;
            return serializer;
        }
    }
}
