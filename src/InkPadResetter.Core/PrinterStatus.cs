using System.Text;

namespace InkPadResetter.Core;

/// <summary>The parts of Epson's binary "ST2" status reply this app uses.</summary>
public sealed record PrinterStatus(byte? State, byte? Error, string? Serial)
{
    public const byte WasteInkError = 0x10;
    const byte StateIdle = 0x04;

    public bool NeedsService => Error == WasteInkError;
    public bool IsError => State == 0x00 || Error is not null;

    public string Describe() => Error switch
    {
        WasteInkError => "Service required",
        0x04 => "Paper jam",
        0x05 => "Out of ink",
        0x06 => "Out of paper",
        byte e => $"Printer error 0x{e:X2}",
        null when State == StateIdle => "Ready",
        null => "Busy",
    };

    /// <summary>Parses "@BDC ST2\r\n" + length + (type, length, data) fields.</summary>
    public static PrinterStatus Parse(byte[] reply)
    {
        int start = IndexOf(reply, "ST2\r\n"u8);
        if (start < 0 || start + 7 > reply.Length) return new(null, null, null);
        int pos = start + 5;
        int end = Math.Min(reply.Length, pos + 2 + (reply[pos] | reply[pos + 1] << 8));
        pos += 2;

        byte? state = null, error = null;
        string? serial = null;
        while (pos + 2 <= end)
        {
            byte type = reply[pos], length = reply[pos + 1];
            int data = pos + 2;
            if (data + length > end) break;
            switch (type)
            {
                case 0x01 when length >= 1: state = reply[data]; break;
                case 0x02 when length >= 1: error = reply[data]; break;
                case 0x40: serial = Encoding.ASCII.GetString(reply, data, length).Trim('\0', ' '); break;
            }
            pos = data + length;
        }
        return new(state, error, serial);
    }

    static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle) => haystack.AsSpan().IndexOf(needle);
}
