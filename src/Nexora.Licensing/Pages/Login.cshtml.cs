using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Nexora.Licensing.Pages;

public sealed class LoginModel(AdminCredentials credentials) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public async Task<IActionResult> OnPostAsync()
    {
        if (!credentials.Verify(Username, Password))
        {
            Password = "";
            ModelState.Remove(nameof(Password));
            ModelState.AddModelError("", "Sign-in failed. Check your details. After five failures, wait 15 minutes.");
            return Page();
        }
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, credentials.Username),
                new Claim("credential-version", credentials.Version) }, CookieAuthenticationDefaults.AuthenticationScheme)));
        return RedirectToPage("/Index");
    }
    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync();
        return RedirectToPage("/Login");
    }
}
