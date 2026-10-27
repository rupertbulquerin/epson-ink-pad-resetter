namespace InkPadResetter.Core;

/// <summary>A failure whose message can be shown to the user as is.</summary>
public class PrinterException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class PrinterNotFoundException()
    : PrinterException("No Epson printer found. Connect it by USB and turn it on.");

public sealed class PrinterBusyException()
    : PrinterException("The printer didn't respond. Cancel any print jobs, turn the printer off and on, then try again.");

public sealed class PrinterProtocolException(string message) : PrinterException(message);

public sealed class UnsupportedPrinterException(string model)
    : PrinterException($"{model} isn't supported. This app only knows the Epson L120.")
{
    public string Model { get; } = model;
}

public sealed class ResetFailedException(ushort address, bool restored, Exception? inner = null)
    : PrinterException(restored
        ? $"The reset failed at 0x{address:X2}. The original values were put back, so nothing changed."
        : $"The reset failed at 0x{address:X2} and the original values couldn't all be put back. Use the backup to restore them.",
        inner)
{
    public ushort Address { get; } = address;
    public bool Restored { get; } = restored;
}
