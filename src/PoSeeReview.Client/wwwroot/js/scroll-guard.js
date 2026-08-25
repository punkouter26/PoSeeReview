// Adds `.is-scrolling` to <body> while the page is moving, so app.css can drop backdrop-filter
// on glass surfaces for the duration.
//
// Why this is JS and not a Blazor @onscroll handler: scroll fires at display rate, and routing
// each event through JS interop into .NET and back would cost more than the blur it is trying to
// save. This never touches .NET at all — it toggles one class from a passive listener.
//
// The listener is passive because it never calls preventDefault; a non-passive scroll listener
// forces the browser to wait for JS before it can scroll, which is the exact stutter this file
// exists to remove.

const IDLE_DELAY_MS = 140;

let timer = 0;
let scrolling = false;

function onScroll() {
    if (!scrolling) {
        scrolling = true;
        document.body.classList.add('is-scrolling');
    }

    clearTimeout(timer);
    timer = setTimeout(() => {
        scrolling = false;
        document.body.classList.remove('is-scrolling');
    }, IDLE_DELAY_MS);
}

window.addEventListener('scroll', onScroll, { passive: true, capture: true });

// Returned to Blazor as the module's disposal hook: DotNet calls dispose() on the
// IJSObjectReference, which runs this and detaches the listener.
export function dispose() {
    window.removeEventListener('scroll', onScroll, { capture: true });
    clearTimeout(timer);
    document.body.classList.remove('is-scrolling');
}
