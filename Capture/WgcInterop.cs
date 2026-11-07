using System;
using System.Runtime.InteropServices;

namespace WindowTV.Capture
{
    /// <summary>
    /// 纯托管 WinRT / COM 调用层。
    /// 不依赖 CsWinRT、不依赖任何第三方程序集，Unity 的 Mono 可以直接跑。
    /// 槽位布局：IUnknown 占 0-2，IInspectable 占 3-5，WinRT 自定义方法从 6 开始。
    /// </summary>
    internal static class WgcNative
    {
        [DllImport("combase.dll", CharSet = CharSet.Unicode)]
        public static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

        [DllImport("combase.dll")]
        public static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll")]
        public static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

        public static IntPtr Activate(string className, Guid iid)
        {
            int hr = WindowsCreateString(className, className.Length, out IntPtr hstr);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }
            try
            {
                hr = RoGetActivationFactory(hstr, ref iid, out IntPtr factory);
                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }
                return factory;
            }
            finally
            {
                WindowsDeleteString(hstr);
            }
        }

        public static void Check(int hr, string what)
        {
            if (hr < 0)
            {
                throw new InvalidOperationException(what + " 失败，HRESULT=0x" + hr.ToString("X8"));
            }
        }
    }

    internal static class WgcVtbl
    {
        public static IntPtr Entry(IntPtr obj, int slot)
        {
            IntPtr vtbl = Marshal.ReadIntPtr(obj);
            return Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size);
        }

        public static T Fn<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr entry = Entry(obj, slot);
            if (entry == IntPtr.Zero)
            {
                throw new InvalidOperationException("槽位 " + slot + " 为空");
            }
            return (T)(object)Marshal.GetDelegateForFunctionPointer(entry, typeof(T));
        }
    }

    internal static class WgcGuids
    {
        public static readonly Guid CaptureItemInterop = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
        public static readonly Guid CaptureItem = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        public static readonly Guid FramePoolStatics2 = new Guid("589B103F-6BBC-5DF5-A991-02E28B3B66D5");
        public static readonly Guid DxgiDevice = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
        public static readonly Guid DxgiInterfaceAccess = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
        public static readonly Guid D3D11Texture2D = new Guid("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
        public static readonly Guid CaptureSession2 = new Guid("2C39AE40-7D2E-5044-804E-8B6799D4CF9E");
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SizeInt32
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SampleDesc
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Texture2DDesc
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public int Format;
        public SampleDesc SampleDesc;
        public int Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MappedSubresource
    {
        public IntPtr Data;
        public uint RowPitch;
        public uint DepthPitch;
    }

    // ---- WinRT：方法从槽位 6 开始（顺序以系统 winmd 为准，不能用投影接口的书写顺序）----

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int CreateForWindowFn(IntPtr self, IntPtr hwnd, ref Guid iid, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int FramePoolCreateFn(IntPtr self, IntPtr device, int pixelFormat, int bufferCount, SizeInt32 size, out IntPtr result);

    /// <summary>IDirect3D11CaptureFramePool：Recreate=0, TryGetNextFrame=1, add/remove=2/3, CreateCaptureSession=4</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int TryGetNextFrameFn(IntPtr self, out IntPtr frame);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int CreateSessionFn(IntPtr self, IntPtr item, IntPtr sessionPtr);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int StartCaptureFn(IntPtr self);

    /// <summary>IGraphicsCaptureSession2：IsCursorCaptureEnabled 的 get/put（槽位 6/7）。WinRT 的 boolean 是单字节。</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int PutBoolFn(IntPtr self, byte value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GetItemSizeFn(IntPtr self, out SizeInt32 size);

    /// <summary>IDirect3D11CaptureFrame：Surface=0（槽位 6）</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GetSurfaceFn(IntPtr self, out IntPtr surface);

    // ---- classic COM ----

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GetInterfaceFn(IntPtr self, ref Guid iid, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int CreateTexture2DFn(IntPtr self, ref Texture2DDesc desc, IntPtr initialData, out IntPtr texture);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void CopyResourceFn(IntPtr self, IntPtr dst, IntPtr src);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int MapFn(IntPtr self, IntPtr resource, uint subresource, int mapType, int mapFlags, out MappedSubresource mapped);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void UnmapFn(IntPtr self, IntPtr resource, uint subresource);
}
