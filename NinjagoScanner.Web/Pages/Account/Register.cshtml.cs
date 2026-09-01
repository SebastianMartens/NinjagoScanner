using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using NinjagoScanner.Web.Data;

namespace NinjagoScanner.Web.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("registration")]
public class RegisterModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;

    public RegisterModel(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<string> Errors { get; set; } = [];

    public class InputModel
    {
        [Required(ErrorMessage = "Benutzername ist erforderlich.")]
        [Display(Name = "Benutzername")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Passwort ist erforderlich.")]
        [DataType(DataType.Password)]
        [Display(Name = "Passwort")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Passwortbestätigung ist erforderlich.")]
        [DataType(DataType.Password)]
        [Display(Name = "Passwort bestätigen")]
        [Compare("Password", ErrorMessage = "Die Passwörter stimmen nicht überein.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var user = new AppUser { UserName = Input.UserName };
        var result = await _userManager.CreateAsync(user, Input.Password);

        if (result.Succeeded)
            return RedirectToPage("/Account/Login");

        foreach (var error in result.Errors)
        {
            Errors.Add(TranslateIdentityError(error));
        }

        return Page();
    }

    private static string TranslateIdentityError(IdentityError error) => error.Code switch
    {
        "DuplicateUserName" => "Dieser Benutzername ist bereits vergeben.",
        "PasswordTooShort" => "Das Passwort muss mindestens 6 Zeichen lang sein.",
        "PasswordRequiresDigit" => "Das Passwort muss mindestens eine Ziffer enthalten.",
        "PasswordRequiresNonAlphanumeric" => "Das Passwort muss mindestens ein Sonderzeichen enthalten.",
        "PasswordRequiresUpper" => "Das Passwort muss mindestens einen Großbuchstaben enthalten.",
        "PasswordRequiresLower" => "Das Passwort muss mindestens einen Kleinbuchstaben enthalten.",
        "InvalidUserName" => "Der Benutzername enthält ungültige Zeichen.",
        _ => error.Description,
    };
}
