using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InkPadResetter.Core;

namespace InkPadResetter.App;

static class Cli
{
    public static string ReadReport(Resetter resetter)
    {
        try
        {
            var s = resetter.Read();
            var text = new StringBuilder()
                .AppendLine($"device id: {s.DeviceId}")
                .AppendLine($"model: {s.ModelName} ({(s.Model is null ? "not supported" : "supported")})")
                .AppendLine($"serial: {s.Serial}")
                .AppendLine($"status: {s.Status.Describe()} (state {Hex(s.Status.State)}, error {Hex(s.Status.Error)})");
            foreach (var c in s.Counters)
                text.AppendLine($"{c.Name}: {c.Value} ({c.LowByte:X2} {c.HighByte:X2})");
            return text.ToString();
        }
        catch (Exception ex)
        {
            return $"error: {ex.GetType().Name}: {ex.Message}\n{ex.InnerException?.Message}";
        }
    }

    static string Hex(byte? b) => b is null ? "none" : $"0x{b:X2}";

    public static void RenderStates(string folder)
    {
        Directory.CreateDirectory(folder);
        var serviceRequired = Sample(new PrinterStatus(0x00, PrinterStatus.WasteInkError, "TP3K520346"), 0x3F, 0x18, 0xAE, 0x25);
        var afterReset = Sample(new PrinterStatus(0x00, PrinterStatus.WasteInkError, "TP3K520346"), 0, 0, 0, 0);
        const string backup = @"C:\Backups\eeprom-TP3K520346-20261027-193028.txt";

        Render(folder, "1-connecting", w => w.ShowPreview(null, null, false, false, null));
        Render(folder, "2-ready", w => w.ShowPreview(serviceRequired, backup, false, false, null));
        Render(folder, "3-checked", w => w.ShowPreview(serviceRequired, backup, false, true, null));
        Render(folder, "4-done", w => w.ShowPreview(afterReset, backup, true, true, null));
        Render(folder, "5-failed", w => w.ShowPreview(serviceRequired, backup, false, true,
            new ResetFailedException(0x1D, restored: true).Message));
        Render(folder, "6-no-printer", w => w.ShowPreview(null, null, false, false, new PrinterNotFoundException().Message));
    }

    static PrinterSnapshot Sample(PrinterStatus status, byte a, byte b, byte c, byte d) => new(
        "MFG:EPSON;CMD:ESCPL2,BDC,D4,D4PX,END4;MDL:L120 Series;CLS:PRINTER;",
        PrinterModel.L120, status,
        [new("Main waste counter", a, b), new("Second counter", c, d)]);

    static void Render(string folder, string name, Action<MainWindow> setup)
    {
        var window = new MainWindow(new Resetter(() => throw new PrinterNotFoundException()));
        setup(window);
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(root.Width, double.PositiveInfinity));
        var size = new Size(root.Width, Math.Max(root.MinHeight, root.DesiredSize.Height));
        root.Arrange(new Rect(size));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)size.Width * 2, (int)size.Height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        png.Save(file);
        window.Close();
    }
}
