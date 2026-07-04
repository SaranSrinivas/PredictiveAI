// Toggles a `has-more-below` class on the nearest `.grid-wrap` ancestor of each
// matched scroll container, based on whether it's actually scrollable and not
// already scrolled to the bottom. Drives the counter hint + bouncing chevron.
export function watch(selector) {
    document.querySelectorAll(selector).forEach(el => {
        if (el.dataset.scrollHintBound) return;
        el.dataset.scrollHintBound = '1';

        const root = el.closest('.grid-wrap');
        if (!root) return;

        const update = () => {
            const scrollable = el.scrollHeight - el.clientHeight > 2;
            const atBottom = el.scrollTop + el.clientHeight >= el.scrollHeight - 2;
            root.classList.toggle('has-more-below', scrollable && !atBottom);
        };

        el.addEventListener('scroll', update, { passive: true });
        new ResizeObserver(update).observe(el);
        update();
    });
}
