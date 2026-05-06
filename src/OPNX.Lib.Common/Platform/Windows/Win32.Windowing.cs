using System.Runtime.InteropServices;

namespace OPNX.Lib.Common.Platform.Windows
{
    public static partial class Win32
    {
        public const int WM_CLOSE = 0x0010;
        public const int WM_COPYDATA = 0x004A;
        public const int WM_NCPAINT = 0x0085;
        public const int WM_GETMINMAXINFO = 0x0024;
        public const int WM_USER = 0x0400;

        public enum GwlIndex : int
        {
            Style = -16
        }

        [Flags]
        public enum WindowStyles : int
        {
            MaximizeBox = 0x00010000
        }

        public enum SystemMetric : int
        {
            RemoteSession = 0x1000
        }

        public enum MonitorFromWindowFlags : uint
        {
            DefaultToNearest = 0x00000002
        }

        [Flags]
        public enum SetWindowPosFlags : uint
        {
            NoSize = 0x0001,
            NoMove = 0x0002,
            NoZOrder = 0x0004,
            NoRedraw = 0x0008,
            NoActivate = 0x0010,
            FrameChanged = 0x0020,
            ShowWindow = 0x0040,
            HideWindow = 0x0080,
            NoCopyBits = 0x0100,
            NoOwnerZOrder = 0x0200,
            NoSendChanging = 0x0400,
            DeferErase = 0x2000,
            AsyncWindowPos = 0x4000
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MinMaxInfo
        {
            public Point PtReserved;
            public Point PtMaxSize;
            public Point PtMaxPosition;
            public Point PtMinTrackSize;
            public Point PtMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MonitorInfo
        {
            public int CbSize;
            public Rect RcMonitor;
            public Rect RcWork;
            public int DwFlags;

            public static MonitorInfo Create()
                => new() { CbSize = Marshal.SizeOf<MonitorInfo>() };
        }

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, GwlIndex index)
            => IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, (int)index)
                : new IntPtr(GetWindowLong32(hWnd, (int)index));

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, GwlIndex index, IntPtr newValue)
            => IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, (int)index, newValue)
                : new IntPtr(SetWindowLong32(hWnd, (int)index, newValue.ToInt32()));

        public static IntPtr MonitorFromWindow(IntPtr hWnd, MonitorFromWindowFlags flags)
            => MonitorFromWindowNative(hWnd, (uint)flags);

        public static bool TryGetMonitorInfo(IntPtr hMonitor, out MonitorInfo info)
        {
            info = MonitorInfo.Create();
            return GetMonitorInfoNative(hMonitor, ref info);
        }

        public static int GetSystemMetrics(SystemMetric metric)
            => GetSystemMetricsNative((int)metric);

        public static bool TryGetWindowRect(IntPtr hWnd, out Rect rect)
        {
            rect = default;
            return GetWindowRectNative(hWnd, ref rect);
        }

        public static bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            SetWindowPosFlags flags)
            => SetWindowPosNative(hWnd, hWndInsertAfter, x, y, cx, cy, (uint)flags);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static partial int GetWindowLong32(IntPtr hWnd, int nIndex);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static partial int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static partial IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static partial IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [LibraryImport("user32.dll", EntryPoint = "MonitorFromWindow")]
        private static partial IntPtr MonitorFromWindowNative(IntPtr hwnd, uint dwFlags);

        [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfo")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetMonitorInfoNative(IntPtr hMonitor, ref MonitorInfo lpmi);

        [LibraryImport("user32.dll", EntryPoint = "GetSystemMetrics")]
        private static partial int GetSystemMetricsNative(int nIndex);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetWindowRectNative(IntPtr hWnd, ref Rect rect);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowPos")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowPosNative(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);
    }
}