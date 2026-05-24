using System;
using System.Collections.Generic;

namespace GameDB.Domain.Entities;
public partial class Game
{
    public int GameId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public DateOnly? ReleaseDate { get; set; }

    public int? DeveloperId { get; set; }

    public int? PublisherId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public double? Rating { get; set; }

    public int? RatingCount { get; set; }

    public string? HeaderImage { get; set; }

    public int? SteamAppId { get; set; }

    public string? Slug { get; set; }

    public bool IsFree { get; set; } 
    
    public string? IconImage { get; set; }

    public virtual ICollection<Alert> Alerts { get; set; } = new List<Alert>();

    public virtual Developer? Developer { get; set; }

    public virtual ICollection<GameOffer> GameOffers { get; set; } = new List<GameOffer>();

    public virtual Publisher? Publisher { get; set; }

    public virtual ICollection<UserLibrary> UserLibraries { get; set; } = new List<UserLibrary>();

    public virtual ICollection<Wishlist> Wishlists { get; set; } = new List<Wishlist>();

    public virtual ICollection<Genre> Genres { get; set; } = new List<Genre>();
}
