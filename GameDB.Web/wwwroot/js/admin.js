(function () {
    'use strict';

    const api = '/api/admin';
    let currentPage = 1;
    const pageSize  = 50;
    let pollTimer   = null;

    const el = id => document.getElementById(id);

    // ── Toast ─────────────────────────────────────────────────────────────────

    function toast(msg, isError = false) {
        const t = el('adminToast');
        t.textContent = msg;
        t.classList.remove('d-none', 'error');
        if (isError) t.classList.add('error');
        clearTimeout(t._hide);
        t._hide = setTimeout(() => t.classList.add('d-none'), 4000);
    }

    // ── Fetch helpers ─────────────────────────────────────────────────────────

    const csrfToken = () =>
        document.querySelector('#antiForgeryForm input[name="__RequestVerificationToken"]')?.value;

    async function post(path, query = '') {
        const url     = query ? `${api}${path}?${query}` : `${api}${path}`;
        const token   = csrfToken();
        const headers = token ? { RequestVerificationToken: token } : {};
        const res     = await fetch(url, { method: 'POST', credentials: 'same-origin', headers });
        const data    = await res.json().catch(() => ({}));
        if (!res.ok) throw new Error(data.message || res.statusText);
        return data;
    }

    async function getDashboard() {
        const res = await fetch(`${api}/dashboard`, { credentials: 'same-origin' });
        if (!res.ok) throw new Error('Не вдалося завантажити dashboard');
        return res.json();
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    function fmtDate(iso) {
        return iso ? new Date(iso).toLocaleString('uk-UA') : '—';
    }

    function escapeHtml(s) {
        const d = document.createElement('div');
        d.textContent = s;
        return d.innerHTML;
    }

    // ── Unified job rendering ─────────────────────────────────────────────────

    function renderJobStatus(statusEl, job, label) {
        const isRunning = job.isRunning;
        const hasError  = !!job.lastError;

        statusEl.classList.toggle('running', isRunning);
        statusEl.classList.toggle('error', !isRunning && hasError);

        const status = isRunning
            ? '▶ працює'
            : (job.finishedAt ? '■ зупинено' : '○ очікує');

        let html = `<strong>${label}</strong>: ${status}`;
        if (job.source)        html += ` · ${escapeHtml(job.source)}`;
        if (job.lastBatchSize) html += ` · батч ${job.lastBatchSize}`;
        if (job.lastMessage)   html += `<br>${escapeHtml(job.lastMessage)}`;
        if (job.lastError)     html += `<br><span class="text-danger">${escapeHtml(job.lastError)}</span>`;
        if (job.startedAt)     html += `<br><span class="text-muted">з ${fmtDate(job.startedAt)}</span>`;

        statusEl.innerHTML = html;
    }

    function renderProgress(wrapEl, barEl, textEl, job) {
        if (!job.isRunning) {
            wrapEl.classList.add('d-none');
            return;
        }
        wrapEl.classList.remove('d-none');

        if (job.processed != null && job.total > 0) {
            // determinate
            const pct = Math.min(100, Math.round((job.processed / job.total) * 100));
            barEl.style.width = `${pct}%`;
            barEl.classList.remove('progress-bar-striped', 'progress-bar-animated');
            textEl.textContent =
                `${job.processed} / ${job.total} (${pct}%)${job.lastMessage ? ' · ' + job.lastMessage : ''}`;
        } else {
            // indeterminate
            barEl.style.width = '100%';
            barEl.classList.add('progress-bar-striped', 'progress-bar-animated');
            textEl.textContent = job.lastMessage || '';
        }
    }

    // ── Dashboard ─────────────────────────────────────────────────────────────

    function applyDashboard(d) {
        const s = d.stats;
        el('statsCards').querySelector('[data-stat="total"]').textContent             = s.totalGames;
        el('statsCards').querySelector('[data-stat="statusFull"]').textContent        = s.statusFull;
        el('statsCards').querySelector('[data-stat="statusBasic"]').textContent       = s.statusBasic;
        el('statsCards').querySelector('[data-stat="withPrice"]').textContent         = s.withPrice;
        el('statsCards').querySelector('[data-stat="withoutPrice"]').textContent      = s.withoutPrice;
        el('statsCards').querySelector('[data-stat="basicWithoutPrice"]').textContent = s.basicWithoutPrice;

        el('pendingDetails').textContent = s.statusBasic;
        el('lastPriceSync').textContent  = fmtDate(s.lastPriceSyncAt);

        // Basic import
        renderJobStatus(el('basicImportJobStatus'), d.basicImport, 'Каталог');
        renderProgress(
            el('basicImportProgressWrap'), el('basicImportProgressBar'), el('basicImportProgressText'),
            d.basicImport
        );

        // Enrichment
        renderJobStatus(el('enrichmentJobStatus'), d.gameEnrichment, 'Збагачення');
        renderProgress(
            el('enrichProgressWrap'), el('enrichProgressBar'), el('enrichProgressText'),
            d.gameEnrichment
        );

        // Price sync
        renderJobStatus(el('priceJobStatus'), d.priceSync, 'Ціни');
        renderProgress(
            el('priceProgressWrap'), el('priceProgressBar'), el('priceProgressText'),
            d.priceSync
        );
    }

    async function refreshDashboard() {
        try { applyDashboard(await getDashboard()); }
        catch (e) { toast(e.message, true); }
    }

    // ── Games table ───────────────────────────────────────────────────────────

    async function loadGames() {
        const params = new URLSearchParams({
            filter:   el('gameFilter').value,
            page:     currentPage,
            pageSize: String(pageSize),
        });
        const search = el('gameSearch').value.trim();
        if (search) params.set('search', search);

        const res = await fetch(`${api}/games?${params}`, { credentials: 'same-origin' });
        if (!res.ok) {
            el('gamesTableBody').innerHTML =
                '<tr><td colspan="7" class="text-danger text-center">Помилка завантаження</td></tr>';
            return;
        }

        const { items, page, pageSize: ps, totalCount } = await res.json();

        el('gamesTableBody').innerHTML = !items.length
            ? '<tr><td colspan="7" class="text-muted text-center py-3">Немає записів</td></tr>'
            : items.map(g => `<tr>
                <td>${g.gameId}</td>
                <td>${escapeHtml(g.name)}</td>
                <td>${g.steamExternalId ?? '—'}</td>
                <td>${g.importStatus === 'Full'
                    ? '<span class="admin-badge admin-badge-yes">Full</span>'
                    : '<span class="admin-badge admin-badge-no">Basic</span>'}</td>
                <td>${g.hasPrice
                    ? '<span class="admin-badge admin-badge-yes">так</span>'
                    : '<span class="admin-badge admin-badge-no">ні</span>'}</td>
                <td class="small text-muted">${fmtDate(g.lastSyncedAt)}</td>
                <td><a href="/Catalog/Details/${g.gameId}" class="btn btn-link btn-sm p-0">→</a></td>
            </tr>`).join('');

        const from = (page - 1) * ps + 1;
        const to   = Math.min(page * ps, totalCount);
        el('gamesPagerInfo').textContent =
            totalCount ? `Показано ${from}–${to} з ${totalCount}` : '0 записів';
        el('btnPrevPage').disabled = page <= 1;
        el('btnNextPage').disabled = page * ps >= totalCount;
    }

    function startPolling() {
        if (pollTimer) clearInterval(pollTimer);
        pollTimer = setInterval(refreshDashboard, 5000);
    }

    // ── Catalog import ────────────────────────────────────────────────────────

    async function runBasicImport(provider, label) {
        document.querySelectorAll('#catalogImportBtns button').forEach(b => b.disabled = true);
        try {
            const query = provider ? `provider=${encodeURIComponent(provider)}` : '';
            await post('/import/basic', query);
            toast(`Каталог ${label}: запущено`);
            refreshDashboard();
            startPolling();
        } catch (e) {
            toast(e.message, true);
        } finally {
            document.querySelectorAll('#catalogImportBtns button').forEach(b => b.disabled = false);
        }
    }

    document.querySelectorAll('[data-import-provider]').forEach(btn =>
        btn.addEventListener('click', () =>
            runBasicImport(btn.dataset.importProvider, btn.textContent.trim())));

    el('btnBasicStop').addEventListener('click', async () => {
        try {
            await post('/import/basic/stop');
            toast('Імпорт каталогу зупинено');
            refreshDashboard();
        } catch (e) { toast(e.message, true); }
    });

    // ── Enrichment ────────────────────────────────────────────────────────────

    async function runEnrichment(provider, label) {
        document.querySelectorAll('#enrichImportBtns button').forEach(b => b.disabled = true);
        try {
            const overwrite = el('enrichOverwrite').checked;
            const query = `overwrite=${overwrite}${provider ? `&provider=${encodeURIComponent(provider)}` : ''}`;
            await post('/import/enrich/start', query);
            toast(`Збагачення ${label} запущено`);
            refreshDashboard();
            startPolling();
        } catch (e) {
            toast(e.message, true);
        } finally {
            document.querySelectorAll('#enrichImportBtns button').forEach(b => b.disabled = false);
        }
    }

    document.querySelectorAll('[data-enrich-provider]').forEach(btn =>
        btn.addEventListener('click', () =>
            runEnrichment(btn.dataset.enrichProvider, btn.textContent.trim())));

    el('btnEnrichStop').addEventListener('click', async () => {
        try {
            await post('/import/enrich/stop');
            toast('Збагачення зупинено');
            refreshDashboard();
        } catch (e) { toast(e.message, true); }
    });

    // ── Price sync ────────────────────────────────────────────────────────────

    function batchParam() {
        return `batchSize=${el('priceBatchSize').value || 100}`;
    }

    async function runPriceSync(provider, label, extra = '') {
        document.querySelectorAll('#priceImportBtns button').forEach(b => b.disabled = true);
        try {
            const query = `${batchParam()}${provider ? `&provider=${encodeURIComponent(provider)}` : ''}${extra}`;
            await post('/import/prices/start', query);
            toast(`Синхронізація цін ${label} запущена`);
            refreshDashboard();
            startPolling();
        } catch (e) {
            toast(e.message, true);
        } finally {
            document.querySelectorAll('#priceImportBtns button').forEach(b => b.disabled = false);
        }
    }

    document.querySelectorAll('[data-price-provider]').forEach(btn =>
        btn.addEventListener('click', () =>
            runPriceSync(btn.dataset.priceProvider, btn.textContent.trim())));

    el('btnPriceSinceStart').addEventListener('click', () => {
        const since = el('priceSinceDate').value;
        if (!since) { toast('Оберіть дату', true); return; }
        runPriceSync('', `(після ${since})`, `&notSyncedSince=${encodeURIComponent(since)}`);
    });

    el('btnPriceStop').addEventListener('click', async () => {
        try {
            await post('/import/prices/stop');
            toast('Зупинка синхронізації цін…');
            refreshDashboard();
        } catch (e) { toast(e.message, true); }
    });

    // ── Table controls ────────────────────────────────────────────────────────

    el('btnRefreshAll').addEventListener('click', () => { refreshDashboard(); loadGames(); });
    el('gameFilter').addEventListener('change',   () => { currentPage = 1; loadGames(); });
    el('gameSearch').addEventListener('keydown',  e  => { if (e.key === 'Enter') { currentPage = 1; loadGames(); } });
    el('btnPrevPage').addEventListener('click',   ()  => { if (currentPage > 1) { currentPage--; loadGames(); } });
    el('btnNextPage').addEventListener('click',   ()  => { currentPage++; loadGames(); });

    // ── Init ──────────────────────────────────────────────────────────────────

    refreshDashboard();
    loadGames();
    startPolling();

})();
