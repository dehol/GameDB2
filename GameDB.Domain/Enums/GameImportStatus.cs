namespace GameDB.Domain.Enums;

/// <summary>
/// Pending — щойно додана, без даних.
/// Basic   — є назва + ExternalId (після базового імпорту).
/// Full    — збагачена: developer, publisher, genres, tags, rating, images.
/// </summary>
public enum GameImportStatus : byte
{
    Basic   = 0,
    Full    = 1,
    Fail = 2
}