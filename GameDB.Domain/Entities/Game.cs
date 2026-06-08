using Pgvector;

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

    public string? HeaderImage { get; set; }

    public string? IconImage { get; set; }

    public Enums.GameImportStatus ImportStatus { get; set; }
    
    public string NormalizedName { get; set; } = null!;

    public Vector? Embedding { get; set; }

    public virtual ICollection<Alert> Alerts { get; set; } = new List<Alert>();

    public virtual Developer? Developer { get; set; }

    public virtual ICollection<GameExternalId> GameExternalIds { get; set; } = new List<GameExternalId>();

    public virtual Publisher? Publisher { get; set; }

    public virtual ICollection<UserLibrary> UserLibraries { get; set; } = new List<UserLibrary>();

    public virtual ICollection<Wishlist> Wishlists { get; set; } = new List<Wishlist>();

    public virtual ICollection<Genre> Genres { get; set; } = new List<Genre>();

    public virtual ICollection<Tag> Tags { get; set; } = new List<Tag>();
}
