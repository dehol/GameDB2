namespace GameDB.Web.Pages.Shared;

public sealed record GameQuickActionsModel(
    int GameId,
    bool InWishlist,
    decimal? SuggestedPrice,
    string ReturnUrl,
    string PageName = "/Catalog/Index",
    bool UseDetailsRoute = false);
