using GameDB.Application.Interfaces;
using GameDB.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services.Import;

/// <summary>
/// Фаза 2 імпорту: збагачує ігри деталями з магазину (жанри, теги, зображення, рейтинг).
/// Зареєстрований як Scoped — кожен виклик з воркера отримує свій DbContext.
/// </summary>
public sealed class GameEnrichmentService(
    IGameRepository          repo,
    StoreGameMapper          mapper,
    ILogger<GameEnrichmentService> logger) : IGameEnrichmentService
{
    public async Task EnrichBatchAsync(
        IStoreProvider          provider,
        List<string>            externalIds,
        ImportOperationState state,
        CancellationToken       ct = default)
    {
        foreach (var externalId in externalIds)
        {
            if (ct.IsCancellationRequested || !state.IsRunning) break;

            try
            {
                await EnrichSingleAsync(provider, externalId, state.OverwriteExisting, ct);
                state.IncrementProcessed();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                state.IncrementFailed();
                logger.LogError(ex, "[{Slug}] Помилка збагачення {Id}", provider.Slug, externalId);
            }

            await Task.Delay(provider.DelayBetweenRequestsMs, ct);
        }
    }

    private async Task EnrichSingleAsync(
        IStoreProvider provider,
        string         externalId,
        bool           overwrite,
        CancellationToken ct)
    {
        var game = await repo.GetByExternalIdAsync(provider.ShopId, externalId, ct);
        if (game is null) return;

        var details = await provider.GetGameDetailsAsync(externalId, ct);
        if (details is null)
        {
            game.ImportStatus = GameImportStatus.Fail;
            await repo.UpdateAsync(game, ct);
            return;
        }

        await mapper.ApplyAsync(game, details, repo, overwrite, ct);

        game.ImportStatus = GameImportStatus.Full;
        game.UpdatedAt    = DateTime.UtcNow;

        if (details.StoreUrl is not null)
        {
            var extId = game.GameExternalIds
                .FirstOrDefault(e => e.ShopId == provider.ShopId && e.ExternalId == externalId);
            if (extId is not null)
                extId.ExternalUrl = details.StoreUrl;
        }

        await repo.UpdateAsync(game, ct);
        logger.LogInformation("[{Slug}] Збагачено: {Name}", provider.Slug, game.Name);
    }
}
