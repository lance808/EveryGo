using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Imaging;
using Microsoft.Win32.SafeHandles;

namespace EveryGo
{
    /// <summary>
    /// 命令行自检：不创建窗口、不写 Registry、不改真实配置。
    /// 报告写入 %TEMP%\EveryGo.selftest.log，退出码 0 = 全部通过。
    /// </summary>
    internal static class SelfTest
    {
        private sealed class CaseResult
        {
            public string Name;
            public bool Passed;
            public string Detail;
        }

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static int Run()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "EveryGo-selftest");
            string reportPath = Path.Combine(Path.GetTempPath(), "EveryGo.selftest.log");
            List<CaseResult> cases = new List<CaseResult>();

            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
                Directory.CreateDirectory(tempRoot);
                Logger.Initialize(Path.Combine(tempRoot, "selftest.log"));

                RunCase(cases, "默认配置值", TestDefaults);
                RunCase(cases, "保存/读取 + 中文空格路径往返", delegate { TestRoundTrip(tempRoot); });
                RunCase(cases, "缺失配置自动创建默认值", delegate { TestMissingConfig(tempRoot); });
                RunCase(cases, "损坏配置备份并回退", delegate { TestCorruptConfig(tempRoot); });
                RunCase(cases, "外观数值钳制与非法枚举回落", delegate { TestClamp(tempRoot); });
                RunCase(cases, "closeAction 默认/非法值", TestCloseAction);
                RunCase(cases, "网格列数 4/5/6 回落", TestGridColumns);
                RunCase(cases, "bat 控制台模式默认/非法值", TestConsoleMode);
                RunCase(cases, "bat 引号与工作目录", TestBatCommand);
                RunCase(cases, "重复路径判断（忽略大小写）", TestDuplicatePath);
                RunCase(cases, "名称回退与重复项去重", TestNameFallback);
                RunCase(cases, "原子写入与 config.bak", delegate { TestBackup(tempRoot); });
                RunCase(cases, "面板尺寸计算", TestLayoutMetrics);
                RunCase(cases, "启动参数与工作目录", TestLaunchSpec);
                RunCase(cases, "开机自启值与路径判定", TestAutostartValue);
                RunCase(cases, "图标提取与缓存", delegate { TestIconCache(tempRoot); });
            }
            catch (Exception ex)
            {
                CaseResult result = new CaseResult();
                result.Name = "自检环境准备";
                result.Passed = false;
                result.Detail = ex.Message;
                cases.Add(result);
            }

            int passed = 0;
            int failed = 0;
            StringBuilder report = new StringBuilder();
            report.AppendLine("EveryGo selftest · v" + Program.AppVersion + " · " + Program.PhaseLabel);
            report.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("运行时：" + Environment.Version + "，OS " + Environment.OSVersion.VersionString);
            report.AppendLine("临时目录：" + tempRoot);
            report.AppendLine();

            for (int i = 0; i < cases.Count; i++)
            {
                CaseResult item = cases[i];
                string index = (i + 1 < 10 ? "0" : "") + (i + 1).ToString();
                if (item.Passed)
                {
                    passed++;
                    report.AppendLine("PASS " + index + "  " + item.Name);
                }
                else
                {
                    failed++;
                    report.AppendLine("FAIL " + index + "  " + item.Name);
                    report.AppendLine("       " + (item.Detail == null ? "" : item.Detail));
                }
            }

            report.AppendLine();
            report.AppendLine("结果：" + passed + " 通过 / " + failed + " 失败");

            try
            {
                File.WriteAllText(reportPath, report.ToString(), Utf8NoBom);
            }
            catch (Exception ex)
            {
                failed++;
                ConsoleBridge.Write("写自检报告失败：" + ex.Message + Environment.NewLine);
            }

            ConsoleBridge.Write(report.ToString());
            return failed == 0 ? 0 : 1;
        }

        private static void RunCase(List<CaseResult> cases, string name, Action body)
        {
            CaseResult result = new CaseResult();
            result.Name = name;
            result.Passed = false;
            try
            {
                body();
                result.Passed = true;
            }
            catch (Exception ex)
            {
                result.Detail = ex.Message;
                Logger.Warn("自检失败 [" + name + "] " + ex.Message);
            }
            cases.Add(result);
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void TestDefaults()
        {
            Config config = new Config();
            Expect(config.version == 1, "version 应为 1");
            Expect(config.apps != null && config.apps.Count == 0, "apps 应为空列表");
            Expect(config.window != null, "window 不应为空");
            Expect(config.window.topmost, "默认应置顶");
            Expect(!config.window.x.HasValue && !config.window.y.HasValue, "默认不应保存窗口坐标");
            Expect(config.closeAction == Config.CloseExit, "closeAction 默认应为 exit");
            Expect(config.gridColumns == 4, "gridColumns 默认应为 4");
            Expect(config.consoleMode == Config.ConsoleMinimized, "consoleMode 默认应为 minimized");
            Expect(config.appearance.type == Config.AppearanceSolid, "默认背景类型应为 solid");
            Expect(config.appearance.color == Config.DefaultColor, "默认颜色应为 #12141A");
            Expect(config.appearance.opacity == 82, "默认透明度应为 82");
            Expect(config.appearance.imageOpacity == 100, "默认图片不透明度应为 100");
            Expect(config.appearance.imageBlur == 0, "默认图片模糊应为 0");
            Expect(config.appearance.tintOpacity == 20, "默认色调强度应为 20");
            Expect(config.appearance.glass, "默认应开启玻璃");
            Expect(config.lastBrowseDir == "", "lastBrowseDir 默认为空");
        }

        private static void TestRoundTrip(string tempRoot)
        {
            AppPaths paths = AppPaths.ForDirectory(Path.Combine(tempRoot, "roundtrip"));
            ConfigStore store = new ConfigStore(paths);

            Config config = new Config();
            config.apps.Add(new AppEntry("a1b2c3", @"D:\我的 应用\测试 程序.exe", @"测试 程序"));
            config.window.x = 321;
            config.window.y = 210;
            config.window.topmost = false;
            config.closeAction = Config.CloseTray;
            config.gridColumns = 5;
            config.consoleMode = Config.ConsoleHidden;
            config.appearance.type = Config.AppearanceImage;
            config.appearance.imagePath = @"background\背景 图.png";
            config.appearance.color = "#3366CC";
            config.appearance.opacity = 55;
            config.appearance.imageOpacity = 80;
            config.appearance.imageBlur = 12;
            config.appearance.tintOpacity = 25;
            config.appearance.glass = false;
            config.lastBrowseDir = @"C:\中文 目录\工具";

            store.Save(config);
            Expect(File.Exists(paths.ConfigFile), "保存后应生成 config.json");

            string notice;
            Config loaded = store.Load(out notice);
            Expect(notice == "", "正常配置不应有损坏提示");
            Expect(loaded.apps.Count == 1, "应用数量应为 1");
            Expect(loaded.apps[0].id == "a1b2c3", "id 应保持");
            Expect(loaded.apps[0].path == @"D:\我的 应用\测试 程序.exe", "中文/空格路径应无损往返");
            Expect(loaded.apps[0].name == @"测试 程序", "中文名称应无损往返");
            Expect(loaded.window.x.HasValue && loaded.window.x.Value == 321, "窗口 x 应保持");
            Expect(loaded.window.y.HasValue && loaded.window.y.Value == 210, "窗口 y 应保持");
            Expect(!loaded.window.topmost, "topmost 应保持 false");
            Expect(loaded.closeAction == Config.CloseTray, "closeAction 应保持 tray");
            Expect(loaded.gridColumns == 5, "gridColumns 应保持 5");
            Expect(loaded.consoleMode == Config.ConsoleHidden, "consoleMode 应保持 hidden");
            Expect(loaded.appearance.type == Config.AppearanceImage, "背景类型应保持 image");
            Expect(loaded.appearance.imagePath == @"background\背景 图.png", "图片相对路径应保持");
            Expect(loaded.appearance.color == "#3366CC", "颜色应保持");
            Expect(loaded.appearance.opacity == 55, "透明度应保持");
            Expect(loaded.appearance.imageOpacity == 80, "图片不透明度应保持");
            Expect(loaded.appearance.imageBlur == 12, "图片模糊应保持");
            Expect(loaded.appearance.tintOpacity == 25, "色调强度应保持");
            Expect(!loaded.appearance.glass, "玻璃开关应保持 false");
            Expect(loaded.lastBrowseDir == @"C:\中文 目录\工具", "上次浏览目录应保持");
        }

        private static void TestMissingConfig(string tempRoot)
        {
            AppPaths paths = AppPaths.ForDirectory(Path.Combine(tempRoot, "missing"));
            ConfigStore store = new ConfigStore(paths);
            Expect(!File.Exists(paths.ConfigFile), "用例前置条件：config.json 不应存在");

            string notice;
            Config config = store.Load(out notice);
            Expect(notice == "", "缺失配置不应给出损坏提示");
            Expect(File.Exists(paths.ConfigFile), "缺失配置时应用默认值创建 config.json");
            Expect(config.apps.Count == 0, "默认无应用");
            Expect(config.closeAction == Config.CloseExit, "默认关闭行为为 exit");
            Expect(config.appearance.opacity == 82, "默认透明度 82");
        }

        private static void TestCorruptConfig(string tempRoot)
        {
            AppPaths paths = AppPaths.ForDirectory(Path.Combine(tempRoot, "corrupt"));
            File.WriteAllText(paths.ConfigFile, "{ 这不是合法 JSON", Utf8NoBom);

            ConfigStore store = new ConfigStore(paths);
            string notice;
            Config config = store.Load(out notice);
            Expect(notice.Length > 0, "损坏配置应给出提示");
            Expect(config.apps.Count == 0, "损坏配置应回退为默认应用列表");
            Expect(config.closeAction == Config.CloseExit, "损坏配置应回退默认行为");
            Expect(File.Exists(paths.ConfigFile), "回退后应重新写入 config.json");

            string[] corruptFiles = Directory.GetFiles(paths.DataDir, "config.corrupt-*.json");
            Expect(corruptFiles.Length == 1, "损坏文件应改名为 config.corrupt-<时间>.json");

            string secondNotice;
            Config second = store.Load(out secondNotice);
            Expect(secondNotice == "", "修复后的配置应能正常读取");
            Expect(second.version == 1, "修复后的配置 version 应为 1");
        }

        private static void TestClamp(string tempRoot)
        {
            AppPaths paths = AppPaths.ForDirectory(Path.Combine(tempRoot, "clamp"));
            string json =
                "{"
                + "\"version\":1,"
                + "\"apps\":[],"
                + "\"window\":{\"x\":5,\"y\":null,\"topmost\":false},"
                + "\"closeAction\":\"TRAY\","
                + "\"appearance\":{\"type\":\"video\",\"color\":\"blue\",\"opacity\":150,"
                + "\"imagePath\":\"\",\"imageOpacity\":-20,\"imageBlur\":99,\"tintOpacity\":250,\"glass\":false},"
                + "\"lastBrowseDir\":\"\","
                + "\"gridColumns\":9,"
                + "\"consoleMode\":\"bogus\""
                + "}";
            File.WriteAllText(paths.ConfigFile, json, Utf8NoBom);

            ConfigStore store = new ConfigStore(paths);
            string notice;
            Config config = store.Load(out notice);
            Expect(notice == "", "非法值不应触发损坏回退");

            Expect(config.closeAction == Config.CloseExit, "非法 closeAction 应回落 exit");
            Expect(config.appearance.type == Config.AppearanceSolid, "非法背景类型应回落 solid");
            Expect(config.appearance.color == Config.DefaultColor, "非法颜色应回落 #12141A");
            Expect(config.appearance.opacity == 100, "透明度应钳制到 100");
            Expect(config.appearance.imageOpacity == 0, "图片不透明度应钳制到 0");
            Expect(config.appearance.imageBlur == 24, "图片模糊应钳制到 24");
            Expect(config.appearance.tintOpacity == 100, "色调强度应钳制到 100");
            Expect(!config.appearance.glass, "glass 应保持 false");
            Expect(!config.window.x.HasValue && !config.window.y.HasValue, "坐标不成对时应清空");
            Expect(!config.window.topmost, "topmost 应保持 false");
            Expect(config.gridColumns == 4, "非法网格列数应回落 4");
            Expect(config.consoleMode == Config.ConsoleMinimized, "非法控制台模式应回落 minimized");
        }

        private static void TestCloseAction()
        {
            Config config = new Config();
            Expect(config.closeAction == Config.CloseExit, "默认应为 exit");
            config.closeAction = Config.CloseTray;
            config.Normalize();
            Expect(config.closeAction == Config.CloseTray, "tray 应保留");
            config.closeAction = "Exit";
            config.Normalize();
            Expect(config.closeAction == Config.CloseExit, "Exit 属非法值，应回落 exit");
            config.closeAction = "TRAY";
            config.Normalize();
            Expect(config.closeAction == Config.CloseExit, "TRAY 属非法值，应回落 exit");
            config.closeAction = null;
            config.Normalize();
            Expect(config.closeAction == Config.CloseExit, "null 应回落 exit");
        }

        private static void TestGridColumns()
        {
            int[] valid = new int[] { 4, 5, 6 };
            for (int i = 0; i < valid.Length; i++)
            {
                Config config = new Config();
                config.gridColumns = valid[i];
                config.Normalize();
                Expect(config.gridColumns == valid[i], "合法列数 " + valid[i] + " 应保留");
            }
            int[] invalid = new int[] { 0, 3, 7, -2, 99 };
            for (int i = 0; i < invalid.Length; i++)
            {
                Config config = new Config();
                config.gridColumns = invalid[i];
                config.Normalize();
                Expect(config.gridColumns == 4, "非法列数 " + invalid[i] + " 应回落 4");
            }
        }

        private static void TestConsoleMode()
        {
            Config config = new Config();
            Expect(config.consoleMode == Config.ConsoleMinimized, "默认应为 minimized");
            string[] valid = new string[] { Config.ConsoleShow, Config.ConsoleMinimized, Config.ConsoleHidden };
            for (int i = 0; i < valid.Length; i++)
            {
                config.consoleMode = valid[i];
                config.Normalize();
                Expect(config.consoleMode == valid[i], "合法模式 " + valid[i] + " 应保留");
            }
            config.consoleMode = "Hidden";
            config.Normalize();
            Expect(config.consoleMode == Config.ConsoleMinimized, "Hidden 应回落 minimized");
            config.consoleMode = null;
            config.Normalize();
            Expect(config.consoleMode == Config.ConsoleMinimized, "null 应回落 minimized");
        }

        private static void TestBatCommand()
        {
            string bat = @"C:\脚本 目录\启动 服务.bat";
            string args = LaunchCommand.BatArguments(bat);
            string expected = "/c \"\"" + bat + "\"\"";
            Expect(args == expected, "bat 参数应为 " + expected + "，实际为 " + args);
            Expect(LaunchCommand.WorkingDirectoryFor(bat) == @"C:\脚本 目录", "bat 工作目录应为脚本所在目录");
            Expect(LaunchCommand.WorkingDirectoryFor(@"C:\Tools\我的 工具.exe") == @"C:\Tools", "exe 工作目录应为其所在目录");
            Expect(LaunchCommand.IsSupportedAppFile(bat), "应识别 .bat");
            Expect(LaunchCommand.IsSupportedAppFile(@"C:\x.CMD"), "应识别大写 .CMD");
            Expect(!LaunchCommand.IsSupportedAppFile(@"C:\x.txt"), "不应识别 .txt");
            Expect(!LaunchCommand.IsSupportedAppFile(""), "空路径不应识别");
        }

        private static void TestDuplicatePath()
        {
            List<AppEntry> apps = new List<AppEntry>();
            apps.Add(new AppEntry("x", @"C:\Apps\Foo\APP.EXE", "APP"));
            Expect(Config.ContainsPath(apps, @"c:\apps\foo\app.exe"), "小写路径应判定为重复");
            Expect(Config.ContainsPath(apps, @"C:/Apps/Foo/app.exe"), "正斜杠路径应判定为重复");
            Expect(Config.ContainsPath(apps, " C:\\Apps\\Foo\\APP.EXE "), "首尾空格应忽略");
            Expect(!Config.ContainsPath(apps, @"C:\Apps\Foo\other.exe"), "不同文件不应判定为重复");
            Expect(!Config.ContainsPath(apps, ""), "空路径不应判定为重复");
        }

        private static void TestNameFallback()
        {
            Config config = new Config();
            config.apps.Add(new AppEntry("", @"C:\工具\我的 工具.exe", ""));
            config.Normalize();
            Expect(config.apps.Count == 1, "应保留 1 个应用");
            Expect(config.apps[0].name == "我的 工具", "空名称应回退为文件名去扩展名");
            Expect(config.apps[0].id.Length > 0, "空 id 应自动生成");

            config.apps.Add(new AppEntry("dup", @"c:\工具\我的 工具.exe", "重复"));
            config.Normalize();
            Expect(config.apps.Count == 1, "重复路径应被去除");
        }

        private static void TestBackup(string tempRoot)
        {
            AppPaths paths = AppPaths.ForDirectory(Path.Combine(tempRoot, "backup"));
            ConfigStore store = new ConfigStore(paths);

            Config first = new Config();
            first.lastBrowseDir = "FIRST-MARKER";
            store.Save(first);

            Config second = new Config();
            second.lastBrowseDir = "SECOND-MARKER";
            store.Save(second);

            Expect(File.Exists(paths.ConfigFile), "config.json 应存在");
            Expect(File.Exists(paths.BackupFile), "config.bak.json 应存在");
            Expect(File.ReadAllText(paths.BackupFile, Encoding.UTF8).Contains("FIRST-MARKER"), "备份应为上一版配置");
            Expect(File.ReadAllText(paths.ConfigFile, Encoding.UTF8).Contains("SECOND-MARKER"), "当前配置应为最新版");
        }

        private static void TestLayoutMetrics()
        {
            Expect(Math.Abs(LayoutMetrics.WindowWidth(4) - 384) < 0.01, "4 列窗口宽应为 384");
            Expect(Math.Abs(LayoutMetrics.WindowWidth(5) - 426) < 0.01, "5 列窗口宽应为 426");
            Expect(Math.Abs(LayoutMetrics.WindowWidth(6) - 452) < 0.01, "6 列窗口宽应为 452");
            Expect(Math.Abs(LayoutMetrics.WindowHeight(1, 4, false) - 166) < 0.01, "空状态窗口高应为 166");
            Expect(Math.Abs(LayoutMetrics.WindowHeight(1, 4, true) - 184) < 0.01, "带名称单行窗口高应为 184");
            Expect(LayoutMetrics.ItemHeight(4, true) == 90, "4 列带名称单项高应为 90");
            Expect(LayoutMetrics.TileSize(4) == 72, "4 列方块应为 72");
            Expect(LayoutMetrics.TileSize(5) == 64, "5 列方块应为 64");
            Expect(LayoutMetrics.TileSize(6) == 56, "6 列方块应为 56");
        }

        private static void TestLaunchSpec()
        {
            string exePath = @"C:\Tools 中文\我的 工具.exe";
            ProcessStartInfo exe = LaunchCommand.CreateStartInfo(exePath, Config.ConsoleMinimized);
            Expect(exe.UseShellExecute, "exe 应使用 ShellExecute");
            Expect(exe.FileName == exePath, "exe FileName 应为原路径");
            Expect(exe.WorkingDirectory == @"C:\Tools 中文", "exe 工作目录应为所在目录");
            Expect(exe.Arguments == "", "exe 不应附加参数");

            string batPath = @"C:\脚本 目录\启动 服务.bat";
            ProcessStartInfo show = LaunchCommand.CreateStartInfo(batPath, Config.ConsoleShow);
            Expect(show.FileName == "cmd.exe", "bat 应通过 cmd.exe 启动");
            Expect(show.UseShellExecute, "显示控制台应使用 ShellExecute");
            Expect(show.WindowStyle == ProcessWindowStyle.Normal, "显示模式应为 Normal");
            Expect(show.Arguments == LaunchCommand.BatArguments(batPath), "bat 双引号参数应正确");
            Expect(show.WorkingDirectory == @"C:\脚本 目录", "bat 工作目录应为脚本目录");

            ProcessStartInfo minimized = LaunchCommand.CreateStartInfo(batPath, Config.ConsoleMinimized);
            Expect(minimized.UseShellExecute, "最小化控制台应使用 ShellExecute");
            Expect(minimized.WindowStyle == ProcessWindowStyle.Minimized, "最小化模式应为 Minimized");

            ProcessStartInfo hidden = LaunchCommand.CreateStartInfo(batPath, Config.ConsoleHidden);
            Expect(!hidden.UseShellExecute, "隐藏控制台不应使用 ShellExecute");
            Expect(hidden.CreateNoWindow, "隐藏控制台应 CreateNoWindow");
            Expect(hidden.WindowStyle == ProcessWindowStyle.Hidden, "隐藏模式应为 Hidden");

            bool threw = false;
            try
            {
                LaunchCommand.CreateStartInfo(@"C:\x.txt", Config.ConsoleMinimized);
            }
            catch (ArgumentException)
            {
                threw = true;
            }
            Expect(threw, "不支持的文件类型应抛出异常");
        }

        private static void TestAutostartValue()
        {
            string exe = @"C:\Tools 中文\EveryGo.exe";
            string value = Autostart.BuildValue(exe);
            Expect(value == "\"" + exe + "\"", "自启值应为带引号的完整路径");
            Expect(Autostart.IsCurrent(value, exe), "同一路径应判定为当前");
            Expect(Autostart.IsCurrent(exe, exe), "未加引号的旧值也应兼容");
            Expect(Autostart.IsCurrent("  " + value + "  ", @"c:\tools 中文\everygo.exe"), "大小写与空格应忽略");
            Expect(!Autostart.IsCurrent(value, @"C:\Other\EveryGo.exe"), "不同路径不应判定为当前");
            Expect(!Autostart.IsCurrent("", exe), "空值不应判定为当前");
        }

        private static void TestIconCache(string tempRoot)
        {
            string iconsDir = Path.Combine(tempRoot, "icon-cache");
            IconCache.Initialize(iconsDir);

            string cmd = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            Expect(File.Exists(cmd), "系统 cmd.exe 应存在");

            bool usedFallback;
            BitmapSource icon = IconCache.GetOrExtractSync(cmd, out usedFallback);
            Expect(icon != null, "应提取到 cmd.exe 图标");
            Expect(!usedFallback, "cmd.exe 图标不应回退到通用图标");
            Expect(icon.PixelWidth > 0 && icon.PixelHeight > 0, "图标尺寸应有效");
            Expect(Directory.GetFiles(iconsDir, "*.png").Length >= 1, "应写入图标缓存 PNG");

            string key1 = IconCache.CacheKeyFor(cmd);
            string key2 = IconCache.CacheKeyFor(cmd);
            Expect(key1 == key2, "缓存键应稳定");
            Expect(IconCache.CacheKeyFor(@"C:\Other\app.exe") != key1, "不同路径缓存键应不同");

            bool missingFallback;
            BitmapSource missing = IconCache.GetOrExtractSync(
                Path.Combine(tempRoot, "not-exist.exe"), out missingFallback);
            Expect(missing != null, "缺失文件也应返回图标");
            Expect(missingFallback, "缺失文件应使用通用图标");
        }

        /// <summary>WinExe 无控制台时附着到父进程控制台输出，失败则忽略（报告文件仍有记录）。</summary>
        private static class ConsoleBridge
        {
            private static bool _attached;

            public static void Write(string text)
            {
                try
                {
                    if (!_attached)
                    {
                        _attached = true;
                        NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);
                    }
                    IntPtr handle = NativeMethods.CreateFileW(
                        "CONOUT$",
                        NativeMethods.GENERIC_WRITE,
                        NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        NativeMethods.OPEN_EXISTING,
                        0,
                        IntPtr.Zero);
                    if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;
                    using (SafeFileHandle safeHandle = new SafeFileHandle(handle, true))
                    using (FileStream stream = new FileStream(safeHandle, FileAccess.Write, 1, false))
                    using (StreamWriter writer = new StreamWriter(stream, Utf8NoBom))
                    {
                        writer.Write(text);
                        writer.Flush();
                    }
                }
                catch
                {
                    // 无父控制台时静默，报告文件是唯一可信证据。
                }
            }
        }
    }
}
