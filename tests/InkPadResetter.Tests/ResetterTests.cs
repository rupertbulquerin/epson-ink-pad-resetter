using InkPadResetter.Core;

namespace InkPadResetter.Tests;

public class ResetterTests
{
    readonly FakePrinter _printer = new();
    Resetter Resetter => new(() => _printer);

    [Fact]
    public void Read_reports_model_status_and_counters()
    {
        var snapshot = Resetter.Read();

        Assert.Equal(PrinterModel.L120, snapshot.Model);
        Assert.Equal("L120 Series", snapshot.ModelName);
        Assert.Equal("TP3K520346", snapshot.Serial);
        Assert.True(snapshot.Status.NeedsService);
        Assert.Equal("Service required", snapshot.Status.Describe());
        Assert.Equal([6207, 0x25AE], snapshot.Counters.Select(c => c.Value));
        Assert.False(_printer.InPacketMode);
    }

    [Fact]
    public void Backup_reads_every_byte_and_round_trips_through_a_file()
    {
        var backup = Resetter.Backup();
        Assert.Equal(_printer.Memory, backup.Data);

        string dir = Path.Combine(Path.GetTempPath(), "inkpad-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var loaded = EepromBackup.Load(backup.Save(dir));
            Assert.Equal(backup.Data, loaded.Data);
            Assert.Equal("TP3K520346", loaded.Serial);
            Assert.Equal("L120", loaded.Model);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Reset_writes_the_reset_values_and_reads_back_zero()
    {
        var after = Resetter.Reset();

        Assert.Equal([0, 0], after.Counters.Select(c => c.Value));
        Assert.Equal(PrinterModel.L120.ResetValues, _printer.Writes);
        Assert.Equal(0x5E, _printer.Memory[0x2E]);
        Assert.False(_printer.InPacketMode);
    }

    [Fact]
    public void Reset_puts_old_values_back_when_a_write_does_not_stick()
    {
        var original = _printer.Memory.ToArray();
        _printer.BrokenAddress = 0x1D;

        var ex = Assert.Throws<ResetFailedException>(() => Resetter.Reset());

        Assert.Equal(0x1D, ex.Address);
        Assert.True(ex.Restored);
        Assert.Equal(original, _printer.Memory);
    }

    [Fact]
    public void Reset_refuses_an_unknown_model_without_writing()
    {
        _printer.Model = "L3110 Series";

        var ex = Assert.Throws<UnsupportedPrinterException>(() => Resetter.Reset());

        Assert.Equal("L3110 Series", ex.Model);
        Assert.Empty(_printer.Writes);
    }

    [Fact]
    public void Restore_puts_backup_values_back()
    {
        var backup = Resetter.Backup();
        Resetter.Reset();

        var restored = Resetter.Restore(backup);

        Assert.Equal([6207, 0x25AE], restored.Counters.Select(c => c.Value));
        Assert.Equal(0x68, _printer.Memory[0x2E]);
    }

    [Fact]
    public void Restore_refuses_a_backup_from_another_printer()
    {
        var backup = Resetter.Backup() with { Serial = "XXXX000000" };

        Assert.Throws<PrinterException>(() => Resetter.Restore(backup));
        Assert.Empty(_printer.Writes);
    }

    [Fact]
    public void Status_parser_reads_a_ready_printer()
    {
        byte[] ready = [.. "@BDC ST2\r\n"u8, 0x03, 0x00, 0x01, 0x01, 0x04];
        var status = PrinterStatus.Parse(ready);

        Assert.Equal("Ready", status.Describe());
        Assert.False(status.NeedsService);
    }
}
