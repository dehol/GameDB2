namespace GameDB.Application.Services;

// ARCH-7 fix: volatile keyword ensures cross-thread visibility (ARM-safe)
public sealed class SteamImportState
{
    private volatile bool _isImportingDetails;

    public bool IsImportingDetails
    {
        get => _isImportingDetails;
        set => _isImportingDetails = value;
    }
}