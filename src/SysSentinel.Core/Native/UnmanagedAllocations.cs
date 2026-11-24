using System.Runtime.InteropServices;

namespace SysSentinel.Core.Native;

/// <summary>Gom các vùng nhớ unmanaged cấp phát cho một lần gọi native và giải phóng hết khi Dispose.</summary>
internal sealed class UnmanagedAllocations : IDisposable
{
    private readonly List<IntPtr> _blocks = new();

    public IntPtr AllocString(string value)
    {
        IntPtr p = Marshal.StringToHGlobalUni(value);
        _blocks.Add(p);
        return p;
    }

    public IntPtr AllocBytes(byte[] data)
    {
        IntPtr p = Marshal.AllocHGlobal(data.Length);
        _blocks.Add(p);
        Marshal.Copy(data, 0, p, data.Length);
        return p;
    }

    public IntPtr AllocStruct<T>(T value) where T : struct
    {
        IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        _blocks.Add(p);
        Marshal.StructureToPtr(value, p, false);
        return p;
    }

    public void Dispose()
    {
        foreach (IntPtr p in _blocks) Marshal.FreeHGlobal(p);
        _blocks.Clear();
    }
}
