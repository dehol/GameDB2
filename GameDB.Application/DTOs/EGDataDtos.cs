using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GameDB.Application.DTOs;

// ── Items list (https://api.egdata.app/items) ─────────────────────────────────

public sealed class EGDataListResponseDto
{
    [JsonPropertyName("elements")]
    public List<EGDataItemDto> Elements { get; init; } = [];
}

public sealed class EGDataPagingDto
{
    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }
}

// ── Single item ───────────────────────────────────────────────────────────────

public sealed class EGDataItemDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; }

    [JsonPropertyName("namespace")]
    public string? Namespace { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("developerDisplayName")]
    public string? DeveloperDisplayName { get; init; }

    [JsonPropertyName("publisherDisplayName")]
    public string? PublisherDisplayName { get; init; }

    // НОВЕ: Додаємо мапінг категорій з API Epic Games
    [JsonPropertyName("categories")]
    public List<EGDataCategoryDto> Categories { get; init; } = [];

    [JsonPropertyName("tags")]
    public List<EGDataTagDto> Tags { get; init; } = [];

    [JsonPropertyName("keyImages")]
    public List<EGDataImageDto> KeyImages { get; init; } = [];

    [JsonPropertyName("price")]
    public EGDataPriceWrapperDto? Price { get; init; }

    [JsonPropertyName("productSlug")]
    public string? ProductSlug { get; init; }
}

// НОВЕ: Клас для десеріалізації категорії
public sealed class EGDataCategoryDto
{
    [JsonPropertyName("path")]
    public string? Path { get; init; }
}

public sealed class EGDataTagDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>"genre", "theme", "feature", etc.</summary>
    [JsonPropertyName("groupName")]
    public string? GroupName { get; init; }
}

public sealed class EGDataImageDto
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

public sealed class EGDataPriceWrapperDto
{
    [JsonPropertyName("totalPrice")]
    public EGDataTotalPriceDto? TotalPrice { get; init; }
}

public sealed class EGDataTotalPriceDto
{
    [JsonPropertyName("discountPrice")]
    public int DiscountPrice { get; init; }

    [JsonPropertyName("originalPrice")]
    public int OriginalPrice { get; init; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; init; }

    [JsonPropertyName("discountPercentage")]
    public int DiscountPercentage { get; init; }
}