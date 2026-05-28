using System.Security.Cryptography;
using GameDB.Application.DTOs.Auth;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;

namespace GameDB.Application.Services;

public class AuthService
{
    private const int SaltSize   = 16;
    private const int HashSize   = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    private readonly IUserRepository _users;

    public AuthService(IUserRepository users) => _users = users;

    // ─── Хешування паролю (PBKDF2-SHA256, без зовнішніх пакетів) ──────────
    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 2) return false;
        var salt     = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual   = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    // ─── Email реєстрація ───────────────────────────────────────────────────
    public async Task<AuthResultDto> RegisterAsync(RegisterDto dto)
    {
        if (await _users.EmailExistsAsync(dto.Email))
            return AuthResultDto.Fail("Цей Email вже зареєстровано.");

        if (await _users.UsernameExistsAsync(dto.Username))
            return AuthResultDto.Fail("Це ім'я користувача вже зайнято.");

        var user = new User
        {
            Username     = dto.Username,
            Email        = dto.Email,
            PasswordHash = HashPassword(dto.Password),
            CreatedAt    = DateTime.UtcNow,
        };

        var created = await _users.CreateAsync(user);
        return AuthResultDto.Ok(created.UserId, created.Username);
    }

    // ─── Email логін ────────────────────────────────────────────────────────
    public async Task<AuthResultDto> LoginAsync(LoginDto dto)
    {
        var user = await _users.GetByEmailAsync(dto.Email);
        if (user is null)
            return AuthResultDto.Fail("Невірний Email або пароль.");

        if (string.IsNullOrEmpty(user.PasswordHash))
            return AuthResultDto.Fail("Цей акаунт не має пароля. Використайте вхід через Steam.");

        if (!VerifyPassword(dto.Password, user.PasswordHash))
            return AuthResultDto.Fail("Невірний Email або пароль.");

        user.LastLogin = DateTime.UtcNow;
        await _users.UpdateAsync(user);
        return AuthResultDto.Ok(user.UserId, user.Username);
    }

    // ─── Steam логін / реєстрація ──────────────────────────────────────────
    public async Task<AuthResultDto> LoginOrRegisterViaSteamAsync(string steamId, string? steamName)
    {
        var user = await _users.GetBySteamIdAsync(steamId);
        if (user is not null)
        {
            user.LastLogin = DateTime.UtcNow;
            await _users.UpdateAsync(user);
            return AuthResultDto.Ok(user.UserId, user.Username);
        }

        var username = await BuildUniqueUsernameAsync(steamName ?? $"steam_{steamId}");
        var newUser = new User
        {
            Username  = username,
            SteamId   = steamId,
            CreatedAt = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
        };
        var created = await _users.CreateAsync(newUser);
        return AuthResultDto.Ok(created.UserId, created.Username);
    }

    private async Task<string> BuildUniqueUsernameAsync(string base_)
    {
        var candidate = base_[..Math.Min(base_.Length, 48)];
        if (!await _users.UsernameExistsAsync(candidate)) return candidate;

        for (var i = 2; i < 1000; i++)
        {
            var attempt = $"{candidate}{i}";
            if (!await _users.UsernameExistsAsync(attempt)) return attempt;
        }
        return $"{candidate}_{Guid.NewGuid():N}"[..50];
    }
}
