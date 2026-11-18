// Vibrance numérique NVIDIA (comme le panneau NVIDIA), pour un seul écran.
using System;
using System.Runtime.InteropServices;

namespace StretchSwitch
{
    public static class NvVibrance
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr QueryInterface(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int HandleFn([MarshalAs(UnmanagedType.LPStr)] string displayName, out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetFn(IntPtr handle, uint outputId, ref DVC_INFO info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetFn(IntPtr handle, uint outputId, int level);

        [StructLayout(LayoutKind.Sequential)]
        struct DVC_INFO { public uint version; public int current, min, max; }

        static bool? ready;
        static HandleFn getHandle;
        static GetFn getDvc;
        static SetFn setDvc;

        static T Fn<T>(uint id) where T : class
        {
            IntPtr p = QueryInterface(id);
            return p == IntPtr.Zero ? null : (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }

        public static bool Available
        {
            get
            {
                if (ready.HasValue) return ready.Value;
                ready = false;
                try
                {
                    if (IntPtr.Size != 8) return false;
                    var init = Fn<InitFn>(0x0150E828);
                    getHandle = Fn<HandleFn>(0x35C29134);  // NvAPI_GetAssociatedNvidiaDisplayHandle
                    getDvc = Fn<GetFn>(0x4085DE45);        // NvAPI_GetDVCInfo
                    setDvc = Fn<SetFn>(0x172409B4);        // NvAPI_SetDVCLevel
                    if (init == null || getHandle == null || getDvc == null || setDvc == null) return false;
                    if (init() != 0) return false;
                    int c, mn, mx;
                    ready = Get(Display.PrimaryDevice(), out c, out mn, out mx);
                }
                catch { ready = false; }
                return ready.Value;
            }
        }

        static IntPtr Handle(string gdi)
        {
            IntPtr h;
            return getHandle(gdi, out h) == 0 ? h : IntPtr.Zero;
        }

        public static bool Get(string gdi, out int current, out int min, out int max)
        {
            current = min = max = 0;
            try
            {
                IntPtr h = Handle(gdi);
                if (h == IntPtr.Zero) return false;
                var info = new DVC_INFO { version = (uint)Marshal.SizeOf(typeof(DVC_INFO)) | (1u << 16) };
                if (getDvc(h, 0, ref info) != 0) return false;
                current = info.current; min = info.min; max = info.max;
                return max > min;
            }
            catch { return false; }
        }

        public static bool Set(string gdi, int level)
        {
            try
            {
                IntPtr h = Handle(gdi);
                return h != IntPtr.Zero && setDvc(h, 0, level) == 0;
            }
            catch { return false; }
        }
    }
}
