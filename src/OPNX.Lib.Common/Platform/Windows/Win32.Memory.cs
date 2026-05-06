using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OPNX.Lib.Common.Platform.Windows
{
    public static partial class Win32
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe IntPtr MemCopy(IntPtr dest, IntPtr src, UIntPtr bytes)
        {
            return MemCpy(dest, src, bytes);
        }

        [LibraryImport("msvcrt.dll", EntryPoint = "memcpy")]
        private static partial IntPtr MemCpy(IntPtr dest, IntPtr src, UIntPtr count);
    }
}