using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Nexora.Application;
using Nexora.Application.Licensing;

namespace Nexora.Licensing.Pages;

public sealed class IndexModel(LicenseAdministrationService service) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Selected { get; set; }
    [BindProperty] public string Action { get; set; } = "Activate";
    [BindProperty] public string Note { get; set; } = "";
    [TempData] public string? Notice { get; set; }
    public IReadOnlyList<LicenseAccountSummary> Accounts { get; private set; } = [];
    public LicenseAccountSummary? SelectedAccount => Accounts.FirstOrDefault(x => x.Id == Selected);
    public IReadOnlyList<LicenseHistory> History { get; private set; } = [];
    public async Task OnGetAsync()
    {
        Accounts = await service.ListAsync(Search, HttpContext.RequestAborted);
        if (Selected.HasValue) History = await service.HistoryAsync(Selected.Value, HttpContext.RequestAborted);
    }
    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            if (!Selected.HasValue) throw new WorkspaceValidationException("Choose an account.");
            await service.ChangeAsync(Selected.Value, Action, Note, HttpContext.RequestAborted);
            Notice = "Account updated. Existing offline access ends at its original expiry; the next online check uses the new status.";
            return RedirectToPage(new { Selected, Search });
        }
        catch (WorkspaceValidationException ex) { ModelState.AddModelError("", ex.Message); await OnGetAsync(); return Page(); }
    }
}
