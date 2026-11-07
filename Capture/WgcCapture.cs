using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowTV.Capture
{
    /// <summary>
    /// 进程内窗口捕获：Windows.Graphics.Capture + D3D11 staging 直读。
    /// 必须在同一个后台线程里创建、取帧和销毁，D3D11 即时上下文不是线程安全的。
    /// </summary>
    internal sealed class WgcCapture : IDisposable
    {
        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(
            IntPtr adapter,
            int driverType,
            IntPtr software,
            uint flags,
            [In] int[] featureLevels,
            uint featureLevelCount,
            uint sdkVersion,
            out IntPtr device,
            out int featureLevel,
            out IntPtr context);

        [DllImport("d3d11.dll")]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        private const int DriverTypeHardware = 1;
        private const int DriverTypeWarp = 5;
        private const uint CreateDeviceBgraSupport = 0x20;
        private const uint SdkVersion = 7;
        private const int FormatB8G8R8A8 = 87;
        private const int UsageStaging = 3;
        private const uint CpuAccessRead = 0x20000;
        private const int MapRead = 1;

        // 帧池必须和捕获项一样大，否则系统会按帧池尺寸裁剪（只保留左上角一块），
        // 看上去就是「右边和下边被切掉了」。所以这里不做任何缩放下限。
        private const int SizeCheckInterval = 20;

        private static readonly int[] Levels11 = { 0xb100, 0xb000, 0xa100, 0xa000 };
        private static readonly int[] Levels10 = { 0xb000, 0xa100, 0xa000 };

        private IntPtr d3dDevice;
        private IntPtr d3dContext;
        private IntPtr winrtDevice;
        private IntPtr item;
        private IntPtr framePool;
        private IntPtr session;
        private IntPtr staging;
        private int stagingWidth;
        private int stagingHeight;

        private byte[] buffer;
        private int width;
        private int height;

        private GetItemSizeFn getItemSize;
        private int framesSinceSizeCheck;

        /// <summary>目标窗口尺寸变了，宿主需要重建捕获会话。</summary>
        public bool SizeChanged;

        private TryGetNextFrameFn tryGetFrame;
        private GetSurfaceFn getSurface;
        private GetInterfaceFn dxgiAccess;
        private CreateTexture2DFn createTexture;
        private CopyResourceFn copyResource;
        private MapFn mapFn;
        private UnmapFn unmapFn;

        public int Width { get { return width; } }
        public int Height { get { return height; } }
        public byte[] Buffer { get { return buffer; } }

        public int ArrivedCount;
        public int PublishedCount;
        public long TotalTicks;
        public int TimedCount;
        public string LastError;

        public double AverageMs
        {
            get
            {
                int n = TimedCount;
                if (n <= 0)
                {
                    return 0;
                }
                return TotalTicks * 1000.0 / n / Stopwatch.Frequency;
            }
        }

        public bool Start(IntPtr hwnd)
        {
            try
            {
                if (!CreateDevice())
                {
                    return false;
                }
                ModEntry.Log.Log((object)"[WindowTV] 捕获: D3D11 设备就绪");

                IntPtr interop = WgcNative.Activate(
                    "Windows.Graphics.Capture.GraphicsCaptureItem",
                    WgcGuids.CaptureItemInterop);
                try
                {
                    CreateForWindowFn createForWindow = WgcVtbl.Fn<CreateForWindowFn>(interop, 3);
                    Guid iidItem = WgcGuids.CaptureItem;
                    WgcNative.Check(createForWindow(interop, hwnd, ref iidItem, out item), "CreateForWindow");
                }
                finally
                {
                    Marshal.Release(interop);
                }

                ModEntry.Log.Log((object)"[WindowTV] 捕获: 捕获项已创建");
                getItemSize = WgcVtbl.Fn<GetItemSizeFn>(item, 7);
                SizeInt32 size;
                WgcNative.Check(getItemSize(item, out size), "get_Size");
                if (size.Width <= 0 || size.Height <= 0)
                {
                    LastError = "目标窗口尺寸无效：" + size.Width + "x" + size.Height;
                    return false;
                }

                // 原尺寸取帧，绝不缩放/裁剪：帧池尺寸和窗口不一致时系统只保留左上角一块
                width = Math.Max(2, size.Width & ~1);
                height = Math.Max(2, size.Height & ~1);
                size.Width = width;
                size.Height = height;
                buffer = new byte[width * height * 4];

                if ((long)width * height > 1920L * 1080L)
                {
                    ModEntry.Log.Log((object)("[WindowTV] 提示：目标窗口较大（" + width + "x" + height + "），每帧拷贝开销会偏高"));
                }

                IntPtr poolStatics = WgcNative.Activate(
                    "Windows.Graphics.Capture.Direct3D11CaptureFramePool",
                    WgcGuids.FramePoolStatics2);
                try
                {
                    FramePoolCreateFn createPool = WgcVtbl.Fn<FramePoolCreateFn>(poolStatics, 6);
                    WgcNative.Check(
                        createPool(poolStatics, winrtDevice, FormatB8G8R8A8, 3, size, out framePool),
                        "FramePool.CreateFreeThreaded");
                }
                finally
                {
                    Marshal.Release(poolStatics);
                }

                ModEntry.Log.Log((object)"[WindowTV] 捕获: 帧池已创建");
                CreateSessionFn createSession = WgcVtbl.Fn<CreateSessionFn>(framePool, 10);
                IntPtr sessionPtr = Marshal.AllocHGlobal(IntPtr.Size);
                int sessionHr;
                try
                {
                    Marshal.WriteIntPtr(sessionPtr, IntPtr.Zero);
                    sessionHr = createSession(framePool, item, sessionPtr);
                    session = Marshal.ReadIntPtr(sessionPtr);
                }
                finally
                {
                    Marshal.FreeHGlobal(sessionPtr);
                }
                WgcNative.Check(sessionHr, "CreateCaptureSession");
                if (session == IntPtr.Zero)
                {
                    LastError = "CreateCaptureSession 返回空对象";
                    return false;
                }

                ModEntry.Log.Log((object)"[WindowTV] 捕获: 会话已创建");
                // WGC 默认把鼠标指针一起画进帧里。QI 到 IGraphicsCaptureSession2 关掉，
                // 老系统（Win10 1903 之前）没有这个接口，QI 失败就静默跳过。
                DisableCursor();
                StartCaptureFn startCapture = WgcVtbl.Fn<StartCaptureFn>(session, 6);
                WgcNative.Check(startCapture(session), "StartCapture");

                tryGetFrame = WgcVtbl.Fn<TryGetNextFrameFn>(framePool, 7);
                getSurface = null;
                dxgiAccess = null;
                createTexture = WgcVtbl.Fn<CreateTexture2DFn>(d3dDevice, 5);
                copyResource = WgcVtbl.Fn<CopyResourceFn>(d3dContext, 47);
                mapFn = WgcVtbl.Fn<MapFn>(d3dContext, 14);
                unmapFn = WgcVtbl.Fn<UnmapFn>(d3dContext, 15);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Dispose();
                return false;
            }
        }

        private void DisableCursor()
        {
            IntPtr session2 = IntPtr.Zero;
            try
            {
                Guid iid = WgcGuids.CaptureSession2;
                int hr = Marshal.QueryInterface(session, ref iid, out session2);
                if (hr < 0 || session2 == IntPtr.Zero)
                {
                    ModEntry.Log.Log((object)"[WindowTV] 捕获: 系统不支持隐藏鼠标（缺少 IGraphicsCaptureSession2）");
                    return;
                }
                PutBoolFn putCursor = WgcVtbl.Fn<PutBoolFn>(session2, 7);
                hr = putCursor(session2, 0);
                ModEntry.Log.Log((object)(hr >= 0
                    ? "[WindowTV] 捕获: 已关闭鼠标指针"
                    : "[WindowTV] 捕获: 关闭鼠标指针失败，HRESULT=0x" + hr.ToString("X8")));
            }
            catch (Exception ex)
            {
                ModEntry.Log.Log((object)("[WindowTV] 捕获: 关闭鼠标指针异常 " + ex.Message));
            }
            finally
            {
                if (session2 != IntPtr.Zero)
                {
                    try { Marshal.Release(session2); } catch (Exception) { }
                }
            }
        }

        private bool CreateDevice()
        {
            int hr = D3D11CreateDevice(
                IntPtr.Zero, DriverTypeHardware, IntPtr.Zero, CreateDeviceBgraSupport,
                Levels11, (uint)Levels11.Length, SdkVersion,
                out d3dDevice, out int level, out d3dContext);
            if (hr < 0)
            {
                hr = D3D11CreateDevice(
                    IntPtr.Zero, DriverTypeHardware, IntPtr.Zero, CreateDeviceBgraSupport,
                    Levels10, (uint)Levels10.Length, SdkVersion,
                    out d3dDevice, out level, out d3dContext);
            }
            if (hr < 0)
            {
                hr = D3D11CreateDevice(
                    IntPtr.Zero, DriverTypeWarp, IntPtr.Zero, CreateDeviceBgraSupport,
                    Levels10, (uint)Levels10.Length, SdkVersion,
                    out d3dDevice, out level, out d3dContext);
            }
            if (hr < 0)
            {
                LastError = "D3D11CreateDevice 失败，HRESULT=0x" + hr.ToString("X8");
                return false;
            }

            Guid iidDxgi = WgcGuids.DxgiDevice;
            hr = Marshal.QueryInterface(d3dDevice, ref iidDxgi, out IntPtr dxgiDevice);
            if (hr < 0)
            {
                LastError = "QueryInterface(IDXGIDevice) 失败，HRESULT=0x" + hr.ToString("X8");
                return false;
            }
            try
            {
                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out winrtDevice);
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
            if (hr < 0)
            {
                LastError = "CreateDirect3D11DeviceFromDXGIDevice 失败，HRESULT=0x" + hr.ToString("X8");
                return false;
            }
            return true;
        }

        private void EnsureStagingTexture()
        {
            if (staging != IntPtr.Zero && stagingWidth == width && stagingHeight == height)
            {
                return;
            }
            if (staging != IntPtr.Zero)
            {
                Marshal.Release(staging);
                staging = IntPtr.Zero;
            }

            Texture2DDesc desc = new Texture2DDesc
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = FormatB8G8R8A8,
                SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
                Usage = UsageStaging,
                BindFlags = 0,
                CpuAccessFlags = CpuAccessRead,
                MiscFlags = 0
            };

            WgcNative.Check(createTexture(d3dDevice, ref desc, IntPtr.Zero, out staging), "CreateTexture2D(staging)");
            stagingWidth = width;
            stagingHeight = height;
        }

        /// <summary>尝试取一帧。返回 true 表示 Buffer 已更新。</summary>
        public bool TryGrab()
        {
            long started = Stopwatch.GetTimestamp();
            IntPtr frame = IntPtr.Zero;
            IntPtr surface = IntPtr.Zero;
            IntPtr texture = IntPtr.Zero;

            // 窗口被拖动改变大小时，帧池尺寸就对不上了，画面会变成「只更新左上角」。
            // 每 20 帧查一次捕获项尺寸，一旦变了就让宿主重建整个会话。
            if (getItemSize != null && ++framesSinceSizeCheck >= SizeCheckInterval)
            {
                framesSinceSizeCheck = 0;
                SizeInt32 now;
                if (getItemSize(item, out now) >= 0 && now.Width > 0 && now.Height > 0
                    && (Math.Max(2, now.Width & ~1) != width || Math.Max(2, now.Height & ~1) != height))
                {
                    SizeChanged = true;
                    return false;
                }
            }

            try
            {
                int hr = tryGetFrame(framePool, out frame);
                if (hr < 0 || frame == IntPtr.Zero)
                {
                    return false;
                }
                ArrivedCount++;

                if (getSurface == null)
                {
                    getSurface = WgcVtbl.Fn<GetSurfaceFn>(frame, 6);
                }
                hr = getSurface(frame, out surface);
                if (hr < 0 || surface == IntPtr.Zero)
                {
                    return false;
                }

                // IDirect3DSurface 是 WinRT 接口，必须先把 surface 查成
                // IDirect3DDxgiInterfaceAccess，再调它的 GetInterface（经典 COM 槽位 3）。
                Guid iidAccess = WgcGuids.DxgiInterfaceAccess;
                hr = Marshal.QueryInterface(surface, ref iidAccess, out IntPtr accessPtr);
                if (hr < 0 || accessPtr == IntPtr.Zero)
                {
                    return false;
                }
                try
                {
                    if (dxgiAccess == null)
                    {
                        dxgiAccess = WgcVtbl.Fn<GetInterfaceFn>(accessPtr, 3);
                    }
                    Guid iidTexture = WgcGuids.D3D11Texture2D;
                    hr = dxgiAccess(accessPtr, ref iidTexture, out texture);
                }
                finally
                {
                    Marshal.Release(accessPtr);
                }
                if (hr < 0 || texture == IntPtr.Zero)
                {
                    return false;
                }

                EnsureStagingTexture();
                if (staging == IntPtr.Zero)
                {
                    return false;
                }

                copyResource(d3dContext, staging, texture);

                MappedSubresource mapped;
                hr = mapFn(d3dContext, staging, 0, MapRead, 0, out mapped);
                if (hr < 0 || mapped.Data == IntPtr.Zero)
                {
                    return false;
                }
                try
                {
                    CopyRows(mapped);
                }
                finally
                {
                    unmapFn(d3dContext, staging, 0);
                }

                PublishedCount++;
                return true;
            }
            finally
            {
                if (texture != IntPtr.Zero) { try { Marshal.Release(texture); } catch (Exception) { } }
                if (surface != IntPtr.Zero) { try { Marshal.Release(surface); } catch (Exception) { } }
                if (frame != IntPtr.Zero) { try { Marshal.Release(frame); } catch (Exception) { } }
                long elapsed = Stopwatch.GetTimestamp() - started;
                TotalTicks += elapsed;
                TimedCount++;
            }
        }

        private void CopyRows(MappedSubresource mapped)
        {
            int rowBytes = width * 4;
            if (mapped.RowPitch == (uint)rowBytes)
            {
                Marshal.Copy(mapped.Data, buffer, 0, rowBytes * height);
                return;
            }
            for (int y = 0; y < height; y++)
            {
                IntPtr src = new IntPtr(mapped.Data.ToInt64() + (long)y * mapped.RowPitch);
                Marshal.Copy(src, buffer, y * rowBytes, rowBytes);
            }
        }

        public void Dispose()
        {
            try { if (staging != IntPtr.Zero) { Marshal.Release(staging); } } catch (Exception) { }
            staging = IntPtr.Zero;
            try { if (session != IntPtr.Zero) { Marshal.Release(session); } } catch (Exception) { }
            session = IntPtr.Zero;
            try { if (framePool != IntPtr.Zero) { Marshal.Release(framePool); } } catch (Exception) { }
            framePool = IntPtr.Zero;
            try { if (item != IntPtr.Zero) { Marshal.Release(item); } } catch (Exception) { }
            item = IntPtr.Zero;
            try { if (winrtDevice != IntPtr.Zero) { Marshal.Release(winrtDevice); } } catch (Exception) { }
            winrtDevice = IntPtr.Zero;
            try { if (d3dContext != IntPtr.Zero) { Marshal.Release(d3dContext); } } catch (Exception) { }
            d3dContext = IntPtr.Zero;
            try { if (d3dDevice != IntPtr.Zero) { Marshal.Release(d3dDevice); } } catch (Exception) { }
            d3dDevice = IntPtr.Zero;
        }
    }
}
