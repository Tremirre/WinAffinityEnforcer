using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinAffinityEnforcer;

/// <summary>One logical processor and the efficiency class of the core it lives on.</summary>
public readonly record struct CpuSetInfo(ushort Group, byte LogicalProcessorIndex, byte CoreIndex, byte EfficiencyClass);

/// <summary>
/// Reads the Windows CPU set layout so users can find real logical CPU numbers on hybrid CPUs,
/// where P-cores are not simply the lowest numbers.
/// </summary>
public static class CpuTopology
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemCpuSetInformation(IntPtr information, uint bufferLength, out uint returnedLength, IntPtr process, uint flags);

    public static IReadOnlyList<CpuSetInfo> GetCpuSets()
    {
        GetSystemCpuSetInformation(IntPtr.Zero, 0, out var length, IntPtr.Zero, 0);
        if (length == 0) return [];

        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetSystemCpuSetInformation(buffer, length, out length, IntPtr.Zero, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetSystemCpuSetInformation failed.");

            var sets = new List<CpuSetInfo>();
            var offset = 0L;
            while (offset < length)
            {
                var entry = (nint)(buffer.ToInt64() + offset);
                var size = (uint)Marshal.ReadInt32(entry);
                if (size == 0) break;

                // CpuSet records (Type 0) only, laid out as SYSTEM_CPU_SET_INFORMATION.CpuSet.
                if (Marshal.ReadInt32(entry, 4) == 0)
                {
                    sets.Add(new CpuSetInfo(
                        (ushort)Marshal.ReadInt16(entry, 12),
                        Marshal.ReadByte(entry, 14),
                        Marshal.ReadByte(entry, 15),
                        Marshal.ReadByte(entry, 18)));
                }
                offset += size;
            }
            return sets;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Windows gives the faster P-cores the higher efficiency class and the E-cores the lower one.
    /// </summary>
    public static string Describe()
    {
        var sets = GetCpuSets();
        if (sets.Count == 0) return "No CPU set information available.";

        var classes = sets.Select(set => set.EfficiencyClass).Distinct().OrderDescending().ToList();
        var lines = new List<string> { $"Logical CPUs: {sets.Count}" };
        foreach (var cluster in sets.GroupBy(set => set.EfficiencyClass).OrderByDescending(group => group.Key))
        {
            var cpus = string.Join(", ", cluster.OrderBy(set => set.LogicalProcessorIndex).Select(set => set.LogicalProcessorIndex));
            var label = classes.Count == 1
                ? "Cores (uniform)"
                : cluster.Key == classes[0]
                    ? $"P-cores (fastest, EfficiencyClass {cluster.Key})"
                    : $"E-cores (EfficiencyClass {cluster.Key})";
            lines.Add($"{label}: {cpus}");
        }
        return string.Join(Environment.NewLine, lines);
    }
}
