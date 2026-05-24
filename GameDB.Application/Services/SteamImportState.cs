namespace GameDB.Application.Services;

public class SteamImportState
{
    // Прапорець, який каже воркеру, чи потрібно стягувати деталі
    public bool IsImportingDetails { get; set; } = false;
}