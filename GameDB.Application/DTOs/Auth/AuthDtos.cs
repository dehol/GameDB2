using System.ComponentModel.DataAnnotations;

namespace GameDB.Application.DTOs.Auth;

public class RegisterDto
{
    [Required(ErrorMessage = "Ім'я користувача обов'язкове")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Ім'я має бути від 3 до 50 символів")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email обов'язковий")]
    [EmailAddress(ErrorMessage = "Невірний формат Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Пароль обов'язковий")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Пароль має бути від 6 символів")]
    public string Password { get; set; } = string.Empty;

    [Compare("Password", ErrorMessage = "Паролі не співпадають")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class LoginDto
{
    [Required(ErrorMessage = "Email обов'язковий")]
    [EmailAddress(ErrorMessage = "Невірний формат Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Пароль обов'язковий")]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

public class AuthResultDto
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int? UserId { get; set; }
    public string? Username { get; set; }

    public static AuthResultDto Ok(int userId, string username) =>
        new() { Success = true, UserId = userId, Username = username };

    public static AuthResultDto Fail(string error) =>
        new() { Success = false, Error = error };
}
