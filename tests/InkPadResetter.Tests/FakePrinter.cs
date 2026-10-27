using System.Text;
using InkPadResetter.Core;

namespace InkPadResetter.Tests;

/// <summary>
/// Simulates an L120 at the byte level: EJL entry, D4 revision 0x10 transactions,
/// and di / st / || commands on EPSON-CTRL, behaving like the real printer did on 2026-10-27.
/// </summary>
sealed class FakePrinter : IPrinterTransport
{
    // ST2 status the real L120 sent while showing "Service required".
    public static readonly byte[] ServiceRequiredStatus = Convert.FromHexString((
        "40 42 44 43 20 53 54 32 0d 0a 5f 00 01 01 00 02 01 10 06 02 01 ff 0f 0d 03 03 01 69 04 02 69 05 03 69 " +
        "01 00 69 10 03 01 09 4e 13 01 01 19 0c 00 00 00 00 00 75 6e 6b 6e 6f 77 6e 24 02 00 00 2f 01 01 36 14 " +
        "ff ff ff ff ff ff ff ff 8c 31 00 00 04 2e 00 00 4b 00 00 00 37 05 02 02 00 00 00 40 0a 54 50 33 4b 35 " +
        "32 30 33 34 36").Replace(" ", ""));

    public byte[] Memory { get; } = new byte[0x200];
    public string Model { get; set; } = "L120 Series";
    public byte[] Status { get; set; } = ServiceRequiredStatus;
    /// <summary>A write to this address is acknowledged but doesn't stick.</summary>
    public ushort? BrokenAddress { get; set; }
    public bool InPacketMode { get; private set; }
    public List<(ushort Address, byte Value)> Writes { get; } = [];

    readonly Queue<byte[]> _replies = new();

    public FakePrinter()
    {
        Memory[0x18] = 0x3F; Memory[0x19] = 0x18;
        Memory[0x1C] = 0xAE; Memory[0x1D] = 0x25;
        Memory[0x2E] = 0x68;
        Encoding.ASCII.GetBytes("TP3K520346").CopyTo(Memory, 0xC0);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (!InPacketMode)
        {
            if (Encoding.ASCII.GetString(data).Contains("@EJL 1284.4"))
            {
                InPacketMode = true;
                _replies.Enqueue([0x00, 0x00, 0x00, 0x08, 0x01, 0x00, 0xC5, 0x00]);
            }
            return;
        }

        byte socket = data[0];
        var payload = data[6..].ToArray();
        if (socket == 0x00) Transaction(payload);
        else if (socket == 0x02) Control(payload);
    }

    void Transaction(byte[] p)
    {
        switch (p[0])
        {
            case 0x00: Reply(0, [0x80, (byte)(p[1] == 0x10 ? 0x00 : 0x02), 0x10]); break;
            case 0x01: Reply(0, [0x81, 0x00, 0x02, 0x02, 0x00, 0x40, 0x01, 0x00, 0x00, 0x00]); break;
            case 0x04: Reply(0, [0x84, 0x00, 0x02, 0x02, 0x00, 0x01]); break;
            case 0x02: Reply(0, [0x82, 0x00, 0x02, 0x02]); break;
            case 0x08: Reply(0, [0x88, 0x00]); InPacketMode = false; break;
        }
    }

    void Control(byte[] p)
    {
        string name = Encoding.ASCII.GetString(p, 0, 2);
        var body = p[4..];
        switch (name)
        {
            case "di":
                Reply(2, Encoding.ASCII.GetBytes($"@EJL ID\r\nMFG:EPSON;CMD:ESCPL2,BDC,D4,D4PX,END4;MDL:{Model};CLS:PRINTER;DES:EPSON {Model};"));
                break;
            case "st":
                Reply(2, Status);
                break;
            case "||":
                ushort key = (ushort)(body[0] | body[1] << 8);
                char letter = (char)body[2];
                ushort address = (ushort)(body[5] | body[6] << 8);
                if (key != PrinterModel.L120.ReadKey) { Reply(2, "||:NA;\f"u8.ToArray()); break; }
                if (letter == 'A')
                {
                    Reply(2, Encoding.ASCII.GetBytes($"@BDC PS\r\nEE:{address:X4}{Memory[address]:X2};\f"));
                }
                else if (letter == 'B')
                {
                    byte value = body[7];
                    if (Encoding.ASCII.GetString(body, 8, body.Length - 8) != PrinterModel.L120.WriteKey)
                    {
                        Reply(2, "||:42:NG;\f"u8.ToArray());
                        break;
                    }
                    Writes.Add((address, value));
                    if (address != BrokenAddress) Memory[address] = value;
                    Reply(2, "||:42:OK;\f"u8.ToArray());
                }
                break;
        }
    }

    void Reply(byte socket, byte[] payload)
    {
        int length = 6 + payload.Length;
        _replies.Enqueue([socket, socket, (byte)(length >> 8), (byte)length, 0x01, 0x00, .. payload]);
    }

    public byte[] Read(int timeoutMs) => _replies.Count > 0 ? _replies.Dequeue() : [];

    public void Dispose() { }
}
