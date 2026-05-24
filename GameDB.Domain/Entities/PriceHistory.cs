using System;
using System.Collections.Generic;

namespace GameDB.Domain.Entities;
public partial class PriceHistory
{
    public int PriceHistory1 { get; set; }

    public DateTime RecordedAt { get; set; }

    public decimal Price { get; set; }

    public short DiscountPercent { get; set; }

    public string Currency { get; set; } = null!;

    public int GameOfferId { get; set; }

    public decimal? LowestPrice { get; set; }

    public DateOnly? LowestPriceDate { get; set; }

    public virtual GameOffer GameOffer { get; set; } = null!;
}
