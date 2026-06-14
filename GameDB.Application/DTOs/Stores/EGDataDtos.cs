using System.Text.Json.Serialization;

namespace GameDB.Application.DTOs;

// ── Items list (https://api.egdata.app/items) ─────────────────────────────────

public sealed class EGDataListResponseDto
{
    [JsonPropertyName("elements")]
    public List<EGDataItemDto> Elements { get; init; } = [];
    
    [JsonIgnore]
    public bool HasDataFromServer { get; set; }

    [JsonPropertyName("paging")]
    public EGDataPagingDto? Paging { get; init; }
    
    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("limit")]
    public int Limit { get; init; }
}

public sealed class EGDataPagingDto
{

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

    [JsonPropertyName("entitlementType")]
    public string? EntitlementType { get; init; }

    [JsonPropertyName("itemType")]
    public string? ItemType { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("unsearchable")]
    public bool Unsearchable { get; init; }

    [JsonPropertyName("requiresSecureAccount")]
    public bool RequiresSecureAccount { get; init; }

    [JsonPropertyName("releaseInfo")]
    public List<EGDataReleaseInfoDto> ReleaseInfo { get; init; } = [];

    [JsonPropertyName("developer")]
    public string? Developer { get; init; }

    [JsonPropertyName("developerId")]
    public string? DeveloperId { get; init; }

    [JsonPropertyName("linkedOffers")]
    public List<string?> LinkedOffers { get; init; } = [];

    /// <summary>
    /// Текстовий або HTML опис гри від EGData API.
    /// Може бути null якщо API не повертає поле для конкретного елемента.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public sealed class EGDataReleaseInfoDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("appId")]
    public string? AppId { get; init; }

    [JsonPropertyName("platform")]
    public List<string> Platform { get; init; } = [];
}

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

public sealed record EGDataPriceDto(
    decimal Price,
    short   Discount,
    string  Currency,
    string? StoreUrl = null);