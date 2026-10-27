using System.Text;
using System.Text.RegularExpressions;

namespace InkPadResetter.Core;

/// <summary>One open EPSON-CTRL conversation: device ID, status, and EEPROM reads and writes.</summary>
internal sealed partial class EpsonSession : IDisposable
{
    readonly D4Link _link;

    EpsonSession(D4Link link) => _link = link;

    public static EpsonSession Open(IPrinterTransport io)
    {
        var link = new D4Link(io);
        link.Open();
        return new EpsonSession(link);
    }

    /// <summary>The IEEE 1284 device ID, e.g. "MFG:EPSON;...;MDL:L120 Series;...".</summary>
    public string DeviceId()
    {
        string reply = Encoding.ASCII.GetString(_link.Call(Command("di"u8, [0x01])));
        int start = reply.IndexOf("MFG:", StringComparison.Ordinal);
        if (start < 0) throw new PrinterProtocolException("The printer didn't say what model it is.");
        return reply[start..].Trim();
    }

    public byte[] StatusReply() => _link.Call(Command("st"u8, [0x01]));

    public byte ReadEeprom(ushort readKey, ushort address)
    {
        string reply = Encoding.ASCII.GetString(_link.Call(Factory(readKey, 'A', [(byte)address, (byte)(address >> 8)])));
        var match = EepromReply().Match(reply);
        if (!match.Success || Convert.ToUInt16(match.Groups[1].Value, 16) != address)
            throw new PrinterProtocolException($"Couldn't read address 0x{address:X2} from the printer.");
        return Convert.ToByte(match.Groups[2].Value, 16);
    }

    public void WriteEeprom(ushort readKey, ReadOnlySpan<byte> writeKey, ushort address, byte value)
    {
        byte[] payload = [(byte)address, (byte)(address >> 8), value, .. writeKey];
        string reply = Encoding.ASCII.GetString(_link.Call(Factory(readKey, 'B', payload)));
        if (!reply.Contains(":OK;", StringComparison.Ordinal))
            throw new PrinterProtocolException($"The printer refused the write to 0x{address:X2}.");
    }

    public void Dispose() => _link.Close();

    static byte[] Command(ReadOnlySpan<byte> name, ReadOnlySpan<byte> payload) =>
        [.. name, (byte)payload.Length, (byte)(payload.Length >> 8), .. payload];

    /// <summary>A "||" factory command: the model's read key, the command letter and two check bytes.</summary>
    static byte[] Factory(ushort readKey, char letter, ReadOnlySpan<byte> payload)
    {
        byte c = (byte)letter;
        byte[] body = [(byte)readKey, (byte)(readKey >> 8), c, (byte)~c, (byte)((c >> 1 & 0x7F) | (c << 7 & 0x80)), .. payload];
        return Command("||"u8, body);
    }

    [GeneratedRegex(@"EE:([0-9A-Fa-f]{4})([0-9A-Fa-f]{2});")]
    private static partial Regex EepromReply();
}
