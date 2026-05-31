namespace GameDB.Domain.Entities;

/// <summary>
/// Зберігає зовнішній ідентифікатор гри в конкретному магазині.
/// Наприклад: GameId=5, ShopId=1 (Steam), ExternalId="730" (CS2).
/// </summary>
public partial class GameExternalId
{
    public int Id { get; set; }

    public int GameId { get; set; }

    /// <summary>ShopId=1 — Steam.</summary>
    public int ShopId { get; set; }

    /// <summary>AppId у вигляді рядка, напр. "730".</summary>
    public string ExternalId { get; set; } = null!;

    /// <summary>Пряме посилання на сторінку гри в магазині.</summary>
    public string? ExternalUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Game Game { get; set; } = null!;

    public virtual GameShop Shop { get; set; } = null!;
}