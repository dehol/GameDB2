namespace GameDB.Domain.Entities;

public partial class GameShop
{
    public int ShopId { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>Унікальний ідентифікатор магазину для коду (напр. "steam", "gog", "epic").</summary>
    public string Slug { get; set; } = null!;

    public string? BaseUrl { get; set; }

    public string? ApiBaseUrl { get; set; }

    public virtual ICollection<GameExternalId> GameExternalIds { get; set; } = new List<GameExternalId>();

    public virtual ICollection<GameOffer> GameOffers { get; set; } = new List<GameOffer>();

    public virtual ICollection<UserLibrary> UserLibraries { get; set; } = new List<UserLibrary>();

    public virtual ICollection<UserShopProfile> UserShopProfiles { get; set; } = new List<UserShopProfile>();
}