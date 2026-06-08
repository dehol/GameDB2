/**
 * price-chart.js
 * Графік цін на сторінці деталей гри.
 * Залежність: Chart.js 4.x (підключається умовно зі CDN у Details.cshtml).
 *
 * ⚠ PriceHistory.Price зберігає ОРИГІНАЛЬНУ ціну (до знижки).
 *   Ефективна ціна = Price * (1 - DiscountPercent / 100).
 */

function initPriceChart(historyData) {
    if (!historyData || historyData.length === 0) return;

    const canvas = document.getElementById('priceChart');
    if (!canvas) return;

    let chart = null;

    /* ── Допоміжні ──────────────────────────────────────────────────── */

    function formatDate(isoStr) {
        const d = new Date(isoStr);
        return d.toLocaleDateString('uk-UA', {
            day: '2-digit',
            month: 'short',
            year: 'numeric'
        });
    }

    /** Розраховує ціну після знижки. */
    function effectivePrice(originalPrice, discountPercent) {
        if (discountPercent <= 0) return Number(originalPrice);
        return Number(originalPrice) * (1 - discountPercent / 100);
    }

    /** Segment callback для Chart.js 4.x: колір ділянки між двома точками. */
    function segmentColor(points, normalColor, saleColor) {
        return function (ctx) {
            return points[ctx.p0DataIndex].discountPercent > 0 ? saleColor : normalColor;
        };
    }

    /* ── Підготовка точок ───────────────────────────────────────────── */

    /**
     * Будує масив точок:
     * 1. Рахує ефективну ціну (після знижки).
     * 2. Якщо остання реальна точка — не сьогодні, додає синтетичну
     *    "сьогоднішню" точку з тим самим станом ціни, щоб лінія
     *    продовжувалась до поточної дати.
     */
    function buildPoints(rawPoints) {
        const pts = rawPoints.map(p => ({
            date:            p.recordedAt,
            effective:       effectivePrice(p.price, p.discountPercent),
            original:        Number(p.price),
            discountPercent: p.discountPercent,
            synthetic:       false
        }));

        if (pts.length === 0) return pts;

        const last     = pts[pts.length - 1];
        const todayEOD = new Date();
        todayEOD.setHours(23, 59, 59, 999);
        const lastDate = new Date(last.date);

        // Додаємо синтетичну точку тільки якщо остання точка — не сьогодні
        if (lastDate < todayEOD && lastDate.toDateString() !== todayEOD.toDateString()) {
            pts.push({
                date:            todayEOD.toISOString(),
                effective:       last.effective,
                original:        last.original,
                discountPercent: last.discountPercent,
                synthetic:       true
            });
        }

        return pts;
    }

    /* ── Основний рендер ────────────────────────────────────────────── */

    function renderShop(idx) {
        const shop = historyData[idx];
        if (!shop || shop.points.length === 0) {
            if (chart) { chart.destroy(); chart = null; }
            return;
        }

        const currency    = shop.points[0].currency;
        const pts         = buildPoints(shop.points);

        const labels      = pts.map(p => formatDate(p.date));
        const prices      = pts.map(p => p.effective);

        const normalColor = '#0d6efd';
        const saleColor   = '#28a745';

        // Синтетична "сьогодні" точка — без крапки на графіку
        const pointRadii       = pts.map(p => p.synthetic ? 0 : 4);
        const pointHoverRadii  = pts.map(p => p.synthetic ? 0 : 7);
        const pointColors      = pts.map(p =>
            p.synthetic ? 'transparent' : (p.discountPercent > 0 ? saleColor : normalColor)
        );

        const dataset = {
            label: shop.shopName,
            data: prices,
            stepped: 'before',
            tension: 0,
            fill: true,
            borderColor: normalColor,
            backgroundColor: 'rgba(13,110,253,0.06)',
            pointRadius:          pointRadii,
            pointHoverRadius:     pointHoverRadii,
            pointBackgroundColor: pointColors,
            pointBorderColor:     pointColors,
            segment: {
                borderColor:     segmentColor(pts, normalColor, saleColor),
                backgroundColor: segmentColor(
                    pts,
                    'rgba(13,110,253,0.06)',
                    'rgba(40,167,69,0.07)'
                ),
            }
        };

        if (chart) chart.destroy();

        chart = new Chart(canvas, {
            type: 'line',
            data: { labels, datasets: [dataset] },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                animation: { duration: 250 },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            title: ctx => {
                                const p = pts[ctx[0].dataIndex];
                                return p.synthetic
                                    ? formatDate(p.date) + ' (сьогодні)'
                                    : ctx[0].label;
                            },
                            label: ctx => {
                                const p    = pts[ctx.dataIndex];
                                const disc = p.discountPercent;
                                const eff  = p.effective;

                                // "2.50 USD  −75%  (було 9.99)"
                                let line = eff.toFixed(2) + '\u00a0' + currency;
                                if (disc > 0) {
                                    line += '  \u2212' + disc + '%';
                                    line += '  (було\u00a0' + p.original.toFixed(2) + ')';
                                }
                                return line;
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        ticks: {
                            maxTicksLimit: 9,
                            maxRotation: 30,
                            font: { size: 11 }
                        },
                        grid: { color: 'rgba(0,0,0,0.04)' }
                    },
                    y: {
                        beginAtZero: false,
                        ticks: {
                            font: { size: 11 },
                            callback: v => parseFloat(v).toFixed(2) + '\u00a0' + currency
                        },
                        grid: { color: 'rgba(0,0,0,0.04)' }
                    }
                }
            }
        });
    }

    /* ── Ініціалізація ──────────────────────────────────────────────── */

    renderShop(0);

    document.querySelectorAll('[data-shop-index]').forEach(function (btn) {
        btn.addEventListener('click', function () {
            document.querySelectorAll('[data-shop-index]')
                .forEach(b => b.classList.remove('active'));
            this.classList.add('active');
            renderShop(parseInt(this.dataset.shopIndex, 10));
        });
    });
}