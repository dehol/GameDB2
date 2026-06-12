namespace GameDB.Domain.Entities;

/// <summary>
/// Зворотній зв'язок користувача по грі.
/// Використовується для feedback_bonus у RecommendationEngine.
/// </summary>
public sealed class UserGameFeedback
{
    public int      FeedbackId { get; set; }
    public int      UserId     { get; set; }
    public int      GameId     { get; set; }
    public bool     IsLiked    { get; set; }
    public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Game? Game { get; set; }
}
