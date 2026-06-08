using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Application.Services.Import;
using Hangfire;

namespace GameDB.Application.Services;

public sealed class AdminService : IAdminService
{
    private readonly IAdminRepository        _adminRepo;
    private readonly IBackgroundJobClient    _jobClient;
    private readonly EnrichmentOperationState _enrichmentState;
    private readonly PriceSyncOperationState  _priceSyncState;

    // Slug'и повинні збігатися з IStoreProvider.Slug у кожному провайдері
    private static readonly string[] PriceSyncProviders     = ["steam", "gog", "epic"];
    private static readonly string[] EnrichmentProviders    = ["steam", "gog", "epic"];

    public AdminService(
        IAdminRepository        adminRepo,
        IBackgroundJobClient    jobClient,
        EnrichmentOperationState enrichmentState,
        PriceSyncOperationState  priceSyncState)
    {
        _adminRepo       = adminRepo;
        _jobClient       = jobClient;
        _enrichmentState = enrichmentState;
        _priceSyncState  = priceSyncState;
    }

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var stats = await _adminRepo.GetStatsAsync(ct);

        return new AdminDashboardDto(
            stats,
            ImportJobStatusDto.FromState(_enrichmentState, "Збагачення деталей"),
            ImportJobStatusDto.FromState(_priceSyncState,  "Синхронізація цін"));
    }

    public Task<AdminGameListDto> GetGamesAsync(
        AdminGameCoverageFilter filter, int page, int pageSize,
        string? search = null, CancellationToken ct = default)
        => _adminRepo.GetGamesAsync(filter, page, pageSize, search, ct);

    public Task<int> ImportBasicGamesAsync(string? providerSlug = null, CancellationToken ct = default)
    {
        _jobClient.Enqueue<IBasicImportService>(s =>
            s.RunImportJobAsync(providerSlug, CancellationToken.None));

        return Task.FromResult(1);
    }

    /// <summary>
    /// БУЛО: один job → Task.WhenAll всередині → ризик перевищення InvisibilityTimeout (8h)
    ///       → Hangfire перезапускав job → дублювання роботи.
    ///
    /// СТАЛО: 3 окремих job (по одному на провайдер) — аналогічно StartPriceSync.
    ///   — кожен job захищений InvisibilityTimeout незалежно
    ///   — state.IsRunning = true виставляється ОДРАЗУ → UI без затримки 15с
    ///   — StopToken лінкується в EnrichProviderAsync → Stop перериває HTTP-запити
    /// </summary>
    public void StartEnrichmentImport(bool overwriteExisting = false)
    {
        _enrichmentState.ResetStop();
        _enrichmentState.PrepareParallelSync("Збагачення деталей", EnrichmentProviders.Length);

        foreach (var slug in EnrichmentProviders)
        {
            _jobClient.Enqueue<IGameEnrichmentService>(s =>
                s.EnrichProviderAsync(slug, overwriteExisting, CancellationToken.None));
        }
    }

    /// <summary>
    /// БУЛО: один job → всі провайдери послідовно → тривалість > InvisibilityTimeout
    ///       → Hangfire вважав job зависшим → перезапускав → 20 годин на dashboard.
    ///
    /// СТАЛО: 3 окремих job → 3 провайдери паралельно → кожен завершується ~2-3 год
    ///        → InvisibilityTimeout (8 год) ніколи не спрацьовує.
    ///
    /// Якщо providerSlug вказано — ставиться лише один job для того провайдера.
    /// </summary>
    public bool StartPriceSync(int batchSize, DateTime? notSyncedSince = null)
    {
        // PrepareParallelSync:
        //   — скидає лічильники
        //   — виставляє IsRunning = true ОДРАЗУ (UI бачить "синхронізація" без затримки)
        //   — замінює StopCts на свіжий (кнопка Stop буде працювати)
        //   — встановлює _activeProviders = 3 (останній provider викличе MarkFinished)
        _priceSyncState.ResetStop();
        _priceSyncState.PrepareParallelSync("Синхронізація цін", PriceSyncProviders.Length);

        foreach (var slug in PriceSyncProviders)
        {
            // CancellationToken.None — Hangfire замінює на ShutdownToken при виконанні.
            // [JobDisplayName("Ціни: {0}")] на інтерфейсі → у Dashboard: "Ціни: steam" тощо.
            _jobClient.Enqueue<IPriceSyncService>(s =>
                s.SyncProviderAsync(slug, CancellationToken.None));
        }

        return true;
    }

    public bool StartPriceSyncForProvider(string providerSlug)
    {
        _jobClient.Enqueue<IPriceSyncService>(s =>
            s.SyncProviderAsync(providerSlug, CancellationToken.None));

        return true;
    }

    public void StopEnrichmentImport() => _enrichmentState.RequestStop();

    // Зупиняє через state.IsRunning = false — петлі у провайдерах перевіряють цей прапор.
    // Для миттєвої зупинки Hangfire-job — видалити через Dashboard або Hangfire API.
    public void StopPriceSync() => _priceSyncState.RequestStop();
}