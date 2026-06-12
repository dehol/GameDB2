using GameDB.Application.DTOs;

namespace GameDB.Infrastructure.AI;

public interface IUserEmbeddingService
{
    /// <summary>
    /// Обчислює user embedding як усереднений L2-нормований вектор
    /// по всіх іграх бібліотеки користувача.
    /// Повертає null якщо бібліотека порожня або жодна гра не має embedding.
    /// </summary>
    Task<float[]?> ComputeUserEmbeddingAsync(int userId, CancellationToken ct);
}

public interface IUserProfileService
{
    /// <summary>
    /// Повертає повний профіль вподобань користувача:
    /// жанри, теги, user embedding, owned/wishlist IDs.
    /// </summary>
    Task<UserPreferenceProfileDto> GetProfileAsync(int userId, CancellationToken ct);
}

public interface IRecommendationEngine
{
    /// <summary>
    /// Гібридна рекомендація: pgvector Stage 1 → hard filters Stage 2 → scoring Stage 3.
    /// LLM НЕ задіяний — лише детермінований код.
    /// </summary>
    Task<List<RecommendedGameDto>> RecommendHybridAsync(
        HybridRecommendRequest req, CancellationToken ct);
}
