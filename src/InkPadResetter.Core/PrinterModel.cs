using System.Text;

namespace InkPadResetter.Core;

/// <summary>A two-byte little-endian counter in EEPROM.</summary>
public sealed record CounterSpec(string Name, ushort Low, ushort High);

/// <summary>
/// What we need to know about one printer model. The L120 values come from reinkpy's model
/// table (codeberg.org/ircama/reinkpy) and were checked against a real L120 on 2026-10-27.
/// </summary>
public sealed record PrinterModel(
    string Name,
    string DeviceIdModel,
    ushort ReadKey,
    string WriteKey,
    ushort MemorySize,
    IReadOnlyList<CounterSpec> Counters,
    IReadOnlyList<(ushort Address, byte Value)> ResetValues)
{
    public byte[] WriteKeyBytes => Encoding.ASCII.GetBytes(WriteKey);

    public static PrinterModel L120 { get; } = new(
        Name: "L120",
        DeviceIdModel: "L120 Series",
        ReadKey: 0x0392,
        WriteKey: "Svecfdjb",
        MemorySize: 0x200,
        Counters:
        [
            new("Main waste counter", 0x18, 0x19),
            new("Second counter", 0x1C, 0x1D),
        ],
        ResetValues:
        [
            (0x18, 0x00), (0x19, 0x00),
            (0x1C, 0x00), (0x1D, 0x00),
            (0x1E, 0x00),
            (0x2E, 0x5E),
        ]);

    public static IReadOnlyList<PrinterModel> Known { get; } = [L120];

    /// <summary>The known model whose MDL field matches the device ID, or null.</summary>
    public static PrinterModel? Match(string deviceId) =>
        Known.FirstOrDefault(m => string.Equals(ModelField(deviceId), m.DeviceIdModel, StringComparison.OrdinalIgnoreCase));

    /// <summary>The MDL field of a device ID ("L120 Series"), or the whole ID when it has none.</summary>
    public static string ModelField(string deviceId)
    {
        foreach (var field in deviceId.Split(';'))
        {
            int colon = field.IndexOf(':');
            if (colon > 0 && field[..colon].Trim() is "MDL" or "MODEL") return field[(colon + 1)..].Trim();
        }
        return deviceId;
    }
}
