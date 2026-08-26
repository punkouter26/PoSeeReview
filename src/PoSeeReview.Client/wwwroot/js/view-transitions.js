// Cross-fade between routes using the View Transitions API, plus a shared-element morph from a
// tapped restaurant card into the comic view.
//
// Blazor's router does not integrate with document.startViewTransition, and there is no hook that
// brackets "DOM is about to change" / "DOM has changed". So this drives it from navigation
// instead: intercept the click on an internal link, snapshot the current document, let Blazor
// navigate, and end the transition once the new route has painted.
//
// Deliberately conservative:
//   * Unsupported browsers get today's hard cut. No polyfill, no JS-driven fade — a hand-rolled
//     crossfade of a whole SPA is exactly the kind of thing that fights the compositor.
//   * prefers-reduced-motion disables it outright. A full-page cross-fade is precisely the
//     "large moving content" reduced-motion exists to suppress.
//   * A safety timeout always resolves the transition. If Blazor throws mid-navigation, an
//     unresolved startViewTransition leaves the page frozen under a snapshot — a blank app.

const SUPPORTED = typeof document !== 'undefined' && typeof document.startViewTransition === 'function';
const SETTLE_TIMEOUT_MS = 600;

let enabled = false;
let pending = null;

function reducedMotion() {
    try {
        return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    } catch {
        return false;
    }
}

/**
 * Marks the tapped card so CSS can morph it into the comic panel. view-transition-name must be
 * unique per document, so exactly one element may carry it at a time — it is cleared on the way
 * out and re-applied per navigation.
 */
function tagMorphSource(element) {
    clearMorphTags();
    if (element) {
        element.style.viewTransitionName = 'comic-morph';
    }
}

function clearMorphTags() {
    for (const el of document.querySelectorAll('[style*="view-transition-name"]')) {
        el.style.viewTransitionName = '';
    }
}

/** Paths served by the Blazor router rather than by an API endpoint or a static file. */
const NON_SPA_PREFIXES = ['/auth', '/api', '/health', '/diag', '/_framework', '/_content'];

function isSpaRoute(pathname) {
    if (NON_SPA_PREFIXES.some((prefix) => pathname === prefix || pathname.startsWith(prefix + '/'))) {
        return false;
    }
    // A trailing extension means a static asset, not a route.
    return !/\.[a-z0-9]{2,5}$/i.test(pathname);
}

function onDocumentClick(event) {
    if (!enabled || event.defaultPrevented || event.button !== 0) return;
    if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;

    const anchor = event.target.closest('a[href]');
    if (!anchor) return;

    // Same-origin, same-tab navigations only. An external link or a download must not be
    // wrapped in a transition that never completes.
    const url = new URL(anchor.href, document.baseURI);
    if (url.origin !== location.origin) return;
    if (anchor.target && anchor.target !== '_self') return;
    if (anchor.hasAttribute('download')) return;
    if (url.pathname === location.pathname) return;

    // Only routes the Blazor router owns. A link to a server endpoint (/auth/login/microsoft,
    // /api/..., a static file) causes a FULL document navigation, and a transition opened for
    // one snapshots a document that is about to be destroyed — Blazor never renders, so nothing
    // ever calls settle(). The timeout would eventually release it, but for those hundreds of
    // milliseconds the page is frozen under a stale image for no benefit at all.
    if (!isSpaRoute(url.pathname)) return;

    // Card -> comic gets the shared-element morph; everything else is a plain cross-fade.
    const card = event.target.closest('[data-physics-card], .leaderboard-card');
    tagMorphSource(card && url.pathname.startsWith('/comic/') ? card : null);

    beginTransition();
}

/**
 * Opens a view transition and hands back a resolver that Blazor's post-render hook calls. The
 * snapshot is taken synchronously here, which is what makes the outgoing frame correct.
 */
function beginTransition() {
    if (pending) return;

    let release;
    const gate = new Promise((resolve) => { release = resolve; });

    const transition = document.startViewTransition(async () => {
        await gate;
        // One frame so Blazor's re-render is actually in the DOM before the snapshot is taken.
        await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
    });

    const timer = setTimeout(() => release(), SETTLE_TIMEOUT_MS);

    pending = {
        settle() {
            clearTimeout(timer);
            release();
            pending = null;
        }
    };

    transition.finished.finally(() => {
        clearMorphTags();
        pending = null;
    }).catch(() => { /* skipTransition or an interrupted navigation */ });
}

/**
 * A full-page navigation destroys the document mid-transition. Releasing the gate here means the
 * outgoing page is never left frozen under a snapshot while the browser tears it down.
 */
function onPageHide() {
    pending?.settle();
}

export function init() {
    enabled = SUPPORTED && !reducedMotion();
    if (enabled) {
        document.addEventListener('click', onDocumentClick, true);
        window.addEventListener('pagehide', onPageHide);
        document.documentElement.dataset.viewTransitions = 'on';
    }
    return { supported: SUPPORTED, enabled };
}

/** Called from Blazor after the destination route has rendered. */
export function settle() {
    pending?.settle();
}

export function dispose() {
    document.removeEventListener('click', onDocumentClick, true);
    window.removeEventListener('pagehide', onPageHide);
    pending?.settle();
    enabled = false;
    delete document.documentElement.dataset.viewTransitions;
}

window.poseeViewTransitions = { init, settle, dispose };
