using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowTV.Capture
{
    internal sealed class WindowInfo
    {
        public IntPtr Handle;
        public string Title;
        public string ProcessName;

        /// <summary>窗口自己声明了不许被捕获（远程串流、防录屏的播放器都这么干）。</summary>
        public bool CaptureBlocked;

        public string ShortTitle
        {
            get
            {
                string t = Title ?? "";
                if (t.Length > 12)
                {
                    t = t.Substring(0, 12) + "…";
                }
                return t;
            }
        }
    }

    /// <summary>
    /// 枚举可以投屏的顶层窗口。
    /// 注意：这里刻意不用 System.Diagnostics.Process —— 官方 ModScanner 把
    /// Process / ProcessStartInfo 归为 process-start 硬红线，一旦出现就直接拒绝加载整个 mod。
    /// 需要进程信息时改走 kernel32 的 OpenProcess + QueryFullProcessImageName。
    /// </summary>
    internal static class WindowFinder
    {
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hwnd, out WinRect rect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder exeName, ref int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct WinRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // GetWindowDisplayAffinity 的取值
        private const uint WDA_NONE = 0x0;
        private const uint WDA_MONITOR = 0x1;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

        /// <summary>
        /// 窗口是否自己声明了"不许抓我"。
        /// WDA_EXCLUDEFROMCAPTURE(0x11) 是 Win10 2004 起提供的防录屏标记，远程串流客户端、
        /// 部分播放器和受保护的阅读器都会设置它。这类窗口 WGC 能正常建会话、也能读到尺寸，
        /// 但帧池一个帧都不会送过来 —— 看起来就像"信号中断"，其实是根本不让抓。
        /// </summary>
        public static bool IsCaptureBlocked(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }
            uint affinity;
            if (!GetWindowDisplayAffinity(hwnd, out affinity))
            {
                // 老系统或特殊窗口取不到，按"没被禁止"处理，交给捕获流程自己报错
                return false;
            }
            return affinity == WDA_EXCLUDEFROMCAPTURE || affinity == WDA_MONITOR;
        }

        /// <summary>列出可选窗口。会排除游戏自身、桌面外壳、最小化窗口和过小的窗口。</summary>
        public static List<WindowInfo> List()
        {
            List<WindowInfo> result = new List<WindowInfo>();
            IntPtr shell = GetShellWindow();
            uint selfId = GetCurrentProcessId();

            EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
            {
                try
                {
                    if (hwnd == shell || hwnd == IntPtr.Zero)
                    {
                        return true;
                    }
                    if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
                    {
                        return true;
                    }

                    int len = GetWindowTextLength(hwnd);
                    if (len <= 0)
                    {
                        return true;
                    }

                    WinRect rect;
                    if (!GetClientRect(hwnd, out rect))
                    {
                        return true;
                    }
                    if (rect.Right - rect.Left < 64 || rect.Bottom - rect.Top < 64)
                    {
                        return true;
                    }

                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    if (pid == selfId)
                    {
                        return true;
                    }

                    StringBuilder sb = new StringBuilder(len + 1);
                    GetWindowText(hwnd, sb, sb.Capacity);
                    string title = sb.ToString();
                    if (string.IsNullOrEmpty(title))
                    {
                        return true;
                    }

                    WindowInfo info = new WindowInfo();
                    info.Handle = hwnd;
                    info.Title = title;
                    info.ProcessName = GetProcessName(pid);
                    info.CaptureBlocked = IsCaptureBlocked(hwnd);
                    result.Add(info);
                }
                catch (Exception)
                {
                }
                return true;
            }, IntPtr.Zero);

            return result;
        }

        /// <summary>按标题精确匹配，找不到返回 IntPtr.Zero。</summary>
        public static IntPtr FindByTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return IntPtr.Zero;
            }
            List<WindowInfo> list = List();
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Title, title, StringComparison.Ordinal))
                {
                    return list[i].Handle;
                }
            }
            // 标题可能被改过（比如浏览器标签切换），退化为包含匹配
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Title.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return list[i].Handle;
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>用 OpenProcess + QueryFullProcessImageName 取 exe 名，失败返回空串。</summary>
        private static string GetProcessName(uint pid)
        {
            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
            {
                return "";
            }
            try
            {
                int size = 1024;
                StringBuilder buffer = new StringBuilder(size);
                if (!QueryFullProcessImageNameW(handle, 0, buffer, ref size))
                {
                    return "";
                }
                string path = buffer.ToString();
                int slash = path.LastIndexOf('\\');
                string name = slash >= 0 ? path.Substring(slash + 1) : path;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - 4);
                }
                return name;
            }
            catch (Exception)
            {
                return "";
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }
}
