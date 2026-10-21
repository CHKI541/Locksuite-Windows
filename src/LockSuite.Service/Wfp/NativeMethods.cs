using System;
using System.Runtime.InteropServices;

namespace LockSuite.Service.Wfp;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct FWPM_DISPLAY_DATA0
{
    [MarshalAs(UnmanagedType.LPWStr)]
    public string name;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string? description;
}

[StructLayout(LayoutKind.Sequential)]
public struct FWPM_SESSION0
{
    public Guid sessionKey;
    public FWPM_DISPLAY_DATA0 displayData;
    public uint flags;
    public uint txnWaitTimeoutInMSec;
    public uint processId;
    public IntPtr sid;
    public IntPtr username;
    [MarshalAs(UnmanagedType.Bool)]
    public bool kernelMode;
}

[StructLayout(LayoutKind.Sequential)]
public struct FWPM_SUBLAYER0
{
    public Guid subLayerKey;
    public FWPM_DISPLAY_DATA0 displayData;
    public uint flags;
    public IntPtr defaultSD;
    public ushort weight;
}

[StructLayout(LayoutKind.Sequential)]
public struct FWP_BYTE_BLOB
{
    public uint size;
    public IntPtr data;
}

[StructLayout(LayoutKind.Sequential)]
public struct FWP_V4_ADDR_AND_MASK
{
    public uint addr;
    public uint mask;
}

[StructLayout(LayoutKind.Explicit)]
public struct FWP_VALUE0
{
    [FieldOffset(0)]
    public uint type;
    [FieldOffset(8)]
    public byte uint8;
    [FieldOffset(8)]
    public ushort uint16;
    [FieldOffset(8)]
    public uint uint32;
    [FieldOffset(8)]
    public ulong uint64;
    [FieldOffset(8)]
    public IntPtr byteBlob;
    [FieldOffset(8)]
    public IntPtr v4AddrMask;
    [FieldOffset(8)]
    public IntPtr byteArray16;
}

[StructLayout(LayoutKind.Explicit)]
public struct FWP_CONDITION_VALUE0
{
    [FieldOffset(0)]
    public uint type;
    [FieldOffset(8)]
    public byte uint8;
    [FieldOffset(8)]
    public ushort uint16;
    [FieldOffset(8)]
    public uint uint32;
    [FieldOffset(8)]
    public ulong uint64;
    [FieldOffset(8)]
    public IntPtr byteBlob;
    [FieldOffset(8)]
    public IntPtr v4AddrMask;
    [FieldOffset(8)]
    public IntPtr byteArray16;
}

[StructLayout(LayoutKind.Sequential)]
public struct FWPM_FILTER_CONDITION0
{
    public Guid fieldKey;
    public uint matchType;
    public FWP_CONDITION_VALUE0 conditionValue;
}

[StructLayout(LayoutKind.Sequential)]
public struct FWPM_ACTION0
{
    public uint type;
    public Guid filterType;
}

[StructLayout(LayoutKind.Sequential)]
public struct FWPM_FILTER0
{
    public Guid filterKey;
    public FWPM_DISPLAY_DATA0 displayData;
    public uint flags;
    public IntPtr providerKey;
    public FWP_BYTE_BLOB providerData;
    public Guid layerKey;
    public Guid subLayerKey;
    public FWP_VALUE0 weight;
    public uint numFilterConditions;
    public IntPtr filterCondition;
    public FWPM_ACTION0 action;
    public ulong context;
    public IntPtr reserved;
    public ulong filterId;
    public FWP_VALUE0 effectiveWeight;
}

public static class NativeMethods
{
    private const string Fwpuclnt = "fwpuclnt.dll";

    [DllImport(Fwpuclnt, ExactSpelling = true)]
    public static extern uint FwpmEngineOpen0(
        [MarshalAs(UnmanagedType.LPWStr)] string? serverName,
        uint authnService,
        IntPtr authIdentity,
        in FWPM_SESSION0 session,
        out IntPtr engineHandle
    );

    [DllImport(Fwpuclnt, ExactSpelling = true)]
    public static extern uint FwpmEngineClose0(IntPtr engineHandle);

    [DllImport(Fwpuclnt, ExactSpelling = true)]
    public static extern uint FwpmSubLayerAdd0(
        IntPtr engineHandle,
        in FWPM_SUBLAYER0 subLayer,
        IntPtr sd
    );

    [DllImport(Fwpuclnt, ExactSpelling = true)]
    public static extern uint FwpmSubLayerDeleteByKey0(
        IntPtr engineHandle,
        in Guid key
    );

    [DllImport(Fwpuclnt, ExactSpelling = true)]
    public static extern uint FwpmFilterAdd0(
        IntPtr engineHandle,
        in FWPM_FILTER0 filter,
        IntPtr sd,
        out ulong id
    );

    [DllImport(Fwpuclnt, ExactSpelling = true)]
    public static extern uint FwpmFilterDeleteById0(
        IntPtr engineHandle,
        ulong id
    );

    [DllImport(Fwpuclnt, ExactSpelling = true)]
    public static extern uint FwpmFilterDeleteByKey0(
        IntPtr engineHandle,
        in Guid key
    );

    [DllImport(Fwpuclnt, ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern uint FwpmGetAppIdFromFileName0(
        [MarshalAs(UnmanagedType.LPWStr)] string fileName,
        out IntPtr appIdBlobPtr
    );

    [DllImport(Fwpuclnt, ExactSpelling = true)]
    public static extern void FwpmFreeMemory0(ref IntPtr p);

    [DllImport(Fwpuclnt, ExactSpelling = true)]
    public static extern uint FwpmTransactionBegin0(IntPtr engineHandle, uint flags);

    [DllImport(Fwpuclnt, ExactSpelling = true)]
    public static extern uint FwpmTransactionCommit0(IntPtr engineHandle);

    [DllImport(Fwpuclnt, ExactSpelling = true)]
    public static extern uint FwpmTransactionAbort0(IntPtr engineHandle);
}
