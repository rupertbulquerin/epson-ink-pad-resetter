namespace InkPadResetter.Core;

/// <summary>
/// A minimal IEEE 1284.4 ("D4") client: just enough to open Epson's EPSON-CTRL channel and
/// trade one command for one reply on it. Uses transaction revision 0x10; the L120 refuses 0x20.
/// </summary>
internal sealed class D4Link(IPrinterTransport io)
{
    public const byte ControlSocket = 0x02;
    const int ReadTimeoutMs = 800;
    const int HeaderLength = 6;

    static readonly byte[] EnterPacketMode = "\0\0\0\u001b\u0001@EJL 1284.4\n@EJL\n@EJL\n"u8.ToArray();
    static readonly byte[] EnterPacketModeReply = [0x00, 0x00, 0x00, 0x08, 0x01, 0x00, 0xC5, 0x00];

    byte[] _received = [];

    public void Open()
    {
        if (!Enter())
        {
            // A session that died mid-way can leave the printer in packet mode: leave it and start over.
            Transact([0x08]);
            if (!Enter()) throw new PrinterBusyException();
        }

        byte[]? open = Transact([0x01, ControlSocket, ControlSocket, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00]);
        if (open is not { Length: >= 2 } || open[1] != 0x00)
            throw new PrinterProtocolException("The printer didn't open its control channel.");
    }

    bool Enter()
    {
        _received = [];
        io.Write(EnterPacketMode);
        var reply = new List<byte>();
        for (int i = 0; i < 3 && !Contains(reply, EnterPacketModeReply); i++)
            reply.AddRange(io.Read(ReadTimeoutMs));

        byte[]? init = Transact([0x00, 0x10]);
        return init is { Length: >= 2 } && init[1] == 0x00;
    }

    /// <summary>Sends one command on the control channel and returns the printer's reply.</summary>
    public byte[] Call(ReadOnlySpan<byte> command)
    {
        byte[]? credit = Transact([0x04, ControlSocket, ControlSocket, 0x00, 0x80, 0xFF, 0xFF]);
        if (credit is not { Length: >= 2 } || credit[1] != 0x00)
            throw new PrinterProtocolException("The printer didn't grant credit on its control channel.");

        io.Write(Packet(ControlSocket, command));
        for (int i = 0; i < 8; i++)
        {
            var packet = ReceivePacket();
            if (packet is null) break;
            if (packet.Value.Socket == ControlSocket) return packet.Value.Payload;
        }
        throw new PrinterProtocolException("The printer stopped answering.");
    }

    /// <summary>Closes the channel and leaves packet mode so the printer can print again.</summary>
    public void Close()
    {
        try
        {
            Transact([0x02, ControlSocket, ControlSocket, 0x00]);
            Transact([0x08]);
        }
        catch (PrinterException)
        {
            // The printer resets packet mode when it's switched off; nothing more to do here.
        }
    }

    byte[]? Transact(byte[] command)
    {
        io.Write(Packet(0x00, command));
        byte expected = (byte)(command[0] | 0x80);
        for (int i = 0; i < 8; i++)
        {
            var packet = ReceivePacket();
            if (packet is null) return null;
            if (packet.Value.Socket == 0x00 && packet.Value.Payload.Length > 0 && packet.Value.Payload[0] == expected)
                return packet.Value.Payload;
            // Anything else (an Error packet, a stray status) is skipped.
        }
        return null;
    }

    (byte Socket, byte[] Payload)? ReceivePacket()
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (_received.Length >= HeaderLength)
            {
                int length = _received[2] << 8 | _received[3];
                if (length < HeaderLength)
                {
                    _received = []; // not a D4 packet: the printer isn't in packet mode
                    return null;
                }
                if (_received.Length >= length)
                {
                    var packet = (_received[0], _received[HeaderLength..length]);
                    _received = _received[length..];
                    return packet;
                }
            }
            var chunk = io.Read(ReadTimeoutMs);
            if (chunk.Length > 0) _received = [.. _received, .. chunk];
        }
        return null;
    }

    static byte[] Packet(byte socket, ReadOnlySpan<byte> payload)
    {
        int length = HeaderLength + payload.Length;
        return [socket, socket, (byte)(length >> 8), (byte)length, 0x01, 0x00, .. payload];
    }

    static bool Contains(List<byte> haystack, byte[] needle) =>
        haystack.Count >= needle.Length && Enumerable.Range(0, haystack.Count - needle.Length + 1)
            .Any(i => haystack.Skip(i).Take(needle.Length).SequenceEqual(needle));
}
