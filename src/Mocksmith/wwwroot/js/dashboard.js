// Lazy-loads dashboard tile previews: iframes get their src only when scrolled
// into view, and at most MAX_CONCURRENT_LOADS load simultaneously.
window.mocksmithDashboard = (() => {
    const MAX_CONCURRENT_LOADS = 6;
    let activeLoads = 0;
    const queue = [];

    function startLoad(frame) {
        activeLoads++;
        frame.addEventListener('load', onLoadSettled, { once: true });
        frame.addEventListener('error', onLoadSettled, { once: true });
        frame.src = frame.getAttribute('data-preview-src');
        frame.removeAttribute('data-preview-src');
    }

    function onLoadSettled() {
        activeLoads--;
        pump();
    }

    function pump() {
        while (activeLoads < MAX_CONCURRENT_LOADS && queue.length > 0) {
            const frame = queue.shift();
            if (frame.isConnected) {
                startLoad(frame);
            }
        }
    }

    const observer = new IntersectionObserver((entries) => {
        for (const entry of entries) {
            if (!entry.isIntersecting) {
                continue;
            }
            observer.unobserve(entry.target);
            queue.push(entry.target);
        }
        pump();
    }, { rootMargin: '200px' });

    function observeTiles() {
        document
            .querySelectorAll('iframe[data-preview-src]:not([data-observed])')
            .forEach((frame) => {
                frame.setAttribute('data-observed', 'true');
                observer.observe(frame);
            });
    }

    return { observeTiles };
})();
