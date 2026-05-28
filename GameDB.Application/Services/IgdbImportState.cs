namespace GameDB.Application.Services;

public sealed class IgdbImportState
{
    private volatile bool _isImporting;

    public bool IsImporting
    {
        get => _isImporting;
        set => _isImporting = value;
    }
}
