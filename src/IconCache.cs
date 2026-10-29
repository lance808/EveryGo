using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace EveryGo
{
    /// <summary>
    /// 应用图标提取与缓存：优先 shell 大图标（256），回退 32px，最终使用系统通用图标。
    /// 缓存键 = 路径 + 修改时间 + 大小；缓存文件位于数据目录 icons\&lt;hash&gt;.png。
    /// </summary>
    internal static class IconCache
    {
        private static string _iconsDir;
        private static BitmapSource _defaultIcon;
        private static readonly object InitLock = new object();
        private static readonly object DefaultLock = new object();
        private static readonly object CacheWriteLock = new object();

        private const string KeySalt = "EveryGo.icon.v1";

        public static void Initialize(string iconsDir)
        {
            _iconsDir = iconsDir;
            try
            {
                if (!string.IsNullOrEmpty(iconsDir))
                {
                    Directory.CreateDirectory(iconsDir);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("创建图标缓存目录失败：" + iconsDir, ex);
            }
        }

        public static string CacheKeyFor(string path)
        {
            long ticks = 0;
            long size = 0;
            try
            {
                FileInfo info = new FileInfo(path);
                if (info.Exists)
                {
                    ticks = info.LastWriteTimeUtc.Ticks;
                    size = info.Length;
                }
            }
            catch
            {
            }

            string raw = KeySalt + "|" + Config.NormalizePathForCompare(path)
                + "|" + ticks.ToString() + "|" + size.ToString();
            using (SHA1 sha = new SHA1CryptoServiceProvider())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            }
        }

        public static void LoadAsync(string path, Action<BitmapSource, bool> completed)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                BitmapSource icon;
                bool usedFallback = false;
                try
                {
                    icon = GetOrExtractSync(path, out usedFallback);
                }
                catch (Exception ex)
                {
                    Logger.Error("提取图标失败：" + path, ex);
                    icon = GetDefaultIcon();
                    usedFallback = true;
                }

                if (completed == null) return;
                Application app = Application.Current;
                if (app == null)
                {
                    completed(icon, usedFallback);
                    return;
                }
                try
                {
                    app.Dispatcher.BeginInvoke(new Action(delegate { completed(icon, usedFallback); }));
                }
                catch (Exception ex)
                {
                    Logger.Warn("图标回调派发失败：" + ex.Message);
                }
            });
        }

        /// <summary>同步提取（自检与后台线程使用）。命中缓存直接读取 PNG。</summary>
        public static BitmapSource GetOrExtractSync(string path, out bool usedFallback)
        {
            usedFallback = false;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                usedFallback = true;
                return GetDefaultIcon();
            }

            string iconsDir = EnsureIconsDir();
            string cacheFile = Path.Combine(iconsDir, CacheKeyFor(path) + ".png");

            if (File.Exists(cacheFile))
            {
                BitmapSource cached = TryLoadPng(cacheFile);
                if (cached != null) return cached;
                try { File.Delete(cacheFile); } catch { }
            }

            Bitmap bitmap = ExtractBitmap(path);
            if (bitmap == null)
            {
                Logger.Warn("无法提取图标，使用通用图标：" + path);
                usedFallback = true;
                return GetDefaultIcon();
            }

            BitmapSource memorySource = null;
            try
            {
                string tempFile = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                bitmap.Save(tempFile, ImageFormat.Png);
                lock (CacheWriteLock)
                {
                    if (!File.Exists(cacheFile))
                    {
                        File.Move(tempFile, cacheFile);
                    }
                    else
                    {
                        try { File.Delete(tempFile); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("图标缓存写入失败：" + path, ex);
                memorySource = ToBitmapSource(bitmap);
            }

            try
            {
                if (memorySource != null) return memorySource;
                BitmapSource saved = TryLoadPng(cacheFile);
                if (saved != null) return saved;
                usedFallback = true;
                Logger.Warn("图标缓存读取失败，使用通用图标：" + path);
                return GetDefaultIcon();
            }
            finally
            {
                bitmap.Dispose();
            }
        }

        public static BitmapSource GetDefaultIcon()
        {
            lock (DefaultLock)
            {
                if (_defaultIcon != null) return _defaultIcon;
                Bitmap bitmap = SystemIcons.Application.ToBitmap();
                try
                {
                    _defaultIcon = ToBitmapSource(bitmap);
                }
                finally
                {
                    bitmap.Dispose();
                }
                return _defaultIcon;
            }
        }

        private static string EnsureIconsDir()
        {
            if (string.IsNullOrEmpty(_iconsDir))
            {
                lock (InitLock)
                {
                    if (string.IsNullOrEmpty(_iconsDir))
                    {
                        _iconsDir = Path.Combine(Path.GetTempPath(), "EveryGo-icons");
                    }
                }
            }
            Directory.CreateDirectory(_iconsDir);
            return _iconsDir;
        }

        private static BitmapSource TryLoadPng(string file)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(bytes);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                Logger.Warn("图标缓存损坏：" + file + " - " + ex.Message);
                return null;
            }
        }

        private static BitmapSource ToBitmapSource(Bitmap bitmap)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = bitmap.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
                BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                    handle,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                if (handle != IntPtr.Zero) DeleteObject(handle);
            }
        }

        private static Bitmap ExtractBitmap(string path)
        {
            string sourcePath = path;
            string extension = Path.GetExtension(path);
            if (string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase))
            {
                sourcePath = FindCmdExe();
            }

            Bitmap shellIcon = TryShellIcon(sourcePath, 256);
            if (shellIcon != null) return shellIcon;

            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(sourcePath))
                {
                    if (icon != null) return icon.ToBitmap();
                }
            }
            catch
            {
            }
            return null;
        }

        private static string FindCmdExe()
        {
            string comspec = Environment.GetEnvironmentVariable("ComSpec");
            if (!string.IsNullOrEmpty(comspec) && File.Exists(comspec)) return comspec;
            string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string cmd = Path.Combine(systemDir, "cmd.exe");
            return File.Exists(cmd) ? cmd : "cmd.exe";
        }

        private static Bitmap TryShellIcon(string path, int size)
        {
            IShellItemImageFactory factory = null;
            IntPtr bitmapHandle = IntPtr.Zero;
            try
            {
                Guid iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, iid, out factory);
                int result = factory.GetImage(
                    new ShellSize(size, size),
                    SIIGBF.ICONONLY | SIIGBF.BIGGERSIZEOK,
                    out bitmapHandle);
                if (result != 0 || bitmapHandle == IntPtr.Zero) return null;
                return System.Drawing.Image.FromHbitmap(bitmapHandle);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (bitmapHandle != IntPtr.Zero) DeleteObject(bitmapHandle);
                if (factory != null)
                {
                    try { Marshal.ReleaseComObject(factory); } catch { }
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ShellSize
        {
            public int cx;
            public int cy;

            public ShellSize(int width, int height)
            {
                cx = width;
                cy = height;
            }
        }

        [Flags]
        private enum SIIGBF
        {
            RESIZETOFIT = 0x00,
            BIGGERSIZEOK = 0x01,
            ICONONLY = 0x04
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(ShellSize size, SIIGBF flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
