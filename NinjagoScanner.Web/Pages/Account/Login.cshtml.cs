using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using NinjagoScanner.Web.Data;

namespace NinjagoScanner.Web.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("login")]
public class LoginModel : PageModel
{
    private readonly SignInManager<AppUser> _signInManager;

    public LoginModel(SignInManager<AppUser> signInManager)
    {
        _signInManager = signInManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Benutzername ist erforderlich.")]
        [Display(Name = "Benutzername")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Passwort ist erforderlich.")]
        [DataType(DataType.Password)]
        [Display(Name = "Passwort")]
        public string Password { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var result = await _signInManager.PasswordSignInAsync(
            Input.UserName, Input.Password, isPersistent: true, lockoutOnFailure: true);

        if (result.Succeeded)
            return LocalRedirect("/");

        if (result.IsLockedOut)
        {
            ErrorMessage = "Dein Konto wurde vorübergehend gesperrt. Bitte versuche es später erneut.";
            return Page();
        }

        ErrorMessage = "Anmeldung fehlgeschlagen. Bitte überprüfe Benutzername und Passwort.";
        return Page();
    }
}
