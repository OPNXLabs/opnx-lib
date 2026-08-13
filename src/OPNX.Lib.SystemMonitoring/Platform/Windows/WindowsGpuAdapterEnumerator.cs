using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OPNX.Lib.SystemMonitoring.Platform.Windows;

[SupportedOSPlatform("windows")]
internal static class WindowsGpuAdapterEnumerator
{
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private static readonly Guid FactoryGuid = typeof(IDxgiFactory1).GUID;

    public static IReadOnlyList<WindowsGpuAdapter> GetAdapters()
    {
        List<WindowsGpuAdapter> adapters = [];
        IDxgiFactory1? factory = null;
        try
        {
            Guid factoryGuid = FactoryGuid;
            int result = CreateDXGIFactory1(ref factoryGuid, out factory);
            if (result < 0 || factory is null)
                return adapters;

            for (uint index = 0; ; index++)
            {
                result = factory.EnumAdapters1(index, out IDxgiAdapter1? adapter);
                if (result == DxgiErrorNotFound)
                    break;
                if (result < 0 || adapter is null)
                    continue;

                try
                {
                    if (adapter.GetDesc1(out DxgiAdapterDescription description) < 0)
                        continue;

                    string luid = $"luid_0x{unchecked((uint)description.AdapterLuid.HighPart):x8}_0x{description.AdapterLuid.LowPart:x8}";
                    adapters.Add(new WindowsGpuAdapter(
                        luid,
                        description.Description.TrimEnd('\0', ' '),
                        description.VendorId,
                        ToInt64(description.DedicatedVideoMemory)));
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        catch (COMException)
        {
            return [];
        }
        finally
        {
            if (factory is not null)
                Marshal.ReleaseComObject(factory);
        }

        return adapters;
    }

    private static long ToInt64(UIntPtr value)
    {
        ulong number = value.ToUInt64();
        return number > long.MaxValue ? long.MaxValue : (long)number;
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1([In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDxgiFactory1? factory);

    [ComImport, Guid("770AAE78-F26F-4DBA-A829-253C83D1B387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDxgiFactory1
    {
        [PreserveSig] int SetPrivateData();
        [PreserveSig] int SetPrivateDataInterface();
        [PreserveSig] int GetPrivateData();
        [PreserveSig] int GetParent();
        [PreserveSig] int EnumAdapters();
        [PreserveSig] int MakeWindowAssociation();
        [PreserveSig] int GetWindowAssociation();
        [PreserveSig] int CreateSwapChain();
        [PreserveSig] int CreateSoftwareAdapter();
        [PreserveSig] int EnumAdapters1(uint adapter, [MarshalAs(UnmanagedType.Interface)] out IDxgiAdapter1? adapterInterface);
        [PreserveSig] bool IsCurrent();
    }

    [ComImport, Guid("29038F61-3839-4626-91FD-086879011A05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDxgiAdapter1
    {
        [PreserveSig] int SetPrivateData();
        [PreserveSig] int SetPrivateDataInterface();
        [PreserveSig] int GetPrivateData();
        [PreserveSig] int GetParent();
        [PreserveSig] int EnumOutputs();
        [PreserveSig] int GetDesc();
        [PreserveSig] int CheckInterfaceSupport();
        [PreserveSig] int GetDesc1(out DxgiAdapterDescription description);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSystemId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public Luid AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }
}

internal sealed record WindowsGpuAdapter(string Luid, string Name, uint VendorId, long DedicatedMemoryBytes);
