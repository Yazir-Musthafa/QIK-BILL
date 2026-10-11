using System.ComponentModel;
using System.Printing;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.Logging;
using Nexora.Domain.Printing;
using Nexora.Printing;
using Nexora.Printing.Layout;

namespace Nexora.Desktop.Services.Printing;

/// <summary>
/// Windows printers through System.Printing (the installed drivers) and, for experimental ESC/POS, the spooler's RAW
/// data type. Capabilities are read only where the driver reports them. A successful call means Windows accepted the
/// job; it never means paper came out. UI unverified at runtime: this class has not been run on Windows.
/// </summary>
public sealed class WindowsPrinterGateway(ILogger<WindowsPrinterGateway> logger) : IPrinterGateway
{
    private const double DipPerMm = 96 / 25.4;

    public Task<IReadOnlyList<InstalledPrinter>> ListPrintersAsync(CancellationToken cancellationToken = default) =>
        OnUiThread<IReadOnlyList<InstalledPrinter>>(() =>
        {
            using var server = new LocalPrintServer();
            string? defaultName = null;
            try
            {
                defaultName = LocalPrintServer.GetDefaultPrintQueue()?.FullName;
            }
            catch (PrintSystemException)
            {
                // No default printer set.
            }

            var printers = new List<InstalledPrinter>();
            foreach (var queue in server.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections]))
            {
                using (queue)
                {
                    printers.Add(Describe(queue, defaultName));
                }
            }

            return printers;
        });

    public Task<PrintOutcome> PrintAsync(PrintJob job, CancellationToken cancellationToken = default) => OnUiThread(() =>
    {
        try
        {
            using var server = new LocalPrintServer();
            using var queue = Find(server, job.PrinterName);
            if (queue is null) return new PrintOutcome(false, PrintFailure.NotFound, $"Windows has no printer named \"{job.PrinterName}\".");
            queue.Refresh();
            if (Problem(queue) is { } problem) return problem;

            var ticket = (queue.UserPrintTicket ?? queue.DefaultPrintTicket)?.Clone() ?? new PrintTicket();
            ticket.CopyCount = job.Copies;
            ticket.PageOrientation = job.Orientation == Nexora.Domain.Printing.PageOrientation.Landscape ? System.Printing.PageOrientation.Landscape : System.Printing.PageOrientation.Portrait;
            var first = job.Document.Pages[0];
            ticket.PageMediaSize = job.Paper switch
            {
                PaperKind.A4 => new PageMediaSize(PageMediaSizeName.ISOA4),
                PaperKind.A5 => new PageMediaSize(PageMediaSizeName.ISOA5),
                // Rolls and labels: a custom size matching the rendered page (in 1/96 inch).
                _ => new PageMediaSize(first.Width * 96 / 72, first.Height * 96 / 72),
            };
            var writer = PrintQueue.CreateXpsDocumentWriter(queue);
            writer.Write(WpfDocumentBuilder.Build(job.Document).DocumentPaginator, ticket);
            return PrintOutcome.Ok;
        }
        catch (PrintQueueException ex)
        {
            logger.LogWarning("Print failed; error type {ErrorType}", ex.GetType().FullName);
            return new PrintOutcome(false, PrintFailure.DriverError, ex.Message);
        }
        catch (PrintSystemException ex)
        {
            logger.LogWarning("Print failed; error type {ErrorType}", ex.GetType().FullName);
            return new PrintOutcome(false, PrintFailure.Unknown, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new PrintOutcome(false, PrintFailure.AccessDenied, ex.Message);
        }
    });

    public Task<PrintOutcome> SendRawAsync(string printerName, byte[] data, string jobName, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        if (!OpenPrinter(printerName, out var handle, IntPtr.Zero))
            return new PrintOutcome(false, PrintFailure.NotFound, new Win32Exception(Marshal.GetLastWin32Error()).Message);
        try
        {
            var info = new DocInfo { DocName = jobName, DataType = "RAW" };
            if (StartDocPrinter(handle, 1, info) == 0) return Failure("The spooler refused the raw job.");
            try
            {
                if (!StartPagePrinter(handle)) return Failure("The spooler refused the page.");
                var buffer = Marshal.AllocCoTaskMem(data.Length);
                try
                {
                    Marshal.Copy(data, 0, buffer, data.Length);
                    if (!WritePrinter(handle, buffer, data.Length, out var written) || written != data.Length) return Failure("Not all data reached the printer.");
                }
                finally
                {
                    Marshal.FreeCoTaskMem(buffer);
                    EndPagePrinter(handle);
                }
            }
            finally
            {
                EndDocPrinter(handle);
            }

            return PrintOutcome.Ok;
        }
        finally
        {
            ClosePrinter(handle);
        }
    }, cancellationToken);

    private static PrintOutcome Failure(string message) =>
        new(false, PrintFailure.DriverError, message + " " + new Win32Exception(Marshal.GetLastWin32Error()).Message);

    private static PrintQueue? Find(LocalPrintServer server, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return LocalPrintServer.GetDefaultPrintQueue();
        foreach (var queue in server.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections]))
        {
            if (string.Equals(queue.FullName, name, StringComparison.OrdinalIgnoreCase) || string.Equals(queue.Name, name, StringComparison.OrdinalIgnoreCase)) return queue;
            queue.Dispose();
        }

        return null;
    }

    private static PrintOutcome? Problem(PrintQueue queue)
    {
        if (queue.IsOffline || queue.IsNotAvailable) return new PrintOutcome(false, PrintFailure.Offline, $"{queue.FullName} is offline or not available.");
        if (queue.IsOutOfPaper || queue.IsPaperJammed || queue.IsDoorOpened || queue.HasPaperProblem)
            return new PrintOutcome(false, PrintFailure.PaperProblem, $"{queue.FullName} reports a paper or cover problem.");
        if (queue.IsInError) return new PrintOutcome(false, PrintFailure.DriverError, $"{queue.FullName} is in an error state.");
        return null;
    }

    private static InstalledPrinter Describe(PrintQueue queue, string? defaultName)
    {
        var state = queue.IsOffline || queue.IsNotAvailable ? PrinterState.Offline
            : queue.IsOutOfPaper || queue.IsPaperJammed || queue.HasPaperProblem ? PrinterState.PaperProblem
            : queue.IsInError ? PrinterState.Error
            : queue.IsPrinting || queue.IsBusy ? PrinterState.Busy
            : PrinterState.Ready;
        IReadOnlyList<string> papers = [];
        bool? colour = null, duplex = null;
        try
        {
            var capabilities = queue.GetPrintCapabilities();
            papers = capabilities.PageMediaSizeCapability
                .Select(m => m.PageMediaSizeName is { } n && n != PageMediaSizeName.Unknown
                    ? n.ToString()
                    : $"{(m.Width ?? 0) / DipPerMm:0} × {(m.Height ?? 0) / DipPerMm:0} mm")
                .Distinct().ToList();
            colour = capabilities.OutputColorCapability.Count > 0 ? capabilities.OutputColorCapability.Contains(OutputColor.Color) : null;
            duplex = capabilities.DuplexingCapability.Count > 0 ? capabilities.DuplexingCapability.Any(d => d != Duplexing.OneSided) : null;
        }
        catch (Exception ex) when (ex is PrintQueueException or PrintSystemException or InvalidOperationException)
        {
            // The driver does not report capabilities; leave them unknown rather than guessing.
        }

        return new InstalledPrinter(queue.FullName, string.Equals(queue.FullName, defaultName, StringComparison.OrdinalIgnoreCase), state, StatusText(queue, state),
            papers, colour, duplex, queue.Location ?? string.Empty, queue.HostingPrintServer?.Name is { Length: > 0 } host && host.StartsWith(@"\\", StringComparison.Ordinal));
    }

    private static string StatusText(PrintQueue queue, PrinterState state) => state switch
    {
        PrinterState.Ready => queue.NumberOfJobs > 0 ? $"Ready, {queue.NumberOfJobs} job(s) waiting" : "Ready",
        PrinterState.Busy => "Printing",
        PrinterState.Offline => "Offline or not available",
        PrinterState.PaperProblem => "Paper problem (out of paper, jam or open cover)",
        PrinterState.Error => "Error",
        _ => queue.QueueStatus.ToString(),
    };

    private static Task<T> OnUiThread<T>(Func<T> action) =>
        System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess()
            ? dispatcher.InvokeAsync(action).Task
            : Task.FromResult(action());

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocName = string.Empty;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType = "RAW";
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenPrinter(string printerName, out IntPtr handle, IntPtr defaults);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int StartDocPrinter(IntPtr handle, int level, [In] DocInfo info);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WritePrinter(IntPtr handle, IntPtr bytes, int count, out int written);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndDocPrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClosePrinter(IntPtr handle);
}
