using GameDB.Domain.Enums;

namespace GameDB.Domain.Entities;

public partial class Game
{
    public int GameId { get; set; }

    public string Name { get; set; } = null!;

    public DateOnly? ReleaseDate { get; set; }

    public int? DeveloperId { get; set; }

    public int? PublisherId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public double? Rating { get; set; }

    public int? RatingCount { get; set; }

    /// <summary>https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appid}/header.jpg</summary>
    public string? HeaderImage { get; set; }

    /// <summary>https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appid}/capsule_184x69.jpg</summary>
    public string? IconImage { get; set; }

    /// <summary>Pending → Basic (базовий імпорт) → Full (збагачення).</summary>
    public GameImportStatus ImportStatus { get; set; } = GameImportStatus.Basic;

    public virtual ICollection<Alert> Alerts { get; set; } = new List<Alert>();

    public virtual Developer? Developer { get; set; }

    public virtual ICollection<GameExternalId> ExternalIds { get; set; } = new List<GameExternalId>();

    public virtual ICollection<GameOffer> GameOffers { get; set; } = new List<GameOffer>();

    public virtual Publisher? Publisher { get; set; }

    public virtual ICollection<UserLibrary> UserLibraries { get; set; } = new List<UserLibrary>();

    public virtual ICollection<Wishlist> Wishlists { get; set; } = new List<Wishlist>();

    public virtual ICollection<Genre> Genres { get; set; } = new List<Genre>();

    public virtual ICollection<Tag> Tags { get; set; } = new List<Tag>();
}