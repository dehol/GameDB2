/**
 * price-chart.js — Динаміка цін для картки гри (GameDB)
 *
 * Вхідні дані очікуються у форматі:
 *   [{ shopName, shopColor, pricePoints: [{ date: "YYYY-MM-DD", price }] }]
 *
 * Серіалізуються server-side у <script id="priceHistoryData" type="application/json">
 * і читаються через JSON.parse у Details.cshtml.
 *
 * Структура БД (нагадування): PriceHistory дістається транзитно —
 *   Game → GameExternalIds[] → GameOffers[] → PriceHistories[]
 * Жодного прямого game.GameOffers — CS1061.
 */

const priceChart = (() => {
    /** @type {Chart|null} */
    let chart = null;

    /**
     * Збирає всі унікальні дати з усіх магазинів і сортує їх.
     * @param {Array} histories
     * @returns {string[]}
     */
    function buildLabels(histories) {
        const dates = new Set();
        histories.forEach(h =>
            h.pricePoints.forEach(p => dates.add(p.date))
        );
        return [...dates].sort();
    }

    /**
     * Будує dataset для Chart.js для одного магазину.
     * Дати без даних заповнюються null (Chart.js пропускає їх).
     * @param {{ shopName: string, shopColor: string, pricePoints: {date:string,price:number}[] }} history
     * @param {string[]} allLabels
     * @returns {object}
     */
    function buildDataset(history, allLabels) {
        const priceMap = new Map(
            history.pricePoints.map(p => [p.date, p.price])
        );
        return {
            label:                history.shopName,
            data:                 allLabels.map(d => priceMap.get(d) ?? null),
            borderColor:          history.shopColor,
            backgroundColor:      hexToRgba(history.shopColor, 0.08),
            borderWidth:          2,
            pointRadius:          3,
            pointHoverRadius:     5,
            pointBackgroundColor: history.shopColor,
            tension:              0.3,
            fill:                 false,
            spanGaps:             true,   // з'єднує розриви, де ціна відсутня
        };
    }

    /**
     * Ініціалізує Chart.js на вказаному canvas.
     * @param {string} canvasId
     * @param {Array}  histories
     */
    function init(canvasId, histories) {
        if (!histories || histories.length === 0) return;

        const ctx = document.getElementById(canvasId);
        if (!ctx) return;

        const labels   = buildLabels(histories);
        const datasets = histories.map(h => buildDataset(h, labels));

        // Знищуємо попередній екземпляр, якщо є (при hot-reload)
        if (chart) {
            chart.destroy();
            chart = null;
        }

        chart = new Chart(ctx, {
            type: 'line',
            data: { labels, datasets },
            options: {
                responsive:          true,
                maintainAspectRatio: false,
                interaction: {
                    mode:      'index',
                    intersect: false,
                },
                plugins: {
                    legend: {
                        display:  histories.length > 1,
                        position: 'top',
                        labels: {
                            boxWidth: 12,
                            font:     { size: 12 },
                        },
                    },
                    tooltip: {
                        callbacks: {
                            label(ctx) {
                                const val = ctx.parsed.y;
                                return val === null
                                    ? `${ctx.dataset.label}: —`
                                    : `${ctx.dataset.label}: ${val.toFixed(2)}`;
                            },
                        },
                    },
                },
                scales: {
                    x: {
                        type:         'category',
                        ticks: {
                            maxTicksLimit: 10,
                            maxRotation:   30,
                            font:          { size: 11 },
                            callback(val, idx) {
                                // Показуємо дату скорочено: "Jan '24"
                                const d = new Date(this.getLabelForValue(idx));
                                return d.toLocaleDateString('uk-UA', {
                                    month: 'short',
                                    year:  '2-digit',
                                });
                            },
                        },
                        grid: { display: false },
                    },
                    y: {
                        beginAtZero: false,
                        ticks: {
                            font: { size: 11 },
                            callback(val) {
                                return val === 0 ? 'Безкоштовно' : val.toFixed(2);
                            },
                        },
                        grid: {
                            color: 'rgba(0,0,0,0.05)',
                        },
                    },
                },
            },
        });
    }

    /**
     * Показує/приховує датасети за назвою магазину.
     * @param {string}      shopName  'all' або назва конкретного магазину
     * @param {HTMLElement} btn       кнопка, що була натиснута (для підсвічення)
     */
    function showShops(shopName, btn) {
        if (!chart) return;

        chart.data.datasets.forEach(ds => {
            ds.hidden = shopName !== 'all' && ds.label !== shopName;
        });
        chart.update('active');

        // Оновлення активного стану кнопок
        const tabs = document.getElementById('shopTabs');
        if (tabs) {
            tabs.querySelectorAll('button').forEach(b => b.classList.remove('active'));
            if (btn) btn.classList.add('active');
        }
    }

    // ── Утиліта: hex → rgba ────────────────────────────────────────────────
    function hexToRgba(hex, alpha) {
        const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
        if (!result) return `rgba(99,102,241,${alpha})`;
        const [, r, g, b] = result.map(x => parseInt(x, 16));
        return `rgba(${r},${g},${b},${alpha})`;
    }

    return { init, showShops };
})();
