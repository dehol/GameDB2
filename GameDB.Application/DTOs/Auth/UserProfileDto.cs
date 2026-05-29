namespace GameDB.Application.DTOs.Auth;

public record UserProfileDto(
    int UserId,
    string Username,
    string? Email,
    string? SteamId,
    bool HasPassword,
    DateTime CreatedAt,
    DateTime? LastLogin
);

public record SteamPlayerInfoDto(
    string PersonaName,
    string? AvatarUrl,
    string ProfileUrl
);
