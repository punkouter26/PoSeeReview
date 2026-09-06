// A bounded pool of WebGL2 contexts, shared by every effect in the app.
//
// THE PROBLEM THIS SOLVES.
//
// Every effect module used to call canvas.getContext('webgl2') on its own canvas. On the comic
// page that is four live contexts at once — backdrop gradient, comic post-process, particle
// burst, loading ring — each with its own GL state machine, its own driver-side command buffer,
// and its own share of a hard browser limit (Chromium evicts at 16, Safari lower). Eviction is
// silent: the oldest context receives a 'webglcontextlost' event and simply stops drawing. Under
// repeated SPA navigation, contexts also linger until GC even after loseContext(), so the real
// peak is higher than the live count suggests.
//
// THE DESIGN.
//
// One WebGL2 context renders everything into a private ATLAS canvas. Each effect gets a SURFACE:
// a reserved rectangular band of that atlas plus its own framebuffer object. Per frame a surface
// binds its FBO, draws, then blits its band to the real on-screen canvas with a 2D drawImage()
// carrying a source crop.
//
// Why an atlas of bands rather than resizing one shared canvas per effect: resizing a WebGL
// drawing buffer reallocates it driver-side. Four effects at different sizes would mean four
// reallocations per frame, which costs far more than the contexts we are saving. Bands are
// allocated once, at the size the effect asks for, and only re-packed when a size changes.
//
// preserveDrawingBuffer is ON deliberately. drawImage() reads the drawing buffer, and without
// preservation the spec permits the browser to have cleared it by the time we read. We draw and
// present inside the same rAF callback so in practice it survives, but "in practice" is not a
// guarantee worth a blank comic panel.
//
// FALLBACK. No OffscreenCanvas, or a browser that refuses the atlas size, drops an effect back
// to its own directly-attached context — exactly the old behaviour. Pooling is an optimisation,
// and an optimisation that cannot fail closed is not one worth shipping.

const MAX_ATLAS_DIM = 4096;
const MAX_POOLED_SURFACES = 8;

const CONTEXT_ATTRIBUTES = {
    alpha: true,
    antialias: false,          // Post-process passes; MSAA buys nothing and costs fill rate.
    depth: false,
    stencil: false,
    premultipliedAlpha: true,
    powerPreference: 'low-power',
    preserveDrawingBuffer: true,
    desynchronized: false      // Incompatible with reading the buffer back via drawImage.
};

const stats = {
    pooledContexts: 0,
    directContexts: 0,
    surfaces: 0,
    atlasRepacks: 0,
    contextLosses: 0
};

let atlas = null;
let atlasAttempted = false;

function supportsPooling() {
    try {
        return typeof OffscreenCanvas === 'function';
    } catch {
        return false;
    }
}

/** The single atlas context. Created lazily so a page with no effects pays nothing. */
function ensureAtlas() {
    if (atlasAttempted) {
        return atlas?.gl ? atlas : null;
    }
    atlasAttempted = true;

    if (!supportsPooling()) {
        return null;
    }

    const created = { canvas: null, gl: null, surfaces: new Set(), width: 0, height: 0, lost: false };

    try {
        created.canvas = new OffscreenCanvas(1, 1);
        created.gl = created.canvas.getContext('webgl2', CONTEXT_ATTRIBUTES);
    } catch {
        created.gl = null;
    }

    if (!created.gl) {
        return null;
    }

    atlas = created;
    stats.pooledContexts = 1;

    // A lost atlas takes every pooled effect down at once, so it must be visible rather than
    // silent. Each surface is marked dead; its owner sees present() return false and tears down.
    try {
        created.canvas.addEventListener('webglcontextlost', (event) => {
            event.preventDefault();
            stats.contextLosses++;
            created.lost = true;
            for (const surface of created.surfaces) {
                surface.dead = true;
            }
            console.warn('[gl-pool] atlas context lost; effects fall back to direct contexts');
        });
    } catch {
        // OffscreenCanvas without EventTarget in older engines. Loss is then undetectable, which
        // degrades to a blank overlay over an intact DOM — the same outcome as no effect at all.
    }

    return atlas;
}

/**
 * Lays every live surface out as a vertical stack of bands and grows the atlas to fit.
 * Called only when a surface is added, removed, or resized — never per frame.
 */
function repack() {
    if (!atlas?.gl) return false;

    let width = 1;
    let height = 0;
    for (const surface of atlas.surfaces) {
        width = Math.max(width, surface.width);
        height += surface.height;
    }
    height = Math.max(1, height);

    if (width > MAX_ATLAS_DIM || height > MAX_ATLAS_DIM) {
        return false;
    }

    if (atlas.width !== width || atlas.height !== height) {
        atlas.canvas.width = width;
        atlas.canvas.height = height;
        atlas.width = width;
        atlas.height = height;
        stats.atlasRepacks++;
    }

    let y = 0;
    for (const surface of atlas.surfaces) {
        surface.originY = y;
        y += surface.height;
    }
    return true;
}

/** (Re)allocates a surface's colour attachment to match its current size. */
function allocateTarget(surface) {
    const { gl } = surface;

    if (surface.texture) gl.deleteTexture(surface.texture);
    if (surface.fbo) gl.deleteFramebuffer(surface.fbo);

    surface.texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, surface.texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, surface.width, surface.height, 0,
        gl.RGBA, gl.UNSIGNED_BYTE, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);

    surface.fbo = gl.createFramebuffer();
    gl.bindFramebuffer(gl.FRAMEBUFFER, surface.fbo);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, surface.texture, 0);

    const complete = gl.checkFramebufferStatus(gl.FRAMEBUFFER) === gl.FRAMEBUFFER_COMPLETE;
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    gl.bindTexture(gl.TEXTURE_2D, null);
    return complete;
}

function measure(canvas, maxDpr) {
    const dpr = Math.min(window.devicePixelRatio || 1, maxDpr);
    return {
        width: Math.max(1, Math.floor((canvas.clientWidth || canvas.width || 1) * dpr)),
        height: Math.max(1, Math.floor((canvas.clientHeight || canvas.height || 1) * dpr))
    };
}

// ── Direct surface: the fallback, and the pre-pool behaviour ─────────────────────────────

function createDirectSurface(canvas, maxDpr) {
    let gl = null;
    try {
        gl = canvas.getContext('webgl2', { ...CONTEXT_ATTRIBUTES, preserveDrawingBuffer: false });
    } catch {
        gl = null;
    }
    if (!gl) return null;

    stats.directContexts++;
    stats.surfaces++;

    const surface = {
        pooled: false,
        gl,
        canvas,
        width: 0,
        height: 0,
        dead: false,

        beginFrame() {
            if (surface.dead) return false;
            const { width, height } = measure(canvas, maxDpr);
            if (canvas.width !== width || canvas.height !== height) {
                canvas.width = width;
                canvas.height = height;
            }
            surface.width = canvas.width;
            surface.height = canvas.height;
            surface.bindTarget();
            gl.disable(gl.BLEND);
            gl.disable(gl.DEPTH_TEST);
            gl.disable(gl.SCISSOR_TEST);
            return true;
        },

        /**
         * Re-binds this surface as the draw target. Multi-pass effects bind their own
         * intermediate framebuffers and need a way back that does not assume which framebuffer
         * a surface is actually backed by — pooled surfaces render to an FBO, direct ones to the
         * default framebuffer, and an effect should not have to know which it got.
         */
        bindTarget() {
            gl.bindFramebuffer(gl.FRAMEBUFFER, null);
            gl.viewport(0, 0, canvas.width, canvas.height);
        },

        // Nothing to do: the effect drew straight to the visible canvas.
        present: () => !surface.dead,

        release() {
            if (surface.dead) return;
            surface.dead = true;
            stats.directContexts--;
            stats.surfaces--;
            try { gl.getExtension('WEBGL_lose_context')?.loseContext(); } catch { /* optional */ }
        }
    };

    const initial = measure(canvas, maxDpr);
    surface.width = initial.width;
    surface.height = initial.height;
    return surface;
}

// ── Pooled surface ───────────────────────────────────────────────────────────────────────

function createPooledSurface(canvas, maxDpr) {
    const active = ensureAtlas();
    if (!active || active.lost || active.surfaces.size >= MAX_POOLED_SURFACES) {
        return null;
    }

    // The presentation path. bitmaprenderer cannot crop, and every surface shares one atlas, so
    // a 2D context with a source rectangle is the only way to hand a band to its own canvas.
    let present2d = null;
    try {
        present2d = canvas.getContext('2d', { alpha: true, desynchronized: true });
    } catch {
        present2d = null;
    }
    if (!present2d) {
        return null;
    }

    const { gl } = active;
    const initial = measure(canvas, maxDpr);

    const surface = {
        pooled: true,
        gl,
        canvas,
        width: initial.width,
        height: initial.height,
        originY: 0,
        texture: null,
        fbo: null,
        dead: false,

        beginFrame() {
            if (surface.dead || active.lost) return false;

            const { width, height } = measure(canvas, maxDpr);
            if (width !== surface.width || height !== surface.height) {
                surface.width = width;
                surface.height = height;
                if (!repack() || !allocateTarget(surface)) {
                    surface.dead = true;
                    return false;
                }
            }
            if (canvas.width !== surface.width || canvas.height !== surface.height) {
                canvas.width = surface.width;
                canvas.height = surface.height;
            }

            surface.bindTarget();
            gl.disable(gl.BLEND);
            gl.disable(gl.DEPTH_TEST);
            gl.disable(gl.SCISSOR_TEST);
            gl.clearColor(0, 0, 0, 0);
            gl.clear(gl.COLOR_BUFFER_BIT);
            return true;
        },

        /** See the direct surface's bindTarget — same contract, different backing store. */
        bindTarget() {
            gl.bindFramebuffer(gl.FRAMEBUFFER, surface.fbo);
            gl.viewport(0, 0, surface.width, surface.height);
        },

        present() {
            if (surface.dead || active.lost) return false;

            // Straight copy into this surface's band of the atlas default framebuffer — NO flip.
            //
            // It is tempting to invert the source rectangle here "to cancel the flip drawImage
            // introduces". drawImage introduces no flip: a 2D context reading a WebGL canvas sees
            // the canvas as the browser presents it, which is already the GL buffer flipped for
            // display. Adding a second flip here renders every effect upside down — invisible on
            // the radially symmetric ones (the gradient's noise, the loading ring) and obvious the
            // moment anything has a top and a bottom.
            gl.bindFramebuffer(gl.READ_FRAMEBUFFER, surface.fbo);
            gl.bindFramebuffer(gl.DRAW_FRAMEBUFFER, null);
            gl.blitFramebuffer(
                0, 0, surface.width, surface.height,
                0, surface.originY, surface.width, surface.originY + surface.height,
                gl.COLOR_BUFFER_BIT, gl.NEAREST);
            gl.bindFramebuffer(gl.FRAMEBUFFER, null);

            // The band's origin is in GL coordinates (bottom-left), and drawImage's source
            // rectangle is in image coordinates (top-left). Converting between them is the whole
            // reason this is not simply `originY`.
            const sourceY = Math.max(0, active.height - (surface.originY + surface.height));

            try {
                present2d.clearRect(0, 0, surface.width, surface.height);
                present2d.drawImage(
                    active.canvas,
                    0, sourceY, surface.width, surface.height,
                    0, 0, surface.width, surface.height);
            } catch {
                surface.dead = true;
                return false;
            }
            return true;
        },

        release() {
            if (!surface.fbo && surface.dead) return;
            surface.dead = true;
            active.surfaces.delete(surface);
            stats.surfaces--;
            try {
                if (surface.texture) gl.deleteTexture(surface.texture);
                if (surface.fbo) gl.deleteFramebuffer(surface.fbo);
            } catch { /* context already gone */ }
            surface.texture = null;
            surface.fbo = null;
            repack();
        }
    };

    active.surfaces.add(surface);
    if (!repack() || !allocateTarget(surface)) {
        active.surfaces.delete(surface);
        try {
            if (surface.texture) gl.deleteTexture(surface.texture);
            if (surface.fbo) gl.deleteFramebuffer(surface.fbo);
        } catch { /* nothing allocated */ }
        return null;
    }

    stats.surfaces++;
    return surface;
}

/**
 * Gets a render surface for a canvas. Prefers the shared atlas; falls back to a directly
 * attached context. Returns null only when the device has no usable WebGL2 at all.
 *
 * @param {HTMLCanvasElement} canvas on-screen target
 * @param {{ maxDpr?: number, direct?: boolean }} options
 *        direct:true forces a private context — for effects that need the default framebuffer
 *        itself, or that a caller has measured to be better off unpooled.
 */
export function acquireSurface(canvas, options = {}) {
    if (!canvas) return null;
    const maxDpr = options.maxDpr ?? 2;

    if (!options.direct) {
        const pooled = createPooledSurface(canvas, maxDpr);
        if (pooled) return pooled;
    }
    return createDirectSurface(canvas, maxDpr);
}

export function poolStats() {
    return {
        ...stats,
        atlasWidth: atlas?.width ?? 0,
        atlasHeight: atlas?.height ?? 0,
        atlasLost: atlas?.lost ?? false
    };
}
