// Reads resolved design-token values out of the live stylesheet.
//
// This exists for exactly one reason: SVG presentation attributes do not resolve `var()`.
// Radzen chart series take a colour as a string and write it onto the SVG as `fill="..."`, so
// passing `var(--color-brand)` produces no colour at all. Rather than hardcode hex codes into a
// component — which is the thing the design system forbids, and which renders wrong in the
// other theme — the page asks the browser what the token currently resolves to.
//
// Published as `window.poseeTheme`, matching the existing `window.geolocation` /
// `window.shareUtils` / `window.poseeFx` convention.
window.poseeTheme = {
    /**
     * Resolves a list of custom property names against :root.
     * @param {string[]} names - Token names, with or without the leading `--`.
     * @returns {string[]} Trimmed values, positionally matching `names`. A token that does not
     *                     resolve comes back as an empty string, never a guessed colour.
     */
    readTokens: function (names) {
        try {
            const styles = getComputedStyle(document.documentElement);
            return (names || []).map(function (name) {
                const key = name.startsWith('--') ? name : '--' + name;
                return (styles.getPropertyValue(key) || '').trim();
            });
        } catch {
            // A page that cannot read its own tokens still has to render. Callers treat an
            // empty string as "let the component pick its own default".
            return (names || []).map(function () { return ''; });
        }
    },

    /**
     * Calls back into .NET whenever the OS colour scheme flips, so a chart drawn with resolved
     * hex values can re-resolve them instead of keeping light-mode colours on a dark ground.
     * @param {object} dotNetRef - DotNetObjectReference exposing `OnThemeChanged`.
     * @returns {boolean} True when the listener was attached.
     */
    watchScheme: function (dotNetRef) {
        try {
            const query = window.matchMedia('(prefers-color-scheme: dark)');
            const handler = function () { dotNetRef.invokeMethodAsync('OnThemeChanged'); };
            query.addEventListener('change', handler);
            // Kept so the component can detach on dispose; one page at a time, so one slot.
            window.poseeTheme._scheme = { query: query, handler: handler };
            return true;
        } catch {
            return false;
        }
    },

    /** Detaches the listener installed by `watchScheme`. Safe to call when none was installed. */
    unwatchScheme: function () {
        try {
            const saved = window.poseeTheme._scheme;
            if (saved) {
                saved.query.removeEventListener('change', saved.handler);
                window.poseeTheme._scheme = null;
            }
        } catch {
            /* Nothing to detach. */
        }
    }
};
