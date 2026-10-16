using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OPNX.Lib.Common.Platform.Windows
{
    public static partial class Win32
    {
        #region Constants
        private const uint FlashTaskbarFlag = 2;
        private const uint FlashUntilForegroundFlag = 12;
        private const ushort VariantUnicodeString = 31;
        private const string PropertyStoreInterfaceID = "886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99";
        private static readonly PropertyKey AppUserModelIDPropertyKey = new()
        {
            FormatID = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            PropertyID = 5
        };
        #endregion

        #region Public Methods
        [SupportedOSPlatform("windows")]
        public static void SetWindowApplicationID(IntPtr handle, string? applicationID)
        {
            if (handle == IntPtr.Zero)
                throw new ArgumentException("A valid window handle is required.", nameof(handle));

            var interfaceID = typeof(IPropertyStore).GUID;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(handle, ref interfaceID, out var store));
            var key = AppUserModelIDPropertyKey;
            var value = new PropertyValue();
            try
            {
                if (applicationID is not null)
                {
                    value.Type = VariantUnicodeString;
                    value.Pointer = Marshal.StringToCoTaskMemUni(applicationID);
                }
                Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
            }
            finally
            {
                Marshal.FreeCoTaskMem(value.Pointer);
                Marshal.ReleaseComObject(store);
            }
        }

        [SupportedOSPlatform("windows")]
        public static void FlashWindow(IntPtr handle, bool enabled)
        {
            if (handle == IntPtr.Zero)
                return;

            var info = new FlashInfo
            {
                Size = (uint)Marshal.SizeOf<FlashInfo>(),
                Handle = handle,
                Flags = enabled ? FlashTaskbarFlag | FlashUntilForegroundFlag : 0u,
                Count = enabled ? uint.MaxValue : 0
            };
            FlashWindowEx(ref info);
        }
        #endregion

        #region Native Types
        [StructLayout(LayoutKind.Sequential)]
        private struct FlashInfo
        {
            public uint Size;
            public IntPtr Handle;
            public uint Flags;
            public uint Count;
            public uint Timeout;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid FormatID;
            public uint PropertyID;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyValue
        {
            public ushort Type;
            public ushort Reserved1;
            public ushort Reserved2;
            public ushort Reserved3;
            public IntPtr Pointer;
            public IntPtr Extra;
        }

        [ComImport, Guid(PropertyStoreInterfaceID), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropertyValue value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropertyValue value);
            [PreserveSig] int Commit();
        }
        #endregion

        #region Native Methods
        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern int SHGetPropertyStoreForWindow(IntPtr handle, ref Guid interfaceID,
            [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        [LibraryImport("user32.dll", EntryPoint = "FlashWindowEx")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool FlashWindowEx(ref FlashInfo info);
        #endregion
    }
}
