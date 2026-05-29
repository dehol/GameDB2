(function () {
    const modal = document.getElementById('priceAlertModal');
    if (!modal) return;

    const input = document.getElementById('alertTargetPrice');
    const currency = modal.dataset.currency || '';
    const cur = parseFloat(modal.dataset.currentLowest || '0') || null;
    const hist = parseFloat(modal.dataset.historicalLow || '0') || null;
    const base = parseFloat(modal.dataset.basePrice || '0') || null;

    function fmt(v) {
        if (v == null || isNaN(v)) return '—';
        return v.toFixed(2) + (currency ? ' ' + currency : '');
    }

    function setTarget(v) {
        if (input && v != null && !isNaN(v) && v > 0)
            input.value = v.toFixed(2);
    }

    function pct(price, percent) {
        return price * (1 - percent / 100);
    }

    function beat(price) {
        return price >= 1 ? Math.max(0.01, price - 0.01) : price * 0.99;
    }

    modal.querySelectorAll('[data-price-action]').forEach(btn => {
        btn.addEventListener('click', () => {
            const action = btn.dataset.priceAction;
            const group = btn.dataset.priceGroup;
            const pctVal = parseFloat(btn.dataset.pct || '0');

            let ref = null;
            if (group === 'current' && cur) ref = cur;
            else if (group === 'historical' && hist) ref = hist;
            else if (group === 'base' && base) ref = base;

            if (!ref) return;

            switch (action) {
                case 'match': setTarget(ref); break;
                case 'beat': setTarget(beat(ref)); break;
                case 'pct': setTarget(pct(ref, pctVal)); break;
                case 'beat-current': if (cur) setTarget(beat(cur)); break;
                case 'match-historical': if (hist) setTarget(hist); break;
            }
        });
    });

    if (modal.dataset.open === 'true') {
        new bootstrap.Modal(modal).show();
    }
})();
