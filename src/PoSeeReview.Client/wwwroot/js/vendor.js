// Loaders for third-party code fetched on first use (MapLibre, Radzen). Styles are fetched and
// wrapped in @layer vendor rather than linked: an unlayered sheet beats every layer in app.css
// (see its header), and `vendor` sits below `page` and `shared` so the app's own styles win.

/** Appends a classic script once; resolves when it has run. `ready()` true skips the load. */
export function loadScript(src, { integrity, ready } = {}) {
    if (ready?.()) return Promise.resolve();
    return new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = src;
        if (integrity) {
            script.integrity = integrity;
            script.crossOrigin = 'anonymous';
        }
        script.onload = resolve;
        script.onerror = () => reject(new Error(`${src} failed to load`));
        document.head.append(script);
    });
}

/** Fetches a stylesheet into `@layer vendor`, once per `name`. `rewrite` adjusts the CSS text. */
export async function loadLayeredStyle(name, url, { init, rewrite } = {}) {
    if (document.querySelector(`style[data-vendor="${name}"]`)) return;
    const response = await fetch(url, init);
    if (!response.ok) throw new Error(`${name} stylesheet ${response.status}`);
    const css = await response.text();
    const style = document.createElement('style');
    style.dataset.vendor = name;
    style.textContent = `@layer vendor {\n${rewrite ? rewrite(css) : css}\n}`;
    document.head.append(style);
}
