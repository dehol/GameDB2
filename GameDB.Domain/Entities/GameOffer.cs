using System;
using System.Collections.Generic;

namespace GameDB.Domain.Entities;
public partial class GameOffer
{
    public int GameOfferId { get; set; }

    public int GameId { get; set; }

    public int ShopId { get; set; }

    public string? ExternalId { get; set; }

    public string? DownloadUrl { get; set; }

    public decimal CurrentPrice { get; set; }

    public short CurrentDiscount { get; set; }

    public string Currency { get; set; } = null!;

    public DateTime? LastSyncedAt { get; set; }

    public string? ItadUuid { get; set; }

    public virtual Game Game { get; set; } = null!;

    public virtual ICollection<PriceHistory> PriceHistories { get; set; } = new List<PriceHistory>();

    public virtual GameShop Shop { get; set; } = null!;
}
