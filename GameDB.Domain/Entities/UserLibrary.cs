using System;
using System.Collections.Generic;

namespace GameDB.Domain.Entities;

public partial class UserLibrary
{
    public int UserId { get; set; }

    public int GameId { get; set; }

    public int ShopId { get; set; }

    public DateTime AddedAt { get; set; }

    public virtual Game Game { get; set; } = null!;

    public virtual GameShop Shop { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
