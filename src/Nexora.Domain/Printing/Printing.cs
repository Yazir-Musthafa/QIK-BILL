using Nexora.Domain.Common;

namespace Nexora.Domain.Printing;

/// <summary>What a printer preference is for. Each company has one preference per purpose.</summary>
public enum PrintPurpose
{
    Invoices,
    Receipts,
    Reports,
    Labels,
}

public enum PaperKind
{
    A4,
    A5,
    Thermal58,
    Thermal80,
}

public enum PageOrientation
{
    Portrait,
    Landscape,
}

/// <summary>Where a print went.</summary>
public enum PrintOutput
{
    Printer,
    Pdf,

    /// <summary>Raw ESC/POS bytes sent to a receipt printer (experimental).</summary>
    EscPos,
}

public enum TestPrintOutcome
{
    /// <summary>Windows accepted the job. Nobody has confirmed the paper yet.</summary>
    Sent,

    /// <summary>The job could not be sent (the printer was missing, offline or refused it).</summary>
    SendFailed,

    /// <summary>The user confirmed the page came out correctly.</summary>
    ConfirmedGood,

    /// <summary>The user reported a problem with the printed page.</summary>
    ReportedProblem,
}

public enum LabelCode
{
    None,

    /// <summary>The product barcode: EAN-13 when valid, otherwise Code 128; the SKU when the product has no barcode.</summary>
    Barcode,

    /// <summary>A QR code built from the company's label QR format.</summary>
    Qr,
}

/// <summary>
/// Printer, paper and layout preference for one purpose in one company. An empty printer name means the Windows
/// default printer. Printer names are as Windows reports them; they are not checked here because printers come and go.
/// </summary>
public sealed class PrinterProfile : ICompanyOwned, IAudited
{
    private PrinterProfile()
    {
        PrinterName = string.Empty;
    }

    public PrinterProfile(Guid id, Guid companyId, PrintPurpose purpose, PaperKind paper) : this()
    {
        Id = id;
        CompanyId = companyId;
        Purpose = purpose;
        Paper = paper;
        Orientation = PageOrientation.Portrait;
        var thermal = paper is PaperKind.Thermal58 or PaperKind.Thermal80;
        (MarginTopMm, MarginRightMm, MarginBottomMm, MarginLeftMm) = thermal ? (3m, 3m, 3m, 3m) : (12m, 12m, 12m, 12m);
        ScalePercent = 100;
        FontSize = thermal ? 8m : 9m;
        Copies = 1;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public PrintPurpose Purpose { get; private set; }

    public string PrinterName { get; private set; }

    public PaperKind Paper { get; private set; }

    public PageOrientation Orientation { get; private set; }

    public decimal MarginTopMm { get; private set; }

    public decimal MarginRightMm { get; private set; }

    public decimal MarginBottomMm { get; private set; }

    public decimal MarginLeftMm { get; private set; }

    public decimal ScalePercent { get; private set; }

    public decimal FontSize { get; private set; }

    public int Copies { get; private set; }

    /// <summary>Experimental: send receipts to the printer as raw ESC/POS commands instead of through the Windows driver.</summary>
    public bool UseEscPos { get; private set; }

    public void Update(string printerName, PaperKind paper, PageOrientation orientation, decimal top, decimal right, decimal bottom, decimal left,
        decimal scalePercent, decimal fontSize, int copies, bool useEscPos)
    {
        var name = (printerName ?? string.Empty).Trim();
        if (name.Length > 200) throw new DomainException("The printer name can have up to 200 characters.");
        if (new[] { top, right, bottom, left }.Any(m => m is < 0 or > 50)) throw new DomainException("Margins must be between 0 and 50 mm.");
        if (scalePercent is < 25 or > 200) throw new DomainException("Scale must be between 25% and 200%.");
        if (fontSize is < 6 or > 16) throw new DomainException("Font size must be between 6 and 16 points.");
        if (copies is < 1 or > 20) throw new DomainException("Copies must be between 1 and 20.");
        var thermal = paper is PaperKind.Thermal58 or PaperKind.Thermal80;
        if (useEscPos && (!thermal || Purpose != PrintPurpose.Receipts))
            throw new DomainException("ESC/POS (experimental) is only for receipts on 58 mm or 80 mm thermal paper.");
        if (useEscPos && name.Length == 0) throw new DomainException("Choose the receipt printer by name to use ESC/POS.");
        PrinterName = name;
        Paper = paper;
        Orientation = thermal ? PageOrientation.Portrait : orientation;
        (MarginTopMm, MarginRightMm, MarginBottomMm, MarginLeftMm) = (top, right, bottom, left);
        ScalePercent = scalePercent;
        FontSize = fontSize;
        Copies = copies;
        UseEscPos = useEscPos;
    }
}

/// <summary>Company-wide print options. Also the seed marker for printing on upgrade.</summary>
public sealed class PrintSettings : ICompanyOwned, IAudited
{
    private PrintSettings()
    {
        LabelQrFormat = InvoiceQrFormat = FooterNote = string.Empty;
    }

    public PrintSettings(Guid id, Guid companyId) : this()
    {
        Id = id;
        CompanyId = companyId;
        MarkReprintsDuplicate = true;
        LabelQrFormat = "{sku}";
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Second and later prints of the same document carry "DUPLICATE".</summary>
    public bool MarkReprintsDuplicate { get; private set; }

    /// <summary>Content of label QR codes (see QrContentFormat in Nexora.Printing).</summary>
    public string LabelQrFormat { get; private set; }

    /// <summary>Content of an optional reference QR on invoices; empty prints no QR.</summary>
    public string InvoiceQrFormat { get; private set; }

    /// <summary>A short line printed at the foot of invoices and receipts, for example bank details or terms.</summary>
    public string FooterNote { get; private set; }

    /// <summary>The caller validates the QR formats (the format rules live with the encoder).</summary>
    public void Update(bool markReprintsDuplicate, string labelQrFormat, string invoiceQrFormat, string footerNote)
    {
        var footer = (footerNote ?? string.Empty).Trim();
        if (footer.Length > 300) throw new DomainException("The footer note can have up to 300 characters.");
        MarkReprintsDuplicate = markReprintsDuplicate;
        LabelQrFormat = (labelQrFormat ?? string.Empty).Trim();
        InvoiceQrFormat = (invoiceQrFormat ?? string.Empty).Trim();
        FooterNote = footer;
    }
}

/// <summary>
/// A label layout: the page (a sheet of labels, or one label on a roll), the label grid and what each label shows.
/// All sizes are in millimetres.
/// </summary>
public sealed class LabelLayout : ICompanyOwned, IAudited
{
    private LabelLayout()
    {
        Name = string.Empty;
    }

    public LabelLayout(Guid id, Guid companyId, string name) : this()
    {
        Id = id;
        CompanyId = companyId;
        Name = name;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; }

    public decimal PageWidthMm { get; private set; }

    public decimal PageHeightMm { get; private set; }

    public decimal LabelWidthMm { get; private set; }

    public decimal LabelHeightMm { get; private set; }

    public int Columns { get; private set; }

    public int Rows { get; private set; }

    public decimal MarginLeftMm { get; private set; }

    public decimal MarginTopMm { get; private set; }

    public decimal HorizontalGapMm { get; private set; }

    public decimal VerticalGapMm { get; private set; }

    public bool ShowName { get; private set; }

    public bool ShowSku { get; private set; }

    public bool ShowPrice { get; private set; }

    public bool ShowMrp { get; private set; }

    public LabelCode Code { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, decimal pageWidth, decimal pageHeight, decimal labelWidth, decimal labelHeight, int columns, int rows,
        decimal marginLeft, decimal marginTop, decimal horizontalGap, decimal verticalGap, bool showName, bool showSku, bool showPrice, bool showMrp,
        LabelCode code)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length is 0 or > 60) throw new DomainException("Enter a layout name of up to 60 characters.");
        if (pageWidth is < 10 or > 500 || pageHeight is < 10 or > 500) throw new DomainException("Page width and height must be between 10 and 500 mm.");
        if (labelWidth is < 10 or > 500 || labelHeight is < 5 or > 500) throw new DomainException("Labels must be at least 10 × 5 mm.");
        if (columns is < 1 or > 20 || rows is < 1 or > 40) throw new DomainException("Use 1 to 20 columns and 1 to 40 rows.");
        if (new[] { marginLeft, marginTop, horizontalGap, verticalGap }.Any(v => v is < 0 or > 100)) throw new DomainException("Margins and gaps must be between 0 and 100 mm.");
        if (marginLeft + columns * labelWidth + (columns - 1) * horizontalGap > pageWidth)
            throw new DomainException("The labels do not fit across the page: left margin + columns × width + gaps is wider than the page.");
        if (marginTop + rows * labelHeight + (rows - 1) * verticalGap > pageHeight)
            throw new DomainException("The labels do not fit down the page: top margin + rows × height + gaps is taller than the page.");
        if (!showName && !showSku && !showPrice && !showMrp && code == LabelCode.None) throw new DomainException("Choose at least one thing to print on the label.");
        Name = clean;
        (PageWidthMm, PageHeightMm, LabelWidthMm, LabelHeightMm) = (pageWidth, pageHeight, labelWidth, labelHeight);
        (Columns, Rows, MarginLeftMm, MarginTopMm, HorizontalGapMm, VerticalGapMm) = (columns, rows, marginLeft, marginTop, horizontalGap, verticalGap);
        (ShowName, ShowSku, ShowPrice, ShowMrp, Code) = (showName, showSku, showPrice, showMrp, code);
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>
/// One print or PDF export of a document: who, when, where, how many copies, and whether it was a reprint.
/// Append-only. <see cref="PrintNumber"/> 1 is the first print of the document; 2 and up are reprints.
/// </summary>
public sealed class PrintRecord : ICompanyOwned
{
    private PrintRecord()
    {
        SourceType = DocumentNumber = PrinterName = string.Empty;
    }

    public PrintRecord(Guid id, Guid companyId, string sourceType, Guid? sourceId, string documentNumber, PrintPurpose purpose, PrintOutput output,
        string printerName, int copies, int printNumber, bool markedDuplicate, DateTime printedUtc, Guid printedBy) : this()
    {
        if (copies < 1) throw new DomainException("Copies must be at least 1.");
        if (printNumber < 1) throw new DomainException("Print numbers start at 1.");
        Id = id;
        CompanyId = companyId;
        SourceType = sourceType;
        SourceId = sourceId;
        DocumentNumber = (documentNumber ?? string.Empty).Trim();
        Purpose = purpose;
        Output = output;
        PrinterName = (printerName ?? string.Empty).Trim();
        Copies = copies;
        PrintNumber = printNumber;
        MarkedDuplicate = markedDuplicate;
        PrintedUtc = DateTime.SpecifyKind(printedUtc, DateTimeKind.Utc);
        PrintedBy = printedBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>"billing", "settlement", "statement", "labels" or "test".</summary>
    public string SourceType { get; private set; }

    public Guid? SourceId { get; private set; }

    public string DocumentNumber { get; private set; }

    public PrintPurpose Purpose { get; private set; }

    public PrintOutput Output { get; private set; }

    public string PrinterName { get; private set; }

    public int Copies { get; private set; }

    public int PrintNumber { get; private set; }

    public bool MarkedDuplicate { get; private set; }

    public DateTime PrintedUtc { get; private set; }

    public Guid PrintedBy { get; private set; }
}

/// <summary>
/// A test page sent to a printer and the user's confirmation of the result. A test counts as passed only when a
/// person confirms the paper, never because Windows accepted the job.
/// </summary>
public sealed class PrinterTest : ICompanyOwned, IAudited
{
    private PrinterTest()
    {
        PrinterName = Detail = Notes = string.Empty;
    }

    public PrinterTest(Guid id, Guid companyId, string printerName, PaperKind paper, bool sent, string detail, DateTime requestedUtc, Guid requestedBy) : this()
    {
        Id = id;
        CompanyId = companyId;
        PrinterName = (printerName ?? string.Empty).Trim();
        Paper = paper;
        Outcome = sent ? TestPrintOutcome.Sent : TestPrintOutcome.SendFailed;
        Detail = Clip(detail, 500);
        RequestedUtc = DateTime.SpecifyKind(requestedUtc, DateTimeKind.Utc);
        RequestedBy = requestedBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string PrinterName { get; private set; }

    public PaperKind Paper { get; private set; }

    public TestPrintOutcome Outcome { get; private set; }

    /// <summary>What the printer layer reported (for example the Windows error).</summary>
    public string Detail { get; private set; }

    /// <summary>What the user wrote when confirming, for example "right margin cut off".</summary>
    public string Notes { get; private set; }

    public DateTime RequestedUtc { get; private set; }

    public Guid RequestedBy { get; private set; }

    public DateTime? ConfirmedUtc { get; private set; }

    public Guid? ConfirmedBy { get; private set; }

    public void Confirm(bool cameOutCorrectly, string notes, DateTime nowUtc, Guid userId)
    {
        if (Outcome != TestPrintOutcome.Sent)
            throw new DomainException(Outcome == TestPrintOutcome.SendFailed ? "This test page was never sent, so there is nothing to confirm." : "This test page has already been confirmed.");
        var text = (notes ?? string.Empty).Trim();
        if (!cameOutCorrectly && text.Length == 0) throw new DomainException("Describe what was wrong with the test page.");
        Outcome = cameOutCorrectly ? TestPrintOutcome.ConfirmedGood : TestPrintOutcome.ReportedProblem;
        Notes = Clip(text, 300);
        ConfirmedUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        ConfirmedBy = userId;
    }

    private static string Clip(string? text, int length)
    {
        var clean = (text ?? string.Empty).Trim();
        return clean.Length <= length ? clean : clean[..length];
    }
}
