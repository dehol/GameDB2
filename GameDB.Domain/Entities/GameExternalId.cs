using System;
using System.Collections.Generic;

namespace GameDB.Domain.Entities;

public partial class GameExternalId
{
    public int Id { get; set; }

    public int GameId { get; set; }

    public int ShopId { get; set; }

    public string ExternalId { get; set; } = null!;

    public string? ExternalUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Game Game { get; set; } = null!;

    public virtual ICollection<GameOffer> GameOffers { get; set; } = new List<GameOffer>();

    public virtual GameShop Shop { get; set; } = null!;
}
