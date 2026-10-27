using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using InkPadResetter.Core;

namespace InkPadResetter.App;

public partial class MainWindow : Window
{
    public static string BackupDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ink Pad Resetter", "Backups");

    readonly Resetter _resetter;
    PrinterSnapshot? _snapshot;
    string? _backupPath;
    string? _backupSerial;
    bool _busy;
    bool _resetDone;

    public MainWindow(Resetter resetter)
    {
        _resetter = resetter;
        InitializeComponent();
        SourceInitialized += (_, _) => UseDarkTitleBar();
    }

    public async Task RefreshAsync()
    {
        SetBusy(true);
        _resetDone = false;
        ShowSearching();
        try
        {
            var snapshot = await Task.Run(_resetter.Read);
            ShowSnapshot(snapshot);
            if (snapshot.Model is { } model && _backupSerial != snapshot.Serial)
            {
                ShowBackupProgress(0, model.MemorySize);
                var progress = new Progress<int>(n => ShowBackupProgress(n, model.MemorySize));
                var backup = await Task.Run(() => _resetter.Backup(progress));
                _backupPath = backup.Save(BackupDirectory);
                _backupSerial = backup.Serial;
            }
            ShowBackupDone();
        }
        catch (Exception ex) when (ex is PrinterException or IOException or UnauthorizedAccessException)
        {
            ShowFailure(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    async Task ResetAsync()
    {
        SetBusy(true);
        ResetButton.Content = "Resetting…";
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var after = await Task.Run(_resetter.Reset);
            _resetDone = true;
            ShowSnapshot(after);
        }
        catch (PrinterException ex)
        {
            ShowFailure(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Puts the window in a given state without a printer, for --render.</summary>
    internal void ShowPreview(PrinterSnapshot? snapshot, string? backupPath, bool resetDone, bool padChecked, string? error)
    {
        _resetDone = resetDone;
        _backupPath = backupPath;
        if (snapshot is null) ShowSearching();
        else ShowSnapshot(snapshot);
        if (backupPath is not null) ShowBackupDone();
        PadCheck.IsChecked = padChecked;
        if (error is not null) ShowFailure(error);
        UpdateResetButton();
    }

    void ShowSearching()
    {
        _snapshot = null;
        PrinterName.Text = "Epson printer";
        PrinterDetail.Text = "USB";
        SetEyebrow("CONNECTING", Brush("Text3"));
        Heading.Text = "Looking for the printer…";
        Body.Text = "Reading its status and waste ink counters over USB.";
        ErrorText.Visibility = Visibility.Collapsed;
        CountersPanel.Visibility = Visibility.Collapsed;
        BackupRow.Visibility = Visibility.Collapsed;
    }

    void ShowSnapshot(PrinterSnapshot snapshot)
    {
        _snapshot = snapshot;
        PrinterName.Text = "EPSON " + snapshot.ModelName;
        PrinterDetail.Text = "USB · " + snapshot.Serial;
        ErrorText.Visibility = Visibility.Collapsed;
        var status = snapshot.Status;

        if (snapshot.Model is null)
        {
            SetEyebrow("NOT SUPPORTED", Brush("Text3"));
            Heading.Text = snapshot.ModelName;
            Body.Text = "This app only knows the Epson L120, so it won't read or change anything on this printer.";
        }
        else if (_resetDone)
        {
            SetEyebrow("COUNTERS RESET", Brush("Text"));
            Heading.Text = "Turn the printer off and on";
            Body.Text = "The waste ink counters are back to zero. After a restart the printer should stop asking for service.";
        }
        else if (status.NeedsService)
        {
            SetEyebrow($"STATUS 0x{status.Error:X2}", Brush("Amber"));
            Heading.Text = "Service required";
            Body.Text = "The printer has stopped because its waste ink counter reached the limit. Resetting the counter lets it print again.";
        }
        else if (status.Error is { } error)
        {
            SetEyebrow($"STATUS 0x{error:X2}", Brush("Amber"));
            Heading.Text = status.Describe();
            Body.Text = "This isn't the ink pad error, so resetting the counters won't clear it.";
        }
        else
        {
            SetEyebrow("STATUS", Brush("Text3"));
            Heading.Text = status.Describe();
            Body.Text = "The waste ink counter hasn't reached its limit. A reset now would start the count again from zero.";
        }

        if (snapshot.Model is { } model && snapshot.Counters.Count == 2)
        {
            ShowCounter(Counter1Label, Counter1Value, Counter1Detail, snapshot.Counters[0], model.Counters[0]);
            ShowCounter(Counter2Label, Counter2Value, Counter2Detail, snapshot.Counters[1], model.Counters[1]);
            CountersPanel.Visibility = Visibility.Visible;
        }
        else
        {
            CountersPanel.Visibility = Visibility.Collapsed;
        }
        UpdateResetButton();
    }

    static void ShowCounter(System.Windows.Controls.TextBlock label, System.Windows.Controls.TextBlock value,
                            System.Windows.Controls.TextBlock detail, CounterReading reading, CounterSpec spec)
    {
        label.Text = reading.Name;
        value.Text = reading.Value.ToString("N0");
        detail.Text = $"0x{spec.Low:X2}–0x{spec.High:X2} · {reading.LowByte:X2} {reading.HighByte:X2}";
    }

    void ShowBackupProgress(int done, int total)
    {
        BackupRow.Visibility = Visibility.Visible;
        BackupTick.Visibility = Visibility.Collapsed;
        BackupLinkBlock.Visibility = Visibility.Collapsed;
        BackupText.Text = $"Backing up memory… {done} of {total} bytes";
    }

    void ShowBackupDone()
    {
        if (_backupPath is null) return;
        BackupRow.Visibility = Visibility.Visible;
        BackupTick.Visibility = Visibility.Visible;
        BackupLinkBlock.Visibility = Visibility.Visible;
        BackupText.Text = "Memory backed up";
        BackupFileName.Text = Path.GetFileName(_backupPath);
    }

    void ShowFailure(string message)
    {
        if (_snapshot is null)
        {
            SetEyebrow("NOT CONNECTED", Brush("Text3"));
            Heading.Text = "Can't reach the printer";
            Body.Text = message;
        }
        else
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    void SetEyebrow(string text, Brush brush)
    {
        Eyebrow.Text = text;
        Eyebrow.Foreground = brush;
        StatusDot.Fill = brush;
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        ReadAgainButton.IsEnabled = !busy;
        UpdateResetButton();
    }

    void UpdateResetButton()
    {
        bool supported = _snapshot?.Model is not null;
        PadBox.Visibility = supported && !_resetDone ? Visibility.Visible : Visibility.Hidden;
        PadCheck.IsEnabled = !_busy;
        ResetButton.Content = _resetDone ? "Reset done" : _busy && supported && _backupPath is not null ? ResetButton.Content : "Reset counters";
        ResetButton.IsEnabled = !_busy && !_resetDone && supported && _backupPath is not null && PadCheck.IsChecked == true;
    }

    Brush Brush(string key) => (Brush)FindResource(key);

    async void ReadAgain_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    async void Reset_Click(object sender, RoutedEventArgs e) => await ResetAsync();

    void PadCheck_Changed(object sender, RoutedEventArgs e) => UpdateResetButton();

    void BackupLink_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(BackupDirectory))
            Process.Start(new ProcessStartInfo(BackupDirectory) { UseShellExecute = true });
    }

    void UseDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int on = 1, caption = 0x151515; // COLORREF is 0x00BBGGRR; grey is the same either way
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
    }

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_CAPTION_COLOR = 35;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
