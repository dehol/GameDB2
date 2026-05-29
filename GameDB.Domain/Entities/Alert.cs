namespace GameDB.Domain.Entities;

public partial class Alert
{
    public int AlertId { get; set; }

    public int UserId { get; set; }

    public int GameId { get; set; }

    public decimal? TargetPrice { get; set; }

    public bool AutoUpdate { get; set; }

    /// <summary>MatchLowest або BeatLowest</summary>
    public string AutoUpdateMode { get; set; } = "BeatLowest";

    /// <summary>null = усі магазини</summary>
    public int? ShopId { get; set; }

    /// <summary>Найнижча ціна на момент останнього оновлення (для auto-update)</summary>
    public decimal? ReferenceLowest { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? TriggeredAt { get; set; }

    public DateTime? LastNotifiedAt { get; set; }

    public virtual Game Game { get; set; } = null!;

    public virtual User User { get; set; } = null!;

    public virtual GameShop? Shop { get; set; }
}
