using Nexora.Printing;

namespace Nexora.Tests.Printing;

/// <summary>A printer layer for tests: records every job and fails on demand. It never claims paper came out.</summary>
public sealed class FakePrinterGateway : IPrinterGateway
{
    public List<PrintJob> Jobs { get; } = [];

    public List<(string Printer, byte[] Data)> RawJobs { get; } = [];

    /// <summary>When set, the next print fails with this outcome.</summary>
    public PrintOutcome? NextFailure { get; set; }

    public List<InstalledPrinter> Printers { get; } =
    [
        new("Office LaserJet", true, PrinterState.Ready, "Ready", ["A4", "A5", "Letter"], false, true, "Accounts room", true),
        new("POS-80 Thermal", false, PrinterState.Ready, "Ready", [], null, null, "Counter", false),
        new("Label Printer", false, PrinterState.Offline, "Offline", [], null, null, string.Empty, false),
    ];

    public Task<IReadOnlyList<InstalledPrinter>> ListPrintersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InstalledPrinter>>(Printers);

    public Task<PrintOutcome> PrintAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        if (TakeFailure() is { } failure) return Task.FromResult(failure);
        Jobs.Add(job);
        return Task.FromResult(PrintOutcome.Ok);
    }

    public Task<PrintOutcome> SendRawAsync(string printerName, byte[] data, string jobName, CancellationToken cancellationToken = default)
    {
        if (TakeFailure() is { } failure) return Task.FromResult(failure);
        RawJobs.Add((printerName, data));
        return Task.FromResult(PrintOutcome.Ok);
    }

    private PrintOutcome? TakeFailure()
    {
        var failure = NextFailure;
        NextFailure = null;
        return failure;
    }
}
