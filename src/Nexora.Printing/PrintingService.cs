using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Billing;
using Nexora.Application.Inventory;
using Nexora.Application.Security;
using Nexora.Application.Service;
using Nexora.Domain.Billing;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;
using Nexora.Domain.Printing;
using Nexora.Domain.Security;
using Nexora.Domain.Users;
using Nexora.Printing.EscPos;
using Nexora.Printing.Layout;
using Nexora.Printing.Pdf;
using Nexora.Printing.Templates;

namespace Nexora.Printing;

/// <summary>What to print.</summary>
public abstract record PrintSubject;

/// <summary>A sales invoice, credit note, purchase bill or debit note; or a delivery note or purchase order made from one.</summary>
public sealed record BillingSubject(Guid DocumentId, DocumentStyle Style = DocumentStyle.Standard) : PrintSubject;

public sealed record SettlementSubject(Guid SettlementId) : PrintSubject;

/// <summary>The job card of a service job (its invoice prints as a <see cref="BillingSubject"/>).</summary>
public sealed record ServiceJobSubject(Guid JobId) : PrintSubject;

public sealed record StatementSubject(Guid PartyId, DateOnly From, DateOnly To) : PrintSubject;

/// <summary>
/// A document laid out by another module (for example a report) with the page setup of the chosen printer preference.
/// The caller checks its own permissions before creating it.
/// </summary>
public sealed record RenderedSubject(string Title, string SourceType, Func<PageSetup, RenderedDocument> Render) : PrintSubject;

public sealed record LabelRequest(Guid ProductId, int Quantity);

public sealed record LabelSubject(Guid LayoutId, IReadOnlyList<LabelRequest> Items, int Skip = 0) : PrintSubject;

public sealed record PrinterProfileRow(PrintPurpose Purpose, string PrinterName, PaperKind Paper, PageOrientation Orientation, decimal MarginTopMm,
    decimal MarginRightMm, decimal MarginBottomMm, decimal MarginLeftMm, decimal ScalePercent, decimal FontSize, int Copies, bool UseEscPos);

public sealed record PrintSettingsRow(bool MarkReprintsDuplicate, string LabelQrFormat, string InvoiceQrFormat, string FooterNote);

public sealed record LabelLayoutRow(Guid Id, string Name, decimal PageWidthMm, decimal PageHeightMm, decimal LabelWidthMm, decimal LabelHeightMm, int Columns,
    int Rows, decimal MarginLeftMm, decimal MarginTopMm, decimal HorizontalGapMm, decimal VerticalGapMm, bool ShowName, bool ShowSku, bool ShowPrice,
    bool ShowMrp, LabelCode Code, bool IsActive)
{
    public string Summary => $"{Columns} × {Rows} of {LabelWidthMm:0.#} × {LabelHeightMm:0.#} mm on {PageWidthMm:0.#} × {PageHeightMm:0.#} mm";
}

public sealed record LabelLayoutInput(Guid? Id, string Name, decimal PageWidthMm, decimal PageHeightMm, decimal LabelWidthMm, decimal LabelHeightMm, int Columns,
    int Rows, decimal MarginLeftMm, decimal MarginTopMm, decimal HorizontalGapMm, decimal VerticalGapMm, bool ShowName, bool ShowSku, bool ShowPrice,
    bool ShowMrp, LabelCode Code);

public sealed record PrintRecordRow(DateTime PrintedUtc, string SourceType, Guid? SourceId, string DocumentNumber, PrintPurpose Purpose, PrintOutput Output,
    string PrinterName, int Copies, int PrintNumber, bool MarkedDuplicate, string PrintedBy)
{
    public bool IsReprint => PrintNumber > 1;
}

public sealed record PrinterTestRow(Guid Id, string PrinterName, PaperKind Paper, TestPrintOutcome Outcome, string Detail, string Notes, DateTime RequestedUtc,
    string RequestedBy, DateTime? ConfirmedUtc, string ConfirmedBy);

/// <summary>An installed printer with the last confirmed test result recorded in this company.</summary>
public sealed record PrinterRow(InstalledPrinter Printer, TestPrintOutcome? LastTest, DateTime? LastTestUtc);

public sealed record PrintResult(bool Succeeded, string Message, string Guidance, int PrintNumber, bool MarkedDuplicate, string PrinterName);

public sealed record PdfResult(byte[] Content, string FileName, int PrintNumber);

public sealed record TestPrintResult(PrinterTestRow Test, string Guidance);

/// <summary>
/// Print centre: preferences, label layouts, document rendering, printing through <see cref="IPrinterGateway"/>,
/// PDF export, reprint tracking and verified test prints. Rendering uses the stored figures of documents loaded
/// through the billing services, so the document view permissions apply; settings need printing.manage.
/// A print is recorded only after the printer layer accepted it; whether paper came out is only known from a person.
/// </summary>
public sealed class PrintingService(
    IUnitOfWorkFactory units,
    IAuthorizer authorizer,
    IClock clock,
    IPrinterGateway gateway,
    BillingService billing,
    SettlementService settlements,
    PartyService parties,
    InventoryCatalogueService catalogue,
    ServiceJobService service)
{
    private sealed record Prepared(RenderedDocument Document, string SourceType, Guid? SourceId, string Number, PrintPurpose Purpose, PrinterProfile Profile,
        int PrintNumber, bool Duplicate, byte[]? EscPos);

    // ----- Rendering, printing and PDF -----

    public static PrintPurpose DefaultPurpose(PrintSubject subject) => subject switch
    {
        SettlementSubject => PrintPurpose.Receipts,
        StatementSubject => PrintPurpose.Reports,
        RenderedSubject => PrintPurpose.Reports,
        LabelSubject => PrintPurpose.Labels,
        _ => PrintPurpose.Invoices,
    };

    /// <summary>The pages as the next print would produce them (including DUPLICATE when it would be a reprint). Nothing is recorded.</summary>
    public async Task<RenderedDocument> PreviewAsync(PrintSubject subject, PrintPurpose? purpose = null, CancellationToken cancellationToken = default) =>
        (await PrepareAsync(subject, purpose, cancellationToken)).Document;

    public async Task<PrintResult> PrintAsync(PrintSubject subject, PrintPurpose? purpose = null, int? copies = null, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(subject, purpose, cancellationToken);
        var count = copies ?? prepared.Profile.Copies;
        if (count is < 1 or > 20) throw new WorkspaceValidationException("Copies must be between 1 and 20.");
        var printer = prepared.Profile.PrinterName;
        PrintOutcome outcome;
        PrintOutput output;
        if (prepared.EscPos is { } raw)
        {
            output = PrintOutput.EscPos;
            outcome = PrintOutcome.Ok;
            for (var i = 0; i < count && outcome.Succeeded; i++)
                outcome = await gateway.SendRawAsync(printer, raw, prepared.Document.Title, cancellationToken);
        }
        else
        {
            output = PrintOutput.Printer;
            outcome = await gateway.PrintAsync(new PrintJob(prepared.Document, printer, count, prepared.Profile.Paper, prepared.Profile.Orientation), cancellationToken);
        }

        var shownPrinter = printer.Length > 0 ? printer : "the Windows default printer";
        if (!outcome.Succeeded)
            return new PrintResult(false, $"Could not print to {shownPrinter}." + (outcome.Detail.Length > 0 ? " " + outcome.Detail : string.Empty),
                PrinterGuidance.For(outcome.Failure), prepared.PrintNumber, prepared.Duplicate, printer);

        var number = await RecordAsync(prepared, output, printer, count, cancellationToken);
        return new PrintResult(true, $"Sent {prepared.Document.Title} to {shownPrinter}" + (count > 1 ? $" ({count} copies)" : string.Empty)
            + (number > 1 ? $". This is print {number} of this document." : "."), string.Empty, number, prepared.Duplicate, printer);
    }

    public async Task<PdfResult> ExportPdfAsync(PrintSubject subject, PrintPurpose? purpose = null, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(subject, purpose, cancellationToken);
        var bytes = PdfRenderer.Render(prepared.Document);
        var number = await RecordAsync(prepared, PrintOutput.Pdf, "PDF", 1, cancellationToken);
        return new PdfResult(bytes, FileName(prepared.Document.Title), number);
    }

    /// <summary>Raw ESC/POS bytes for a billing document (experimental), for tests and diagnostics.</summary>
    public async Task<byte[]> EscPosBytesAsync(Guid billingDocumentId, int columns, CancellationToken cancellationToken = default)
    {
        authorizer.RequireSession();
        var document = await billing.GetAsync(billingDocumentId, cancellationToken);
        var (company, settings) = await CompanyAsync(cancellationToken);
        var title = InvoiceTemplate.TitleOf(document, DocumentStyle.Standard, company.Gstin.Length > 0);
        return EscPosWriter.Receipt(document, company, title, Marks(settings, [], null), columns);
    }

    // ----- History -----

    /// <summary>Prints of one document, newest first. All prints of the company need printing.manage.</summary>
    public async Task<IReadOnlyList<PrintRecordRow>> HistoryAsync(string? sourceType = null, Guid? sourceId = null, int take = 500, CancellationToken cancellationToken = default)
    {
        if (sourceId is null) authorizer.Demand(Permissions.PrintingManage);
        else authorizer.RequireSession();
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var records = await unit.ListAsync<PrintRecord, DateTime>(r => (sourceType == null || r.SourceType == sourceType) && (sourceId == null || r.SourceId == sourceId),
            r => r.PrintedUtc, descending: true, 0, Math.Clamp(take, 1, 5000), cancellationToken);
        var names = await UserNamesAsync(unit, cancellationToken);
        return records.Select(r => new PrintRecordRow(r.PrintedUtc, r.SourceType, r.SourceId, r.DocumentNumber, r.Purpose, r.Output, r.PrinterName, r.Copies,
            r.PrintNumber, r.MarkedDuplicate, names.GetValueOrDefault(r.PrintedBy, "?"))).ToList();
    }

    // ----- Printers and test prints -----

    public async Task<IReadOnlyList<PrinterRow>> ListPrintersAsync(CancellationToken cancellationToken = default)
    {
        authorizer.RequireSession();
        var printers = await gateway.ListPrintersAsync(cancellationToken);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var tests = (await unit.ListAsync<PrinterTest>(t => t.Outcome == TestPrintOutcome.ConfirmedGood || t.Outcome == TestPrintOutcome.ReportedProblem, cancellationToken))
            .GroupBy(t => t.PrinterName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.MaxBy(t => t.ConfirmedUtc)!, StringComparer.OrdinalIgnoreCase);
        return printers.OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => tests.TryGetValue(p.Name, out var t) ? new PrinterRow(p, t.Outcome, t.ConfirmedUtc) : new PrinterRow(p, null, null))
            .ToList();
    }

    /// <summary>Prints the test page and records it as sent (or failed). It passes only when someone confirms it with <see cref="ConfirmTestAsync"/>.</summary>
    public async Task<TestPrintResult> StartTestAsync(string printerName, PaperKind paper, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.PrintingManage);
        var id = Guid.NewGuid();
        var code = "TP" + id.ToString("N")[..8].ToUpperInvariant();
        var setup = PageSetup.Default(paper);
        var page = TestPageTemplate.Render(printerName ?? string.Empty, code, clock.UtcNow.ToLocalTime(), setup, PrintFonts.Instance);
        var outcome = await gateway.PrintAsync(new PrintJob(page, printerName ?? string.Empty, 1, paper, PageOrientation.Portrait), cancellationToken);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var test = new PrinterTest(id, session.CompanyId, printerName ?? string.Empty, paper, outcome.Succeeded,
            outcome.Succeeded ? $"Windows accepted test {code}." : outcome.Detail, clock.UtcNow, session.UserId);
        unit.Add(test);
        unit.Add(new PrintRecord(Guid.NewGuid(), session.CompanyId, "test", id, code, PrintPurpose.Reports, PrintOutput.Printer, test.PrinterName, 1, 1, false,
            clock.UtcNow, session.UserId));
        await unit.CommitAsync(cancellationToken);
        var names = new Dictionary<Guid, string> { [session.UserId] = session.DisplayName };
        return new TestPrintResult(TestRow(test, names), outcome.Succeeded ? string.Empty : PrinterGuidance.For(outcome.Failure));
    }

    public async Task ConfirmTestAsync(Guid testId, bool cameOutCorrectly, string notes, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.PrintingManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var test = await unit.FindAsync<PrinterTest>(testId, cancellationToken) ?? throw new WorkspaceValidationException("That test print no longer exists.");
        Guard(() => test.Confirm(cameOutCorrectly, notes, clock.UtcNow, session.UserId));
        await unit.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PrinterTestRow>> TestsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.PrintingManage);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var tests = await unit.ListAsync<PrinterTest, DateTime>(null, t => t.RequestedUtc, descending: true, 0, 200, cancellationToken);
        var names = await UserNamesAsync(unit, cancellationToken);
        return tests.Select(t => TestRow(t, names)).ToList();
    }

    // ----- Preferences, settings and label layouts -----

    public async Task<IReadOnlyList<PrinterProfileRow>> ProfilesAsync(CancellationToken cancellationToken = default)
    {
        authorizer.RequireSession();
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        return (await unit.ListAsync<PrinterProfile>(cancellationToken: cancellationToken)).OrderBy(p => p.Purpose).Select(ProfileRow).ToList();
    }

    public async Task SaveProfileAsync(PrinterProfileRow input, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.PrintingManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var purpose = input.Purpose;
        var profile = (await unit.ListAsync<PrinterProfile>(p => p.Purpose == purpose, cancellationToken)).SingleOrDefault()
            ?? throw new WorkspaceValidationException("Printer preferences are missing. Sign out and in again to finish the upgrade.");
        Guard(() => profile.Update(input.PrinterName, input.Paper, input.Orientation, input.MarginTopMm, input.MarginRightMm, input.MarginBottomMm, input.MarginLeftMm,
            input.ScalePercent, input.FontSize, input.Copies, input.UseEscPos));
        Guard(() => SetupOf(profile).Validate());
        await unit.CommitAsync(cancellationToken);
    }

    public async Task<PrintSettingsRow> SettingsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.RequireSession();
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var s = await SettingsAsync(unit, cancellationToken);
        return new PrintSettingsRow(s.MarkReprintsDuplicate, s.LabelQrFormat, s.InvoiceQrFormat, s.FooterNote);
    }

    public async Task SaveSettingsAsync(PrintSettingsRow input, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.PrintingManage);
        if (Barcodes.QrContentFormat.Validate(input.LabelQrFormat, Barcodes.QrContentFormat.LabelFields) is { } labelError)
            throw new WorkspaceValidationException("Label QR format: " + labelError);
        if (Barcodes.QrContentFormat.Validate(input.InvoiceQrFormat, Barcodes.QrContentFormat.DocumentFields) is { } invoiceError)
            throw new WorkspaceValidationException("Invoice QR format: " + invoiceError);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var settings = await SettingsAsync(unit, cancellationToken);
        Guard(() => settings.Update(input.MarkReprintsDuplicate, input.LabelQrFormat, input.InvoiceQrFormat, input.FooterNote));
        await unit.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LabelLayoutRow>> LabelLayoutsAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        authorizer.RequireSession();
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        return (await unit.ListAsync<LabelLayout>(l => includeInactive || l.IsActive, cancellationToken))
            .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).Select(LayoutRow).ToList();
    }

    public async Task<Guid> SaveLabelLayoutAsync(LabelLayoutInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.PrintingManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var name = (input.Name ?? string.Empty).Trim();
        var selfId = input.Id ?? Guid.Empty;
        if (await unit.AnyAsync<LabelLayout>(l => l.Name == name && l.Id != selfId, cancellationToken))
            throw new WorkspaceValidationException($"A label layout named \"{name}\" already exists.");
        LabelLayout layout;
        if (input.Id is { } id)
        {
            layout = await unit.FindAsync<LabelLayout>(id, cancellationToken) ?? throw new WorkspaceValidationException("That label layout no longer exists.");
        }
        else
        {
            layout = new LabelLayout(Guid.NewGuid(), session.CompanyId, name);
            unit.Add(layout);
        }

        Guard(() => layout.Update(name, input.PageWidthMm, input.PageHeightMm, input.LabelWidthMm, input.LabelHeightMm, input.Columns, input.Rows, input.MarginLeftMm,
            input.MarginTopMm, input.HorizontalGapMm, input.VerticalGapMm, input.ShowName, input.ShowSku, input.ShowPrice, input.ShowMrp, input.Code));
        await unit.CommitAsync(cancellationToken);
        return layout.Id;
    }

    public async Task SetLabelLayoutActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.PrintingManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var layout = await unit.FindAsync<LabelLayout>(id, cancellationToken) ?? throw new WorkspaceValidationException("That label layout no longer exists.");
        layout.SetActive(active);
        await unit.CommitAsync(cancellationToken);
    }

    // ----- Preparation -----

    private async Task<Prepared> PrepareAsync(PrintSubject subject, PrintPurpose? requestedPurpose, CancellationToken cancellationToken)
    {
        authorizer.RequireSession();
        var purpose = requestedPurpose ?? DefaultPurpose(subject);
        var (company, settings) = await CompanyAsync(cancellationToken);
        PrinterProfile profile;
        await using (var unit = await units.BeginAsync(readOnly: true, cancellationToken))
        {
            profile = (await unit.ListAsync<PrinterProfile>(p => p.Purpose == purpose, cancellationToken)).SingleOrDefault()
                ?? throw new WorkspaceValidationException("Printer preferences are missing. Sign out and in again to finish the upgrade.");
        }

        var setup = SetupOf(profile);
        switch (subject)
        {
            case BillingSubject b:
            {
                var d = await billing.GetAsync(b.DocumentId, cancellationToken);
                var party = await PartyAsync(d, cancellationToken);
                var (printNumber, duplicate) = await PrintNumberAsync("billing", d.Id, settings, cancellationToken);
                var banners = new List<string>();
                if (duplicate) banners.Add("DUPLICATE");
                if (d.IsCancelled) banners.Add("CANCELLED");
                if (d.State == BillingStatus.Draft && b.Style != DocumentStyle.PurchaseOrder) banners.Add("DRAFT · NOT A VALID DOCUMENT");
                var qr = b.Style == DocumentStyle.Standard && d.Kind == BillingDocumentKind.SalesInvoice && d.State == BillingStatus.Posted
                    && settings.InvoiceQrFormat.Length > 0
                        ? Barcodes.QrContentFormat.Apply(settings.InvoiceQrFormat, new Dictionary<string, string>
                        {
                            ["number"] = d.Number ?? string.Empty, ["date"] = Fmt.Date(d.Date), ["total"] = Barcodes.QrContentFormat.Money(d.Total),
                            ["gstin"] = company.Gstin, ["party"] = d.PartyName, ["company"] = company.Name,
                        })
                        : null;
                var marks = Marks(settings, banners, qr);
                var document = Guard(() => InvoiceTemplate.Render(d, company, party, b.Style, marks, setup, PrintFonts.Instance));
                byte[]? escPos = null;
                if (profile.UseEscPos && purpose == PrintPurpose.Receipts && b.Style == DocumentStyle.Standard)
                    escPos = EscPosWriter.Receipt(d, company, InvoiceTemplate.TitleOf(d, b.Style, company.Gstin.Length > 0), marks, profile.Paper == PaperKind.Thermal58 ? 32 : 48);
                var number = (b.Style switch { DocumentStyle.DeliveryNote => "Delivery note ", DocumentStyle.PurchaseOrder => "Purchase order ", _ => string.Empty })
                    + (d.Number ?? "draft");
                return new Prepared(document, "billing", d.Id, number, purpose, profile, printNumber, duplicate, escPos);
            }

            case SettlementSubject s:
            {
                var view = await settlements.GetAsync(s.SettlementId, cancellationToken);
                var (printNumber, duplicate) = await PrintNumberAsync("settlement", view.Id, settings, cancellationToken);
                var document = ReceiptTemplate.Render(view, company, Marks(settings, duplicate ? ["DUPLICATE"] : [], null), setup, PrintFonts.Instance);
                return new Prepared(document, "settlement", view.Id, view.Number, purpose, profile, printNumber, duplicate, null);
            }

            case ServiceJobSubject sj:
            {
                var job = await service.GetAsync(sj.JobId, cancellationToken);
                var (printNumber, duplicate) = await PrintNumberAsync("service", job.Id, settings, cancellationToken);
                PrintParty customer;
                await using (var unit = await units.BeginAsync(readOnly: true, cancellationToken))
                {
                    var p = await unit.FindAsync<Party>(job.CustomerId, cancellationToken);
                    customer = new PrintParty(job.Customer, p is null ? [] : new[] { p.Address, p.City }.Where(x => x.Length > 0).ToList(), p?.Gstin ?? string.Empty,
                        IndianState.FromCode(p?.StateCode)?.Name ?? string.Empty, job.CustomerPhone);
                }

                var document = JobCardTemplate.Render(job, company, customer, Marks(settings, duplicate ? ["DUPLICATE"] : [], null), setup, PrintFonts.Instance);
                return new Prepared(document, "service", job.Id, $"Job card {job.Number}", purpose, profile, printNumber, duplicate, null);
            }

            case StatementSubject st:
            {
                var statement = await parties.StatementAsync(st.PartyId, st.From, st.To, cancellationToken);
                var document = StatementTemplate.Render(statement, company, setup, PrintFonts.Instance);
                var (printNumber, _) = await PrintNumberAsync("statement", st.PartyId, settings, cancellationToken);
                return new Prepared(document, "statement", st.PartyId, $"Statement {Fmt.Date(st.From)} to {Fmt.Date(st.To)}", purpose, profile, printNumber, false, null);
            }

            case RenderedSubject r:
                return new Prepared(r.Render(setup), r.SourceType, null, r.Title, purpose, profile, 1, false, null);

            case LabelSubject l:
            {
                var layout = await LayoutAsync(l.LayoutId, cancellationToken);
                var products = (await catalogue.ListProductsAsync(cancellationToken: cancellationToken)).ToDictionary(p => p.Id);
                var items = new List<(LabelData, int)>();
                foreach (var request in l.Items.Where(i => i.Quantity != 0))
                {
                    if (!products.TryGetValue(request.ProductId, out var p)) throw new WorkspaceValidationException("A chosen product no longer exists or is archived.");
                    var values = new Dictionary<string, string>
                    {
                        ["sku"] = p.Sku, ["name"] = p.DisplayName, ["barcode"] = p.Barcode ?? string.Empty,
                        ["price"] = Barcodes.QrContentFormat.Money(p.SellingPrice), ["mrp"] = Barcodes.QrContentFormat.Money(p.Mrp),
                    };
                    var qr = settings.LabelQrFormat.Length > 0 ? Barcodes.QrContentFormat.Apply(settings.LabelQrFormat, values) : string.Empty;
                    items.Add((new LabelData(p.DisplayName, p.Sku, p.SellingPrice, p.Mrp, p.Barcode ?? p.QrCode, qr.Length > 0 ? qr : p.QrCode ?? p.Sku), request.Quantity));
                }

                var document = Guard(() => LabelTemplate.Render(LabelGeometry.From(layout), items, l.Skip, PrintFonts.Instance));
                return new Prepared(document, "labels", null, $"{layout.Name}: {items.Sum(i => i.Item2)} labels", purpose, profile, 1, false, null);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(subject));
        }
    }

    private async Task<int> RecordAsync(Prepared prepared, PrintOutput output, string printer, int copies, CancellationToken cancellationToken)
    {
        var session = authorizer.RequireSession();
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var sourceType = prepared.SourceType;
        var sourceId = prepared.SourceId;
        // Counted again inside the transaction, so two people printing at once still get distinct numbers.
        var previous = sourceId is null ? 0 : (await unit.ListAsync<PrintRecord>(r => r.SourceType == sourceType && r.SourceId == sourceId, cancellationToken)).Count;
        var number = previous + 1;
        unit.Add(new PrintRecord(Guid.NewGuid(), session.CompanyId, sourceType, sourceId, prepared.Number, prepared.Purpose, output, printer, copies, number,
            prepared.Duplicate, clock.UtcNow, session.UserId));
        await unit.CommitAsync(cancellationToken);
        return number;
    }

    private async Task<(int Number, bool Duplicate)> PrintNumberAsync(string sourceType, Guid sourceId, PrintSettings settings, CancellationToken cancellationToken)
    {
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var previous = (await unit.ListAsync<PrintRecord>(r => r.SourceType == sourceType && r.SourceId == sourceId, cancellationToken)).Count;
        return (previous + 1, previous > 0 && settings.MarkReprintsDuplicate && sourceType != "statement");
    }

    private async Task<(PrintCompany Company, PrintSettings Settings)> CompanyAsync(CancellationToken cancellationToken)
    {
        var session = authorizer.RequireSession();
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var c = await unit.FindAsync<Company>(session.CompanyId, cancellationToken) ?? throw new WorkspaceValidationException("Sign in again.");
        var settings = await SettingsAsync(unit, cancellationToken);
        var cityLine = string.Join(" ", new[] { c.City, c.Pincode }.Where(s => s.Length > 0));
        var company = new PrintCompany(c.LegalName.Length > 0 ? c.LegalName : c.Name, new[] { c.AddressLine1, c.AddressLine2, cityLine }.Where(s => s.Length > 0).ToList(),
            c.Gstin, c.StateCode, IndianState.FromCode(c.StateCode)?.Name ?? string.Empty, c.Phone, c.Email, c.Logo);
        return (company, settings);
    }

    private async Task<PrintParty> PartyAsync(BillingDocumentView d, CancellationToken cancellationToken)
    {
        if (d.PartyId is not { } partyId) return new PrintParty(d.PartyName, [], d.PartyGstin, string.Empty, string.Empty);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var p = await unit.FindAsync<Party>(partyId, cancellationToken);
        if (p is null) return new PrintParty(d.PartyName, [], d.PartyGstin, d.PlaceOfSupplyName, string.Empty);
        var cityLine = string.Join(" ", new[] { p.City, p.Pincode }.Where(s => s.Length > 0));
        return new PrintParty(d.PartyName, new[] { p.Address, cityLine }.Where(s => s.Length > 0).ToList(), d.PartyGstin,
            IndianState.FromCode(p.StateCode)?.Name ?? string.Empty, p.Phone);
    }

    private async Task<LabelLayout> LayoutAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var layout = await unit.FindAsync<LabelLayout>(id, cancellationToken) ?? throw new WorkspaceValidationException("Choose a label layout.");
        if (!layout.IsActive) throw new WorkspaceValidationException($"Label layout {layout.Name} is inactive.");
        return layout;
    }

    private static async Task<PrintSettings> SettingsAsync(IUnitOfWork unit, CancellationToken cancellationToken) =>
        (await unit.ListAsync<PrintSettings>(cancellationToken: cancellationToken)).SingleOrDefault()
        ?? throw new WorkspaceValidationException("Print settings are missing. Sign out and in again to finish the upgrade.");

    private static async Task<Dictionary<Guid, string>> UserNamesAsync(IUnitOfWork unit, CancellationToken cancellationToken) =>
        (await unit.ListAsync<User>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id, u => u.DisplayName);

    private static PrintMarks Marks(PrintSettings settings, IReadOnlyList<string> banners, string? qr) => new(banners, qr, settings.FooterNote);

    public static PageSetup SetupOf(PrinterProfile p) => new(p.Paper, p.Orientation,
        new Margins((double)p.MarginTopMm, (double)p.MarginRightMm, (double)p.MarginBottomMm, (double)p.MarginLeftMm), (double)p.ScalePercent, (double)p.FontSize);

    private static PrinterProfileRow ProfileRow(PrinterProfile p) => new(p.Purpose, p.PrinterName, p.Paper, p.Orientation, p.MarginTopMm, p.MarginRightMm,
        p.MarginBottomMm, p.MarginLeftMm, p.ScalePercent, p.FontSize, p.Copies, p.UseEscPos);

    private static LabelLayoutRow LayoutRow(LabelLayout l) => new(l.Id, l.Name, l.PageWidthMm, l.PageHeightMm, l.LabelWidthMm, l.LabelHeightMm, l.Columns, l.Rows,
        l.MarginLeftMm, l.MarginTopMm, l.HorizontalGapMm, l.VerticalGapMm, l.ShowName, l.ShowSku, l.ShowPrice, l.ShowMrp, l.Code, l.IsActive);

    private static PrinterTestRow TestRow(PrinterTest t, IReadOnlyDictionary<Guid, string> names) => new(t.Id, t.PrinterName, t.Paper, t.Outcome, t.Detail, t.Notes,
        t.RequestedUtc, names.GetValueOrDefault(t.RequestedBy, "?"), t.ConfirmedUtc, t.ConfirmedBy is { } c ? names.GetValueOrDefault(c, "?") : string.Empty);

    private static string FileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        var clean = new string(title.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return (clean.Length > 0 ? clean : "document") + ".pdf";
    }

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is DomainException or ArgumentException)
        {
            throw new WorkspaceValidationException(ex.Message);
        }
    }

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is DomainException or ArgumentException)
        {
            throw new WorkspaceValidationException(ex.Message);
        }
    }
}
