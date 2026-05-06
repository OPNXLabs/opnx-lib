using System.Runtime.InteropServices;

namespace OPNX.Lib.Common.Platform.Windows
{
    public static partial class Win32
    {
        [Flags]
        public enum ExecutionState : uint
        {
            AwayModeRequired = 0x00000040,
            Continuous = 0x80000000,
            DisplayRequired = 0x00000002,
            SystemRequired = 0x00000001
        }

        public static ExecutionState SetThreadExecutionState(ExecutionState flags)
            => SetThreadExecutionStateNative(flags);

        [LibraryImport("kernel32.dll", EntryPoint = "SetThreadExecutionState")]
        private static partial ExecutionState SetThreadExecutionStateNative(ExecutionState esFlags);
    }
}
