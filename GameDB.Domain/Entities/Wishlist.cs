using System;
using System.Collections.Generic;

namespace GameDB.Domain.Entities;
public partial class Wishlist
{
    public int UserId { get; set; }

    public int GameId { get; set; }

    public DateTime AddedAt { get; set; }

    public virtual Game Game { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
