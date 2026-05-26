namespace GameDB.Domain.Entities;

public partial class UserShopProfile
{
    public int ProfileId { get; set; }

    public int UserId { get; set; }

    public int ShopId { get; set; }

    public string ExternalUid { get; set; } = null!;

    public DateTime LinkedAt { get; set; }

    public virtual GameShop Shop { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
