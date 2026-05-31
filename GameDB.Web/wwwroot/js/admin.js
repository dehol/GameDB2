(function () {
    const api = '/api/admin';
    let currentPage = 1;
    const pageSize = 50;
    let pollTimer = null;

    const el = (id) => document.getElementById(id);

    function toast(msg, isError) {
        const t = el('adminToast');
        t.textContent = msg;
        t.classList.remove('d-none', 'error');
        if (isError) t.classList.add('error');
        clearTimeout(t._hide);
        t._hide = setTimeout(() => t.classList.add('d-none'), 4000);
    }

    const csrfToken = () =>
        document.querySelector('#antiForgeryForm input[name="__RequestVerificationToken"]')?.value;

    async function post(path, query) {
        const url = query ? `${api}${path}?${query}` : `${api}${path}`;
        const token = csrfToken();
        const headers = token ? { RequestVerificationToken: token } : {};
        const res = await fetch(url, { method: 'POST', credentials: 'same-origin', headers });
        const data = await res.json().catch(() => ({}));
        if (!res.ok) throw new Error(data.message || res.statusText);
        return data;
    }

    async function getDashboard() {
        const res = await fetch(`${api}/dashboard`, { credentials: 'same-origin' });
        if (!res.ok) throw new Error('Не вдалося завантажити dashboard');
        return res.json();
    }

    function fmtDate(iso) {
        if (!iso) return '—';
        return new Date(iso).toLocaleString('uk-UA');
    }

    function jobHtml(job, label) {
        const cls = job.isRunning ? 'running' : (job.lastError ? 'error' : '');
        const status = job.isRunning ? '▶ працює' : (job.finishedAt ? '■ зупинено' : '○ очікує');
        let text = `<strong>${label}</strong>: ${status}`;
        if (job.lastBatchSize) text += ` · батч ${job.lastBatchSize}`;
        if (job.lastMessage) text += `<br>${job.lastMessage}`;
        if (job.lastError) text += `<br><span class="text-danger">${job.lastError}</span>`;
        if (job.startedAt) text += `<br><span class="text-muted">з ${fmtDate(job.startedAt)}</span>`;
        return `<div class="admin-job-status ${cls}">${text}</div>`;
    }

    function applyDashboard(d) {
        const s = d.stats;
        el('statsCards').querySelector('[data-stat="total"]').textContent          = s.totalGames;
        el('statsCards').querySelector('[data-stat="statusFull"]').textContent     = s.statusFull;
        el('statsCards').querySelector('[data-stat="statusBasic"]').textContent    = s.statusBasic;
        el('statsCards').querySelector('[data-stat="withPrice"]').textContent      = s.withPrice;
        el('statsCards').querySelector('[data-stat="withoutPrice"]').textContent   = s.withoutPrice;
        el('statsCards').querySelector('[data-stat="basicWithoutPrice"]').textContent = s.basicWithoutPrice;

        el('pendingDetails').textContent = s.statusBasic;
        el('lastPriceSync').textContent  = fmtDate(s.lastPriceSyncAt);

        el('enrichmentJobStatus').innerHTML = jobHtml(d.gameEnrichment, 'Збагачення');

        const price = d.priceSync;
        el('priceJobStatus').innerHTML = jobHtml(price, 'SteamSpy');

        const wrap = el('priceProgressWrap');
        if (price.isRunning && price.processed != null) {
            wrap.classList.remove('d-none');
            const total = price.total || 1;
            const pct = Math.min(100, Math.round((price.processed / total) * 100));
            el('priceProgressBar').style.width = pct + '%';
            el('priceProgressText').textContent =
                `${price.processed} / ${total} (${pct}%) · ${price.lastMessage || ''}`;
        } else if (!price.isRunning) {
            wrap.classList.add('d-none');
        }
    }

    async function refreshDashboard() {
        try {
            const d = await getDashboard();
            applyDashboard(d);
        } catch (e) {
            toast(e.message, true);
        }
    }

    async function loadGames() {
        const filter = el('gameFilter').value;
        const search = el('gameSearch').value.trim();
        const params = new URLSearchParams({
            filter,
            page: currentPage,
            pageSize: String(pageSize),
        });
        if (search) params.set('search', search);

        const res = await fetch(`${api}/games?${params}`, { credentials: 'same-origin' });
        if (!res.ok) {
            el('gamesTableBody').innerHTML =
                '<tr><td colspan="7" class="text-danger text-center">Помилка завантаження</td></tr>';
            return;
        }

        const data = await res.json();
        const tbody = el('gamesTableBody');

        if (!data.items.length) {
            tbody.innerHTML = '<tr><td colspan="7" class="text-muted text-center py-3">Немає записів</td></tr>';
        } else {
            tbody.innerHTML = data.items.map((g) => {
                const statusBadge = g.importStatus === 'Full'
                    ? '<span class="admin-badge admin-badge-yes">Full</span>'
                    : '<span class="admin-badge admin-badge-no">Basic</span>';
                const price = g.hasPrice
                    ? '<span class="admin-badge admin-badge-yes">так</span>'
                    : '<span class="admin-badge admin-badge-no">ні</span>';
                return `<tr>
                    <td>${g.gameId}</td>
                    <td>${escapeHtml(g.name)}</td>
                    <td>${g.steamExternalId ?? '—'}</td>
                    <td>${statusBadge}</td>
                    <td>${price}</td>
                    <td class="small text-muted">${fmtDate(g.lastSyncedAt)}</td>
                    <td><a href="/Catalog/Details/${g.gameId}" class="btn btn-link btn-sm p-0">→</a></td>
                </tr>`;
            }).join('');
        }

        const from = (data.page - 1) * data.pageSize + 1;
        const to = Math.min(data.page * data.pageSize, data.totalCount);
        el('gamesPagerInfo').textContent =
            data.totalCount ? `Показано ${from}–${to} з ${data.totalCount}` : '0 записів';

        el('btnPrevPage').disabled = data.page <= 1;
        el('btnNextPage').disabled = data.page * data.pageSize >= data.totalCount;
    }

    function escapeHtml(s) {
        const d = document.createElement('div');
        d.textContent = s;
        return d.innerHTML;
    }

    function startPolling() {
        if (pollTimer) clearInterval(pollTimer);
        pollTimer = setInterval(refreshDashboard, 5000);
    }

    el('btnRefreshAll').addEventListener('click', () => {
        refreshDashboard();
        loadGames();
    });

    el('btnBasicImport').addEventListener('click', async () => {
        el('btnBasicImport').disabled = true;
        el('basicImportResult').textContent = 'Імпорт…';
        try {
            const data = await post('/import/basic');
            el('basicImportResult').textContent = `Додано: ${data.imported} ігор`;
            toast(`Basic import: +${data.imported}`);
            refreshDashboard();
            loadGames();
        } catch (e) {
            el('basicImportResult').textContent = e.message;
            toast(e.message, true);
        } finally {
            el('btnBasicImport').disabled = false;
        }
    });

    el('btnEnrichStart').addEventListener('click', async () => {
        try {
            const overwrite = el('enrichOverwrite')?.checked;
            const query = overwrite ? 'overwrite=true' : '';
            await post('/import/enrich/start', query);
            toast('Збагачення запущено');
            refreshDashboard();
            startPolling();
        } catch (e) { toast(e.message, true); }
    });

    el('btnEnrichStop').addEventListener('click', async () => {
        try {
            await post('/import/enrich/stop');
            toast('Збагачення зупинено');
            refreshDashboard();
        } catch (e) { toast(e.message, true); }
    });

    el('btnPriceStart').addEventListener('click', async () => {
        const batch = el('priceBatchSize').value || '100';
        try {
            await post('/import/prices/start', `batchSize=${batch}`);
            toast('Синхронізація цін запущена');
            refreshDashboard();
            startPolling();
        } catch (e) { toast(e.message, true); }
    });

    el('btnPriceSinceStart').addEventListener('click', async () => {
        const batch = el('priceBatchSize').value || '100';
        const since = el('priceSinceDate').value;
        if (!since) {
            toast('Оберіть дату для фільтрації', true);
            return;
        }
        try {
            // since — формат YYYY-MM-DD, передаємо як ISO date
            const params = `batchSize=${batch}&notSyncedSince=${encodeURIComponent(since)}`;
            await post('/import/prices/start', params);
            toast(`Синхронізація цін (після ${since}) запущена`);
            refreshDashboard();
            startPolling();
        } catch (e) { toast(e.message, true); }
    });

    el('btnPriceStop').addEventListener('click', async () => {
        try {
            await post('/import/prices/stop');
            toast('Зупинка цін…');
            refreshDashboard();
        } catch (e) { toast(e.message, true); }
    });

    el('gameFilter').addEventListener('change', () => { currentPage = 1; loadGames(); });
    el('gameSearch').addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { currentPage = 1; loadGames(); }
    });
    el('btnPrevPage').addEventListener('click', () => { if (currentPage > 1) { currentPage--; loadGames(); } });
    el('btnNextPage').addEventListener('click', () => { currentPage++; loadGames(); });

    refreshDashboard();
    loadGames();
    startPolling();
})();