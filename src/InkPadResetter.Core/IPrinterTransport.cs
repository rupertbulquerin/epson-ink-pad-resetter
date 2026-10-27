namespace InkPadResetter.Core;

/// <summary>A raw two-way byte pipe to the printer (USB in the app, a simulator in tests).</summary>
public interface IPrinterTransport : IDisposable
{
    void Write(ReadOnlySpan<byte> data);

    /// <summary>Returns whatever the printer sent within the timeout; empty when it sent nothing.</summary>
    byte[] Read(int timeoutMs);
}
