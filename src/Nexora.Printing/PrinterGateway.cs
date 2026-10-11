using Nexora.Domain.Printing;
using Nexora.Printing.Layout;

namespace Nexora.Printing;

public enum PrinterState
{
    Ready,
    Busy,
    Offline,
    Error,
    PaperProblem,
    Unknown,
}

/// <summary>
/// An installed printer as the operating system reports it. Drivers differ: paper sizes and capabilities are empty or
/// null when the driver does not say, and must not be assumed.
/// </summary>
public sealed record InstalledPrinter(string Name, bool IsDefault, PrinterState State, string StatusText, IReadOnlyList<string> PaperSizes,
    bool? SupportsColour, bool? SupportsDuplex, string Location, bool IsNetwork)
{
    public bool CapabilitiesKnown => PaperSizes.Count > 0 || SupportsColour is not null || SupportsDuplex is not null;
}

public enum PrintFailure
{
    None,
    NotFound,
    Offline,
    PaperProblem,
    AccessDenied,
    DriverError,
    Unknown,
}

public sealed record PrintOutcome(bool Succeeded, PrintFailure Failure = PrintFailure.None, string Detail = "")
{
    public static PrintOutcome Ok { get; } = new(true);
}

/// <summary>A print job: the rendered pages, the printer (empty = Windows default), copies, orientation and the paper to request.</summary>
public sealed record PrintJob(RenderedDocument Document, string PrinterName, int Copies, PaperKind Paper, PageOrientation Orientation);

/// <summary>
/// The operating system's printers. The Windows implementation lives in the desktop app; tests use a fake. Nothing
/// above this interface may assume that a printer printed anything: only a person can confirm that.
/// </summary>
public interface IPrinterGateway
{
    Task<IReadOnlyList<InstalledPrinter>> ListPrintersAsync(CancellationToken cancellationToken = default);

    Task<PrintOutcome> PrintAsync(PrintJob job, CancellationToken cancellationToken = default);

    /// <summary>Sends raw bytes (ESC/POS, experimental) to the named printer, bypassing the driver's rendering.</summary>
    Task<PrintOutcome> SendRawAsync(string printerName, byte[] data, string jobName, CancellationToken cancellationToken = default);
}

/// <summary>Plain-language recovery steps for each kind of printer failure, shown with the error.</summary>
public static class PrinterGuidance
{
    public static string For(PrintFailure failure) => failure switch
    {
        PrintFailure.None => string.Empty,
        PrintFailure.NotFound => "Windows does not list this printer any more. Check it is installed (Settings → Bluetooth & devices → Printers & scanners), or choose another printer in Print centre → Preferences.",
        PrintFailure.Offline => "The printer is offline or disconnected. Check it is switched on and its cable or network is connected, then open its queue in Windows and clear \"Use printer offline\". Try again.",
        PrintFailure.PaperProblem => "The printer reports a paper problem (out of paper, jam or open cover). Fix it at the printer, then try again. Check the paper size in Print centre matches the paper loaded.",
        PrintFailure.AccessDenied => "Windows refused the job (permissions). Ask your administrator to allow printing to this printer, or choose another printer.",
        PrintFailure.DriverError => "The printer driver reported an error. Print a Windows test page from the printer's properties; if that fails too, reinstall the driver from the manufacturer.",
        _ => "The print did not complete. Check the printer's queue in Windows for errors, then try again. If it keeps failing, export a PDF and print that.",
    };
}
