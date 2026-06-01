using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GameDB.Application.DTOs;

// ── Filtered List (https://www.gog.com/games/ajax/filtered) ──────────────────

public sealed class GogFilteredResponseDto
{
    [JsonPropertyName("products")]
    public List<GogProductListItemDto> Products { get; init; } = [];

    [JsonPropertyName("totalGamesCount")]
    public int TotalGamesCount { get; init; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; init; }
}

public sealed class GogProductListItemDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("slug")]
    public string? Slug { get; init; }

    [JsonPropertyName("developer")]
    public string? Developer { get; init; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; init; }

    [JsonPropertyName("isGame")]
    public bool IsGame { get; init; }

    [JsonPropertyName("isAvailableForSale")]
    public bool IsAvailableForSale { get; init; }
}

// ── Product Details (https://api.gog.com/products/{id}) ──────────────────────

public sealed class GogProductDetailsDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("slug")]
    public string? Slug { get; init; }

    [JsonPropertyName("developer")]
    public string? Developer { get; init; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; init; }

    /// <summary>0–50 scale. Normalize to 0–100 via × 2.</summary>
    [JsonPropertyName("rating")]
    public int Rating { get; init; }

    [JsonPropertyName("genres")]
    public List<GogCategoryDto> Genres { get; init; } = [];

    [JsonPropertyName("tags")]
    public List<GogCategoryDto> Tags { get; init; } = [];

    [JsonPropertyName("images")]
    public GogImagesDto? Images { get; init; }

    [JsonPropertyName("price")]
    public GogPriceDto? Price { get; init; }
}

public sealed class GogCategoryDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("slug")]
    public string? Slug { get; init; }
}

public sealed class GogImagesDto
{
    /// <summary>Square logo / icon. May start with "//".</summary>
    [JsonPropertyName("logo")]
    public string? Logo { get; init; }

    /// <summary>Wide background / header. May start with "//".</summary>
    [JsonPropertyName("background")]
    public string? Background { get; init; }
}

public sealed class GogPriceDto
{
    /// <summary>Current price as decimal string, e.g. "9.99".</summary>
    [JsonPropertyName("amount")]
    public string? Amount { get; init; }

    /// <summary>Original price before discount.</summary>
    [JsonPropertyName("baseAmount")]
    public string? BaseAmount { get; init; }

    /// <summary>Discount percentage 0–100.</summary>
    [JsonPropertyName("discountPercentage")]
    public int DiscountPercentage { get; init; }

    [JsonPropertyName("isFree")]
    public bool IsFree { get; init; }
}