// GameDB.Application/Options/SteamImportOptions.cs
namespace GameDB.Application.Options;

public class SteamImportOptions
{
    public int DelayBetweenRequestsMs { get; set; } = 820;
    public int ProphylacticPauseAfter { get; set; } = 200;
    public int PauseAfterErrorMs { get; set; } = 75000;
    public int PauseAfterBatchMs { get; set; } = 75000;
    public int BasicImportBatchSize { get; set; } = 1000;
}