namespace InkPadResetter.Core;

public sealed record CounterReading(string Name, byte LowByte, byte HighByte)
{
    public int Value => LowByte | HighByte << 8;
}

public sealed record PrinterSnapshot(
    string DeviceId,
    PrinterModel? Model,
    PrinterStatus Status,
    IReadOnlyList<CounterReading> Counters)
{
    public string ModelName => PrinterModel.ModelField(DeviceId);
    public string Serial => Status.Serial ?? "unknown";
}

/// <summary>
/// The app's operations. Each call opens its own session and leaves packet mode afterwards,
/// so the printer is free to print between them.
/// </summary>
public sealed class Resetter(Func<IPrinterTransport> connect)
{
    public static Resetter ForUsb() => new(() =>
    {
        string path = UsbPrintPort.FindDevicePaths().FirstOrDefault() ?? throw new PrinterNotFoundException();
        return UsbPrintPort.Open(path);
    });

    public PrinterSnapshot Read() => InSession(Snapshot);

    /// <summary>Reads the whole EEPROM. Refuses unknown models, since their read key is unknown.</summary>
    public EepromBackup Backup(IProgress<int>? progress = null) => InSession(session =>
    {
        var snapshot = Snapshot(session);
        var model = snapshot.Model ?? throw new UnsupportedPrinterException(snapshot.ModelName);
        var data = new byte[model.MemorySize];
        for (ushort address = 0; address < data.Length; address++)
        {
            data[address] = session.ReadEeprom(model.ReadKey, address);
            if (address % 16 == 15) progress?.Report(address + 1);
        }
        return new EepromBackup(model.Name, snapshot.Serial, DateTime.Now, data);
    });

    /// <summary>
    /// Writes the model's reset values, reading each one back. On any failure the bytes
    /// written so far get their previous values back before the error is thrown.
    /// </summary>
    public PrinterSnapshot Reset() => InSession(session =>
    {
        var before = Snapshot(session);
        var model = before.Model ?? throw new UnsupportedPrinterException(before.ModelName);
        return WriteVerified(session, model, model.ResetValues) is { } failure
            ? throw failure
            : Snapshot(session);
    });

    /// <summary>Puts the reset addresses back to the values in a backup of this same printer.</summary>
    public PrinterSnapshot Restore(EepromBackup backup) => InSession(session =>
    {
        var before = Snapshot(session);
        var model = before.Model ?? throw new UnsupportedPrinterException(before.ModelName);
        if (!string.Equals(backup.Serial, before.Serial, StringComparison.OrdinalIgnoreCase))
            throw new PrinterException($"That backup is from printer {backup.Serial}, not {before.Serial}.");
        var values = model.ResetValues.Select(r => (r.Address, backup.Data[r.Address])).ToList();
        return WriteVerified(session, model, values) is { } failure ? throw failure : Snapshot(session);
    });

    static ResetFailedException? WriteVerified(EpsonSession session, PrinterModel model,
                                               IReadOnlyList<(ushort Address, byte Value)> values)
    {
        var key = model.WriteKeyBytes;
        var previous = values.Select(v => (v.Address, Value: session.ReadEeprom(model.ReadKey, v.Address))).ToList();
        var touched = new List<(ushort Address, byte Value)>();
        foreach (var (address, value) in values)
        {
            try
            {
                touched.Add(previous.First(p => p.Address == address));
                session.WriteEeprom(model.ReadKey, key, address, value);
                if (session.ReadEeprom(model.ReadKey, address) != value)
                    throw new PrinterProtocolException($"0x{address:X2} didn't keep the new value.");
            }
            catch (PrinterException ex)
            {
                return new ResetFailedException(address, Rollback(session, model, touched), ex);
            }
        }
        return null;
    }

    static bool Rollback(EpsonSession session, PrinterModel model, List<(ushort Address, byte Value)> touched)
    {
        bool ok = true;
        foreach (var (address, value) in touched)
        {
            try
            {
                session.WriteEeprom(model.ReadKey, model.WriteKeyBytes, address, value);
                ok &= session.ReadEeprom(model.ReadKey, address) == value;
            }
            catch (PrinterException)
            {
                ok = false;
            }
        }
        return ok;
    }

    static PrinterSnapshot Snapshot(EpsonSession session)
    {
        string deviceId = session.DeviceId();
        var model = PrinterModel.Match(deviceId);
        var status = PrinterStatus.Parse(session.StatusReply());
        var counters = model?.Counters
            .Select(c => new CounterReading(c.Name, session.ReadEeprom(model.ReadKey, c.Low), session.ReadEeprom(model.ReadKey, c.High)))
            .ToList() ?? [];
        return new PrinterSnapshot(deviceId, model, status, counters);
    }

    T InSession<T>(Func<EpsonSession, T> work)
    {
        using var io = connect();
        using var session = EpsonSession.Open(io);
        return work(session);
    }
}
