using Nexora.Domain.Companies;
using Nexora.Domain.Numbering;

namespace Nexora.Application.Abstractions;

/// <summary>
/// Takes the next number for a document type inside the caller's transaction. The counter row is updated in the
/// same transaction as the document, so numbers are unique and gapless per type and period.
/// </summary>
public static class NumberAllocator
{
    public static async Task<string> NextAsync(IUnitOfWork unit, Guid companyId, string documentType, FinancialYear year,
        Func<string, Task<bool>> isTaken, CancellationToken cancellationToken)
    {
        var rule = (await unit.ListAsync<NumberingRule>(r => r.DocumentType == documentType, cancellationToken)).SingleOrDefault()
            ?? throw new WorkspaceValidationException("Numbering for this document type is missing. Check Settings → Document numbering.");
        var period = rule.PeriodFor(year);
        var counter = (await unit.ListAsync<NumberCounter>(c => c.DocumentType == documentType && c.Period == period, cancellationToken)).SingleOrDefault();
        if (counter is null)
        {
            counter = new NumberCounter(Guid.NewGuid(), companyId, documentType, period);
            unit.Add(counter);
        }

        // Skip numbers already used, for example after a prefix was changed back to an earlier pattern.
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var candidate = rule.Format(counter.Take(), year);
            if (!await isTaken(candidate)) return candidate;
        }

        throw new WorkspaceValidationException("No free document number was found. Check the numbering settings.");
    }
}
