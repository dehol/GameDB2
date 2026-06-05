using System;
using System.Collections.Generic;

namespace GameDB.Domain.Entities;

public partial class GameShop
{
    public int ShopId { get; set; }

    public string Name { get; set; } = null!;

    public string? BaseUrl { get; set; }

    public string? ApiBaseUrl { get; set; }

    public int? ItadShopId { get; set; }

    public string Slug { get; set; } = null!;

    public virtual ICollection<Alert> Alerts { get; set; } = new List<Alert>();

    public virtual ICollection<GameExternalId> GameExternalIds { get; set; } = new List<GameExternalId>();

    public virtual ICollection<UserLibrary> UserLibraries { get; set; } = new List<UserLibrary>();

    public virtual ICollection<UserShopProfile> UserShopProfiles { get; set; } = new List<UserShopProfile>();
}
