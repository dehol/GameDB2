using System;
using System.Collections.Generic;

namespace GameDB.Domain.Entities;

public partial class Alert
{
    public int AlertId { get; set; }

    public int UserId { get; set; }

    public int GameId { get; set; }

    public decimal? TargetPrice { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? TriggeredAt { get; set; }

    public DateTime? LastNotifiedAt { get; set; }

    public bool AutoUpdate { get; set; }

    public string AutoUpdateMode { get; set; } = null!;

    public decimal? ReferenceLowest { get; set; }

    public int? ShopId { get; set; }

    public virtual Game Game { get; set; } = null!;

    public virtual GameShop? Shop { get; set; }

    public virtual User User { get; set; } = null!;
}
