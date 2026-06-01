namespace GameDB.Application.DTOs.Store;

/// <summary>
/// Базова інформація про гру зі списку магазину.
/// Slug — необов'язковий: Steam не має slug, GOG і Epic передають його зі списку.
/// </summary>
public sealed record StoreGameListItem(string ExternalId, string Name, string? Slug = null);
