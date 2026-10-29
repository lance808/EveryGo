using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EveryGo
{
    /// <summary>
    /// 应用图标提取与缓存：优先 shell 大图标（256），回退 32px，最终使用系统通用图标。
    /// 关键点：HBITMAP 通过 GetDIBits 转为 Bgra32，保留透明通道，避免 PNG 出现黑底/黑边。
    /// </summary>
    internal static class IconCache
    {
        private static string _iconsDir;
        private static BitmapSource _defaultIcon;
        private static readonly object InitLock = new object();
        private static readonly object DefaultLock = new object();
        private static readonly object CacheWriteLock = new object();

        private const string KeySalt = "EveryGo.icon.v2";

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

            BitmapSource source = ExtractSource(path);
            if (source == null)
            {
                Logger.Warn("无法提取图标，使用通用图标：" + path);
                usedFallback = true;
                return GetDefaultIcon();
            }

            try
            {
                string tempFile = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                SavePng(source, tempFile);
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
                return source;
            }

            BitmapSource saved = TryLoadPng(cacheFile);
            return saved != null ? saved : source;
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
                if (_defaultIcon == null)
                {
                    _defaultIcon = CreateFallbackIcon();
                }
                return _defaultIcon;
            }
        }

        private static BitmapSource CreateFallbackIcon()
        {
            int size = 32;
            byte[] pixels = new byte[size * size * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 0x60;
                pixels[i + 1] = 0x64;
                pixels[i + 2] = 0x6C;
                pixels[i + 3] = 0xFF;
            }
            BitmapSource source = BitmapSource.Create(
                size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            source.Freeze();
            return source;
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

        private static void SavePng(BitmapSource source, string file)
        {
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (FileStream stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
            }
        }

        private static BitmapSource ExtractSource(string path)
        {
            string sourcePath = path;
            string extension = Path.GetExtension(path);
            if (string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase))
            {
                sourcePath = FindCmdExe();
            }

            BitmapSource shellIcon = TryShellIcon(sourcePath, 256);
            if (shellIcon != null) return shellIcon;

            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(sourcePath))
                {
                    if (icon != null)
                    {
                        using (Bitmap bitmap = icon.ToBitmap())
                        {
                            BitmapSource source = ToBitmapSource(bitmap);
                            if (source != null) return source;
                        }
                    }
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

        private static BitmapSource TryShellIcon(string path, int size)
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
                return HBitmapToSource(bitmapHandle);
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

        private static BitmapSource ToBitmapSource(Bitmap bitmap)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = bitmap.GetHbitmap(System.Drawing.Color.FromArgb(0, 0, 0, 0));
                return HBitmapToSource(handle);
            }
            finally
            {
                if (handle != IntPtr.Zero) DeleteObject(handle);
            }
        }

        /// <summary>GetDIBits 读取 BGRA 像素后按 Bgra32 创建，保留 Alpha。</summary>
        private static BitmapSource HBitmapToSource(IntPtr hBitmap)
        {
            if (hBitmap == IntPtr.Zero) return null;

            BITMAP bitmapInfo;
            if (GetObject(hBitmap, Marshal.SizeOf(typeof(BITMAP)), out bitmapInfo) == 0)
            {
                return null;
            }
            int width = Math.Abs(bitmapInfo.bmWidth);
            int height = Math.Abs(bitmapInfo.bmHeight);
            if (width <= 0 || height <= 0) return null;

            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return null;
            try
            {
                BITMAPINFO dibInfo = new BITMAPINFO();
                dibInfo.bmiHeader.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                dibInfo.bmiHeader.biWidth = width;
                dibInfo.bmiHeader.biHeight = -height; // 自顶向下
                dibInfo.bmiHeader.biPlanes = 1;
                dibInfo.bmiHeader.biBitCount = 32;
                dibInfo.bmiHeader.biCompression = 0; // BI_RGB

                byte[] pixels = new byte[width * height * 4];
                int lines = GetDIBits(hdc, hBitmap, 0, (uint)height, pixels, ref dibInfo, 0);
                if (lines != height) return null;

                bool hasAlpha = false;
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    if (pixels[i] != 0)
                    {
                        hasAlpha = true;
                        break;
                    }
                }
                if (!hasAlpha)
                {
                    for (int i = 3; i < pixels.Length; i += 4)
                    {
                        pixels[i] = 0xFF;
                    }
                }

                BitmapSource source = BitmapSource.Create(
                    width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
                source.Freeze();
                return source;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
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

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
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
        private static extern int GetObject(IntPtr hObject, int cbSize, out BITMAP lpObject);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(
            IntPtr hdc,
            IntPtr hbm,
            uint start,
            uint cLines,
            byte[] lpvBits,
            ref BITMAPINFO lpbmi,
            uint usage);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    }
}
