using System;
using System.Collections.Generic;

namespace GameDB.Domain.Entities;

public partial class GameOffer
{
    public int GameOfferId { get; set; }

    public decimal CurrentPrice { get; set; }

    public short CurrentDiscount { get; set; }

    public string Currency { get; set; } = null!;

    public DateTime? LastSyncedAt { get; set; }

    public decimal? FinalPrice { get; set; }

    public int ExternalId { get; set; }

    public virtual GameExternalId External { get; set; } = null!;

    public virtual ICollection<PriceHistory> PriceHistories { get; set; } = new List<PriceHistory>();
}
