using System.ComponentModel.DataAnnotations;

namespace GameDB.Application.DTOs;

public record GameSummaryDto(
    int GameId,
    string Name,
    string? Slug,
    string? HeaderImage,
    DateOnly? ReleaseDate,
    decimal CurrentPrice,
    int CurrentDiscount
);
