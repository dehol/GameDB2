namespace GameDB.Application.DTOs;

// ── User Preference Profile ────────────────────────────────────────────────────

/// <summary>
/// Профіль вподобань користувача: обчислюється з бібліотеки, не зберігається в БД.
/// </summary>
public sealed class UserPreferenceProfileDto
{
    public int          UserId          { get; set; }
    public List<string> FavoriteGenres  { get; set; } = [];
    public List<string> FavoriteTags    { get; set; } = [];
    public List<string> TopFeatures     { get; set; } = [];
    public List<int>    OwnedGameIds    { get; set; } = [];
    public List<int>    WishlistGameIds { get; set; } = [];

    /// <summary>
    /// Усереднений L2-нормований embedding по всіх іграх бібліотеки.
    /// Порожній список якщо у користувача немає ігор або embeddings.
    /// </summary>
    public List<float>  UserEmbedding   { get; set; } = [];
}

// ── Hybrid Recommendation Request ─────────────────────────────────────────────

/// <summary>
/// Запит до RecommendationEngine. Формується у planning_node Python-агента.
/// LLM НЕ ранжує результати — лише визначає параметри пошуку.
/// </summary>
public sealed class HybridRecommendRequest
{
    public int UserId { get; set; }

    /// <summary>Жанри, що ОБОВ'ЯЗКОВО мають бути в грі (hard filter Stage 2).</summary>
    public List<string> RequiredGenres { get; set; } = [];

    /// <summary>Жанри для буст-скорингу (soft preference Stage 3).</summary>
    public List<string> BoostGenres { get; set; } = [];

    /// <summary>Теги, що ОБОВ'ЯЗКОВО мають бути в грі (hard filter Stage 2).</summary>
    public List<string> RequiredTags { get; set; } = [];

    /// <summary>Теги для буст-скорингу (soft preference Stage 3).</summary>
    public List<string> BoostTags { get; set; } = [];

    /// <summary>Жанри що ЗАБОРОНЕНІ (hard exclusion Stage 2).</summary>
    public List<string> ExcludeGenres { get; set; } = [];

    /// <summary>Теги що ЗАБОРОНЕНІ (hard exclusion Stage 2).</summary>
    public List<string> ExcludeTags { get; set; } = [];

    public decimal? MaxPrice { get; set; }
    public bool?    IsFree   { get; set; }

    /// <summary>
    /// L2-нормований user embedding з UserEmbeddingService.
    /// Якщо порожній — Stage 1 використовує fallback (rating-based retrieval).
    /// </summary>
    public List<float> UserEmbedding { get; set; } = [];

    /// <summary>ID куплених ігор. Виключаються жорстко на Stage 2.</summary>
    public List<int> OwnedGameIds { get; set; } = [];

    /// <summary>Кількість результатів у відповіді (1..20).</summary>
    public int Limit { get; set; } = 10;
}

// ── Recommended Game DTO ───────────────────────────────────────────────────────

public sealed class RecommendedGameDto
{
    public int          GameId     { get; set; }
    public string       Name       { get; set; } = "";
    public List<string> Genres     { get; set; } = [];
    public List<string> Tags       { get; set; } = [];
    public double?      Rating     { get; set; }
    public decimal?     BestPrice  { get; set; }
    public string?      Currency   { get; set; }
    public string?      ShopName   { get; set; }
    public bool         IsFree     { get; set; }

    /// <summary>
    /// Короткий опис гри (plain-text, перші 300 символів).
    /// Використовується у Python-агенті для генерації пояснень чому гра рекомендована.
    /// </summary>
    public string?      Description { get; set; }

    /// <summary>
    /// Фінальний score = base_score + feedback_bonus.
    /// Обчислюється виключно кодом, не LLM.
    /// </summary>
    public double FinalScore { get; set; }

    public ScoreBreakdownDto ScoreBreakdown { get; set; } = new();
}

/// <summary>Деталізація скору для дебагу та аналізу якості рекомендацій.</summary>
public sealed class ScoreBreakdownDto
{
    /// <summary>0.35 * cosine_similarity(userEmb, gameEmb)</summary>
    public double EmbeddingSimilarity { get; set; }

    /// <summary>0.30 * |gameTags ∩ prefTags| / max(|gameTags|, |prefTags|)</summary>
    public double TagSimilarity { get; set; }

    /// <summary>0.20 * |gameGenres ∩ prefGenres| / max(|gameGenres|, |prefGenres|)</summary>
    public double GenreSimilarity { get; set; }

    /// <summary>0.10 * game.Rating / maxRating</summary>
    public double NormalizedRating { get; set; }

    /// <summary>0.05 * log10(1 + reviewCount) / log10(1 + maxReviewCount)</summary>
    public double Popularity { get; set; }

    /// <summary>Додатковий бонус на основі liked games (max 0.15)</summary>
    public double FeedbackBonus { get; set; }
}

// ── Feedback Request ───────────────────────────────────────────────────────────

public sealed class FeedbackRequest
{
    public int  UserId  { get; set; }
    public int  GameId  { get; set; }
    public bool IsLiked { get; set; }
}