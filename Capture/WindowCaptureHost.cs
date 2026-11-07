using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace WindowTV.Capture
{
    /// <summary>
    /// 后台捕获线程 + 线程安全的最新帧交换。
    /// 所有 COM / D3D11 调用都发生在后台线程，Unity 主线程只负责上传纹理。
    /// </summary>
    internal sealed class WindowCaptureHost : IDisposable
    {
        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        private const uint CoInitMultithreaded = 0;

        private Thread thread;
        private volatile bool running;
        private volatile WgcCapture capture;
        private IntPtr target;

        private readonly object gate = new object();
        private byte[] latest;
        private int latestWidth;
        private int latestHeight;
        private int latestSeq;
        private int consumedSeq;

        private volatile string status = "未选择窗口";
        private volatile string error;

        public string Status { get { return status; } }
        public string Error { get { return error; } }

        public int Width
        {
            get { lock (gate) { return latestWidth; } }
        }

        public int Height
        {
            get { lock (gate) { return latestHeight; } }
        }

        public bool IsRunning { get { return running; } }

        // 诊断用：把捕获线程内部的计数暴露出来，好判断到底卡在"没拿到帧"还是"拿了没送上来"
        public int ArrivedFrames
        {
            get
            {
                WgcCapture c = capture;
                return c != null ? c.ArrivedCount : -1;
            }
        }

        public int PublishedFrames
        {
            get
            {
                WgcCapture c = capture;
                return c != null ? c.PublishedCount : -1;
            }
        }

        public int Sequence
        {
            get { lock (gate) { return latestSeq; } }
        }

        public void Start(IntPtr hwnd)
        {
            Stop();
            if (hwnd == IntPtr.Zero)
            {
                status = "未选择窗口";
                return;
            }

            target = hwnd;
            error = null;
            status = "连接中…";
            lock (gate)
            {
                latest = null;
                latestWidth = 0;
                latestHeight = 0;
                latestSeq = 0;
                consumedSeq = 0;
            }

            running = true;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "WindowTV.Capture";
            thread.Start();
        }

        private void Loop()
        {
            // Unity 的后台线程默认没有初始化 COM，不初始化的话
            // RoGetActivationFactory 会直接返回 CO_E_NOTINITIALIZED。
            bool comReady = false;
            try
            {
                int hr = CoInitializeEx(IntPtr.Zero, CoInitMultithreaded);
                // S_OK / S_FALSE 都能用；RPC_E_CHANGED_MODE 表示线程已是别的套间模型，
                // 这种情况下沿用现有套间，不能再配对 CoUninitialize。
                comReady = hr >= 0;
                if (!comReady)
                {
                    try { ModEntry.Log.Log((object)("[WindowTV] CoInitializeEx 返回 0x" + hr.ToString("X8"))); } catch (Exception) { }
                }
            }
            catch (Exception ex)
            {
                try { ModEntry.Log.Log((object)("[WindowTV] CoInitializeEx 异常：" + ex.Message)); } catch (Exception) { }
            }

            WgcCapture local = null;
            try
            {
                while (running)
                {
                    local = new WgcCapture();
                    capture = local;
                    if (!local.Start(target))
                    {
                        error = local.LastError;
                        status = "捕获失败：" + (local.LastError ?? "未知原因");
                        return;
                    }

                    status = "投屏中 " + local.Width + "x" + local.Height;
                    ModEntry.Log.Log((object)("[WindowTV] 捕获已启动 " + local.Width + "x" + local.Height));

                    while (running && !local.SizeChanged)
                    {
                        bool got;
                        try
                        {
                            got = local.TryGrab();
                        }
                        catch (Exception ex)
                        {
                            error = ex.Message;
                            status = "捕获异常：" + ex.Message;
                            break;
                        }

                        if (!got)
                        {
                            Thread.Sleep(1);
                            continue;
                        }

                        byte[] src = local.Buffer;
                        if (src == null)
                        {
                            continue;
                        }
                        lock (gate)
                        {
                            if (latest == null || latest.Length != src.Length)
                            {
                                latest = new byte[src.Length];
                            }
                            System.Buffer.BlockCopy(src, 0, latest, 0, src.Length);
                            latestWidth = local.Width;
                            latestHeight = local.Height;
                            latestSeq++;
                        }
                    }

                    if (error != null)
                    {
                        break;
                    }
                    if (!running)
                    {
                        break;
                    }
                    if (local.SizeChanged)
                    {
                        // 窗口尺寸变了：整个会话按新尺寸重建，画面才能完整
                        try { local.Dispose(); } catch (Exception) { }
                        local = null;
                        capture = null;
                        status = "窗口尺寸变化，重建中…";
                        ModEntry.Log.Log((object)"[WindowTV] 窗口尺寸变化，重建捕获");
                        continue;
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                status = "线程异常：" + ex.Message;
                try { ModEntry.Log.Log((object)("[WindowTV] 捕获线程异常：" + ex)); } catch (Exception) { }
            }
            finally
            {
                capture = null;
                if (local != null)
                {
                    try { local.Dispose(); } catch (Exception) { }
                }
                if (running)
                {
                    status = "已停止";
                }
                running = false;
                if (comReady)
                {
                    try { CoUninitialize(); } catch (Exception) { }
                }
            }
        }

        /// <summary>
        /// 把最新一帧拷进调用方缓冲。返回 false 表示没有新帧（或缓冲太小）。
        /// dest 为 null 时仍会给出 width/height，方便调用方先分配。
        /// </summary>
        public bool TryCopyInto(byte[] dest, out int width, out int height)
        {
            lock (gate)
            {
                width = latestWidth;
                height = latestHeight;
                if (latest == null || latestSeq == consumedSeq)
                {
                    return false;
                }
                if (dest == null || dest.Length < latest.Length)
                {
                    return false;
                }
                consumedSeq = latestSeq;
                System.Buffer.BlockCopy(latest, 0, dest, 0, latest.Length);
                return true;
            }
        }

        public void Stop()
        {
            running = false;
            Thread t = thread;
            thread = null;
            if (t != null)
            {
                try
                {
                    if (!t.Join(1500))
                    {
                        try { ModEntry.Log.Log((object)"[WindowTV] 捕获线程未在 1.5 秒内退出"); } catch (Exception) { }
                    }
                }
                catch (Exception)
                {
                }
            }
            capture = null;
        }

        public void Dispose()
        {
            Stop();
            lock (gate)
            {
                latest = null;
                latestWidth = 0;
                latestHeight = 0;
            }
        }
    }
}
