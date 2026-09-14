// A small Verlet solver, and the two effects that earn it.
//
// WHY A PHYSICS ENGINE IS BACK AFTER RAPIER WAS DELETED.
//
// Rapier went because ~2.4MB of WASM solver was being spent to jiggle a card grid that already
// worked — the simulation said nothing the markup did not, and every visitor paid for it on
// first load. That verdict was about the trade, not about physics. So the shelf.js conditions
// apply here instead: NO LIBRARY (this file is the whole solver), LAZY (dynamic import, one
// route, on one event), `full` TIER ONLY, and the real DOM element stays exactly where it is.
//
// What physics buys that a keyframe cannot: the existing particle burst is simulated entirely in
// the vertex shader from immutable seeds, which is why 1500 particles cost the same as 20 — but
// it also means no particle can ever know about another one, or about the floor. Ink that is
// thrown at a page LANDS. It pools, it piles unevenly, and where it piles depends on where the
// last drop went. That is state, and stateless-by-design is exactly what the shader sim is.
//
// WHY 2D CANVAS AND NOT gl-pool. This never asks for a WebGL context, so it does not touch the
// pooled atlas or its surface budget — a hundred soft sprites is a trivial 2D workload, and
// adding a ninth surface to a pool capped at eight to save nothing would be a bad trade. It does
// register with the shared rAF loop like everything else, so its cost lands in the same frame
// budget that can downgrade it.
//
// SOLVER NOTES.
//
//   * Verlet, not Euler. Position and previous position ARE the velocity, so a collision is
//     resolved by moving a body — no separate velocity to keep in sync, and stacking is stable
//     at low substep counts, which is the entire behaviour these effects are built on.
//   * A uniform spatial hash, rebuilt per substep. Cell size is the largest body diameter, so a
//     body can only touch its own cell and the eight around it. Brute force at N=90 is 4000
//     pairs per substep and would work; the grid is 30 lines and makes the count irrelevant.
//   * Bodies fall ASLEEP. Once a body's movement stays under a threshold for a few frames it is
//     skipped by integration and collision alike. Pooled ink is the steady state of both these
//     effects, so within about a second most of the work stops happening at all.

import { gfx } from './gfx-core.js';

const SUBSTEPS = 3;
const SLEEP_EPSILON = 0.06;     // px of movement per frame below which a body is a candidate.
const SLEEP_FRAMES = 8;         // consecutive quiet frames before it stops being simulated.
const MAX_BODIES = 160;
const FIXED_DT = 1 / 60;        // Verlet is not time-step invariant; a fixed step keeps it sane.

/**
 * One pre-tinted soft sprite per palette colour, built once and scaled per body.
 *
 * Drawing a blob is then a single `drawImage` with no state change. The obvious alternatives are
 * both worse in the same way — a per-blob radial gradient rebuilds the gradient object ~90 times
 * a frame, and stamping one white mask through a `clip()` costs a save/clip/fill/restore per
 * body. At this count the whole effect is bounded by 2D-context state changes rather than by
 * fill rate, so removing them is the optimisation that matters.
 *
 * The soft edge is the other half of the look: opaque well past the middle, then a fast falloff.
 * A slow falloff reads as fog; ink has a definite edge with a thin wet halo, and that halo is
 * what merges neighbouring drops into a pool instead of a bag of circles.
 */
let sprites = null;
let spritePaletteKey = '';

function buildSprite(color) {
    const size = 64;
    const canvas = document.createElement('canvas');
    canvas.width = size;
    canvas.height = size;
    const ctx = canvas.getContext('2d');
    if (!ctx) return null;

    const gradient = ctx.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
    gradient.addColorStop(0, 'rgba(0,0,0,1)');
    gradient.addColorStop(0.62, 'rgba(0,0,0,0.98)');
    gradient.addColorStop(0.86, 'rgba(0,0,0,0.42)');
    gradient.addColorStop(1, 'rgba(0,0,0,0)');

    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, size, size);

    // source-in keeps the alpha already laid down and replaces the colour, which is how the tint
    // is baked in without a second canvas or a per-frame composite mode.
    ctx.globalCompositeOperation = 'source-in';
    ctx.fillStyle = color;
    ctx.fillRect(0, 0, size, size);

    return canvas;
}

/**
 * Rebuilt whenever the resolved palette changes, so a theme flip mid-effect does not keep
 * stamping light-mode ink onto a dark page.
 */
function ensureSprites(palette) {
    const key = palette.join('|');
    if (sprites && spritePaletteKey === key) return sprites;

    const built = palette.map(buildSprite);
    if (built.some(s => !s)) return null;

    sprites = built;
    spritePaletteKey = key;
    return sprites;
}

/**
 * Emoji sprites for the reaction pile, rendered once per glyph and cached.
 *
 * Separate from the ink sprites because they are a different kind of thing: ink is tinted from
 * the design tokens and must follow the theme, whereas an emoji is a glyph the platform draws
 * and there is nothing to tint. Keyed by the character, so the four reactions cost four canvases
 * for the life of the page no matter how many bodies are in flight.
 *
 * Drawn at 96px and scaled down per body. Rendering at the body's own radius would mean a new
 * canvas per size, and the sizes vary continuously.
 */
const glyphSprites = new Map();

function glyphSprite(glyph) {
    const cached = glyphSprites.get(glyph);
    if (cached !== undefined) return cached;

    let sprite = null;
    try {
        const size = 96;
        const canvas = document.createElement('canvas');
        canvas.width = size;
        canvas.height = size;
        const ctx = canvas.getContext('2d');
        if (ctx) {
            ctx.font = `${Math.round(size * 0.78)}px system-ui, "Apple Color Emoji", "Segoe UI Emoji", sans-serif`;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText(glyph, size / 2, size / 2 + size * 0.04);
            sprite = canvas;
        }
    } catch {
        sprite = null;
    }

    // Cached even when null: a platform that cannot render the glyph will not start being able
    // to, and retrying per body would allocate a canvas per frame.
    glyphSprites.set(glyph, sprite);
    return sprite;
}

/** Fixed-capacity, struct-of-arrays. One allocation per world, none per frame. */
function createWorld(capacity) {
    const n = Math.min(MAX_BODIES, capacity);
    return {
        count: 0,
        capacity: n,
        x: new Float32Array(n),
        y: new Float32Array(n),
        px: new Float32Array(n),
        py: new Float32Array(n),
        radius: new Float32Array(n),
        // Index into the pre-tinted sprite set. Render-only; the solver never reads it.
        tint: new Uint8Array(n),
        // Emoji drawn instead of an ink blob, for the reaction pile. Render-only, and a plain
        // array because these are strings — the solver stays entirely typed-array.
        glyph: new Array(n).fill(null),
        // Accumulated tumble, in radians. Also render-only: Verlet has no angular term, so this
        // is integrated from horizontal travel in the draw loop rather than solved. A reaction
        // that lands face-up every time reads as a sprite being placed, not as an object falling.
        spin: new Float32Array(n),
        quiet: new Uint8Array(n),
        asleep: new Uint8Array(n),
        width: 0,
        height: 0,
        gravity: 1400,
        damping: 0.992,
        restitution: 0.28,
        // Spatial hash, allocated on first step once the cell size is known.
        cellSize: 0,
        cols: 0,
        rows: 0,
        heads: null,
        next: null,
        // Ring-buffer overwrite once full. Off for the one-shot presets, which seed exactly once
        // and would only lose bodies to it; on for the pile, which is fed by taps.
        recycle: false,
        oldest: 0
    };
}

/** Fills one slot. Split out so a fresh body and a recycled one cannot drift apart. */
function writeBody(world, i, x, y, vx, vy, radius, tint, glyph) {
    world.x[i] = x;
    world.y[i] = y;
    // Verlet seeds velocity as a position offset, so it must be scaled by the timestep.
    world.px[i] = x - vx * FIXED_DT;
    world.py[i] = y - vy * FIXED_DT;
    world.radius[i] = radius;
    world.tint[i] = tint;
    world.glyph[i] = glyph;
    world.spin[i] = Math.random() * Math.PI * 2;
    world.quiet[i] = 0;
    world.asleep[i] = 0;
}

function addBody(world, x, y, vx, vy, radius, tint, glyph = null) {
    // The pile is the one preset that can outlive its own capacity: a user who keeps tapping
    // reactions would otherwise hit the cap and have later taps silently do nothing. Recycling
    // the OLDEST body keeps the newest tap visible, which is the one the user is looking at.
    if (world.count >= world.capacity) {
        if (!world.recycle) return -1;
        const i = world.oldest;
        world.oldest = (world.oldest + 1) % world.capacity;
        writeBody(world, i, x, y, vx, vy, radius, tint, glyph);
        return i;
    }
    const i = world.count++;
    writeBody(world, i, x, y, vx, vy, radius, tint, glyph);
    return i;
}

function rebuildGrid(world) {
    let maxRadius = 1;
    for (let i = 0; i < world.count; i++) {
        if (world.radius[i] > maxRadius) maxRadius = world.radius[i];
    }
    const cell = Math.max(4, maxRadius * 2);
    const cols = Math.max(1, Math.ceil(world.width / cell));
    const rows = Math.max(1, Math.ceil(world.height / cell));

    if (world.cols !== cols || world.rows !== rows || !world.heads) {
        world.heads = new Int32Array(cols * rows);
        world.next = new Int32Array(world.capacity);
        world.cols = cols;
        world.rows = rows;
    }
    world.cellSize = cell;
}

/** Singly-linked buckets: heads[cell] is the first body index, next[i] the one after it. */
function fillGrid(world) {
    world.heads.fill(-1);
    const { cellSize, cols, rows } = world;
    for (let i = 0; i < world.count; i++) {
        const cx = Math.min(cols - 1, Math.max(0, (world.x[i] / cellSize) | 0));
        const cy = Math.min(rows - 1, Math.max(0, (world.y[i] / cellSize) | 0));
        const cell = cy * cols + cx;
        world.next[i] = world.heads[cell];
        world.heads[cell] = i;
    }
}

/**
 * Positional collision response. Each pair is pushed apart along its centre line by half the
 * overlap each — no impulse, no velocity term. In Verlet that IS the impulse: the body moved,
 * its previous position did not, so next integration reads the separation as velocity.
 */
function resolveCollisions(world) {
    const { cols, rows, heads, next } = world;

    for (let cy = 0; cy < rows; cy++) {
        for (let cx = 0; cx < cols; cx++) {
            let a = heads[cy * cols + cx];
            while (a !== -1) {
                // Own cell (only bodies later in this bucket, so no pair is visited twice) plus
                // the four neighbours forward of it — the other four see this cell as forward.
                pairAgainstCell(world, a, next[a]);
                if (cx + 1 < cols) pairAgainstCell(world, a, heads[cy * cols + cx + 1]);
                if (cy + 1 < rows) {
                    pairAgainstCell(world, a, heads[(cy + 1) * cols + cx]);
                    if (cx + 1 < cols) pairAgainstCell(world, a, heads[(cy + 1) * cols + cx + 1]);
                    if (cx > 0) pairAgainstCell(world, a, heads[(cy + 1) * cols + cx - 1]);
                }
                a = next[a];
            }
        }
    }
}

function pairAgainstCell(world, a, start) {
    const { x, y, radius, asleep, next } = world;
    for (let b = start; b !== -1; b = next[b]) {
        if (asleep[a] && asleep[b]) continue;

        const dx = x[b] - x[a];
        const dy = y[b] - y[a];
        const minDist = radius[a] + radius[b];
        const sq = dx * dx + dy * dy;
        if (sq >= minDist * minDist || sq === 0) continue;

        const dist = Math.sqrt(sq);
        // 0.5 of the overlap each, relaxed slightly. A full correction in one substep makes a
        // dense pile jitter; three substeps at 0.85 converge without the buzz.
        const push = ((minDist - dist) / dist) * 0.5 * 0.85;
        const ox = dx * push;
        const oy = dy * push;

        if (!asleep[a]) { x[a] -= ox; y[a] -= oy; }
        if (!asleep[b]) { x[b] += ox; y[b] += oy; }

        // Contact wakes a sleeping body. Without this a new drop lands on a settled pile and
        // passes through it, because the pile was skipped by both integration and this loop.
        if (asleep[a] && !asleep[b]) { asleep[a] = 0; world.quiet[a] = 0; }
        if (asleep[b] && !asleep[a]) { asleep[b] = 0; world.quiet[b] = 0; }
    }
}

function integrate(world, dt) {
    const { x, y, px, py, radius, asleep, quiet } = world;
    const accelY = world.gravity * dt * dt;
    const damping = world.damping;
    const floor = world.height;
    const right = world.width;

    for (let i = 0; i < world.count; i++) {
        if (asleep[i]) continue;

        const vx = (x[i] - px[i]) * damping;
        const vy = (y[i] - py[i]) * damping;

        px[i] = x[i];
        py[i] = y[i];
        x[i] += vx;
        y[i] += vy + accelY;

        const r = radius[i];

        // Walls. Reflecting the PREVIOUS position rather than the current one is what turns a
        // clamp into a bounce: the gap between them is the velocity, so mirroring it and scaling
        // by restitution is the whole collision response.
        if (x[i] < r) {
            x[i] = r;
            px[i] = x[i] + vx * world.restitution;
        } else if (x[i] > right - r) {
            x[i] = right - r;
            px[i] = x[i] + vx * world.restitution;
        }

        if (y[i] > floor - r) {
            y[i] = floor - r;
            py[i] = y[i] + vy * world.restitution;
            // Ink is not a bouncy ball; horizontal energy bleeds off hard on contact so a drop
            // spreads a little and stops instead of skating along the floor.
            px[i] = x[i] - (x[i] - px[i]) * 0.72;
        } else if (y[i] < r) {
            y[i] = r;
            py[i] = y[i] + vy * world.restitution;
        }

        const moved = Math.abs(x[i] - px[i]) + Math.abs(y[i] - py[i]);
        if (moved < SLEEP_EPSILON) {
            if (++quiet[i] >= SLEEP_FRAMES) {
                asleep[i] = 1;
                // Zero the residual so it does not wake up with stale momentum.
                px[i] = x[i];
                py[i] = y[i];
            }
        } else {
            quiet[i] = 0;
        }
    }
}

export function step(world, dt = FIXED_DT) {
    if (world.count === 0) return 0;
    rebuildGrid(world);

    const sub = dt / SUBSTEPS;
    for (let s = 0; s < SUBSTEPS; s++) {
        integrate(world, sub);
        fillGrid(world);
        resolveCollisions(world);
    }

    let awake = 0;
    for (let i = 0; i < world.count; i++) {
        if (!world.asleep[i]) awake++;
    }
    return awake;
}

// ── Palettes ─────────────────────────────────────────────────────────────────────────────
//
// Read once from the design tokens rather than hardcoded, for the reason the Insights charts and
// the map pins already do it: a literal hex freezes light mode into a page that also renders
// dark. Falls back to the brand purple if the token is missing, never to black — a black blob on
// a dark surface is invisible, which reads as the effect being broken rather than subtle.

function readPalette() {
    const fallback = ['#7C3AED', '#f5576c', '#1c1926'];
    try {
        const style = getComputedStyle(document.documentElement);
        const pick = (name, or) => (style.getPropertyValue(name).trim() || or);
        return [
            pick('--color-brand', fallback[0]),
            pick('--color-accent', fallback[1]),
            // The comic's own ink colour, which is what this is meant to look like. Both of
            // these are real tokens and both flip with the theme — see the token block in
            // app.css; --color-text does not exist and asking for it would silently return the
            // fallback forever.
            pick('--color-comic-ink', pick('--color-text-primary', fallback[2]))
        ];
    } catch {
        return fallback;
    }
}

// ── Effects ──────────────────────────────────────────────────────────────────────────────

const instances = new Map();
let nextId = 1;

function measure(canvas) {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const width = Math.max(1, Math.floor((canvas.clientWidth || 1) * dpr));
    const height = Math.max(1, Math.floor((canvas.clientHeight || 1) * dpr));
    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
    }
    return { width, height, dpr };
}

/**
 * Seeds the world for a mode.
 *
 * `ink`      — drops enter from above the top edge across the full width and fall. This is the
 *              settle that follows the shader burst: the burst throws ink, this is where it ends
 *              up.
 * `shatter`  — bodies start at an origin with outward velocity, so the score ring appears to
 *              break apart and the pieces fall. Reserved for high scores by the caller; a ring
 *              that shatters on every comic stops meaning anything.
 */
function seed(world, mode, score, originX, originY) {
    const strange = Math.min(1, Math.max(0, score / 100));
    const count = Math.min(world.capacity, Math.round(26 + strange * 62));
    const base = Math.max(3, Math.min(world.width, world.height) * 0.028);

    for (let i = 0; i < count; i++) {
        // Size varies over nearly a factor of three. A monodisperse pile packs into a visible
        // lattice; mixed sizes settle into something that reads as liquid.
        const radius = base * (0.55 + Math.random() * 1.5);
        const tint = Math.random() < 0.62 ? 0 : (Math.random() < 0.6 ? 1 : 2);

        if (mode === 'shatter') {
            const angle = Math.random() * Math.PI * 2;
            const speed = (140 + Math.random() * 420) * (0.6 + strange * 0.7);
            addBody(world,
                originX + Math.cos(angle) * 4,
                originY + Math.sin(angle) * 4,
                Math.cos(angle) * speed,
                // Biased upward: everything falls a moment later, and a burst that only ever
                // goes down reads as a leak rather than a break.
                Math.sin(angle) * speed - 220,
                radius, tint);
        } else {
            addBody(world,
                Math.random() * world.width,
                -radius - Math.random() * world.height * 0.7,
                (Math.random() - 0.5) * 120,
                60 + Math.random() * 240,
                radius, tint);
        }
    }
}

/**
 * @param {HTMLCanvasElement} canvas overlay canvas; must be aria-hidden and pointer-events:none
 * @param {{ mode?: 'ink'|'shatter', score?: number, originX?: number, originY?: number,
 *           holdMs?: number }} options
 *        originX/originY are 0..1 fractions of the canvas, so the caller does not have to know
 *        the device pixel ratio.
 * @returns {number} handle, or 0 if it did not start
 */
export function start(canvas, options = {}) {
    if (!canvas || !gfx.allows('full')) return 0;

    const ctx = (() => {
        try {
            return canvas.getContext('2d', { alpha: true, desynchronized: true });
        } catch {
            return null;
        }
    })();
    const palette = readPalette();
    const tinted = ensureSprites(palette);
    if (!ctx || !tinted) return 0;

    const { width, height } = measure(canvas);
    const world = createWorld(MAX_BODIES);
    world.width = width;
    world.height = height;

    const mode = options.mode === 'shatter' ? 'shatter'
        : options.mode === 'pile' ? 'pile'
        : 'ink';
    const score = options.score ?? 50;

    // `pile` starts EMPTY. It is the only preset that is not a one-shot: bodies arrive from
    // emit() as the user taps reactions, so seeding it would put ink on screen for a tap nobody
    // has made yet. It also never ages out on its own — see holdMs below.
    if (mode === 'pile') {
        world.recycle = true;
    } else {
        seed(world, mode, score,
            (options.originX ?? 0.5) * width,
            (options.originY ?? 0.5) * height);
    }

    const id = nextId++;
    // Infinity for the pile: it is a persistent surface for the life of the comic page, and a
    // fade would quietly delete reactions the user can still see themselves having left. Every
    // other preset is a one-shot that must stop, because a rAF task redrawing an identical still
    // frame is exactly what the shared scheduler exists to prevent.
    const holdMs = options.holdMs ?? (mode === 'pile' ? Infinity : 2600);
    const fadeMs = 900;
    const startedAt = performance.now();

    const instance = {
        canvas,
        ctx,
        world,
        mode,
        stop: null
    };

    instance.stop = gfx.addTask(`physics#${id}`, (now, elapsed) => {
        const age = now - startedAt;

        // The canvas can be resized by a rotation mid-flight. Bodies keep their positions, which
        // is correct: they are in device pixels and the floor simply moves.
        const size = measure(canvas);
        world.width = size.width;
        world.height = size.height;

        // Clamp the step. A tab that was throttled hands back a 900ms elapsed, and integrating
        // that in one go teleports every body through the floor.
        const dt = Math.min(1 / 30, Math.max(1 / 240, (elapsed || 16.7) / 1000));
        const awake = step(world, dt);

        const fade = age > holdMs
            ? Math.max(0, 1 - (age - holdMs) / fadeMs)
            : 1;

        ctx.clearRect(0, 0, size.width, size.height);
        ctx.globalAlpha = fade;
        // Multiply so overlapping drops darken into a pool instead of flattening to one tone —
        // the single cheapest thing that makes this read as ink rather than as bubbles.
        ctx.globalCompositeOperation = 'multiply';

        // Two passes, not one interleaved loop with a branch. Emoji must composite normally —
        // multiply would darken every reaction against whatever it overlaps and turn a pile of
        // yellow faces brown — and flipping globalCompositeOperation per body is the exact kind
        // of 2D-context state change this renderer was written to avoid.
        for (let i = 0; i < world.count; i++) {
            if (world.glyph[i]) continue;
            const r = world.radius[i];
            // One drawImage, no state change: the tint is already baked into the sprite.
            ctx.drawImage(tinted[world.tint[i]] ?? tinted[0],
                world.x[i] - r, world.y[i] - r, r * 2, r * 2);
        }

        ctx.globalCompositeOperation = 'source-over';

        for (let i = 0; i < world.count; i++) {
            const glyph = world.glyph[i];
            if (!glyph) continue;
            const sprite = glyphSprite(glyph);
            if (!sprite) continue;

            const r = world.radius[i];
            // Tumble integrated from horizontal travel. Verlet carries no angular term, and
            // solving one for decoration would mean an inertia tensor per body; reading the spin
            // off the motion the solver already produces costs one multiply and stops the moment
            // the body does, which is the only property that actually matters here.
            world.spin[i] += (world.x[i] - world.px[i]) * 0.05;

            ctx.save();
            ctx.translate(world.x[i], world.y[i]);
            ctx.rotate(world.spin[i]);
            ctx.drawImage(sprite, -r, -r, r * 2, r * 2);
            ctx.restore();
        }

        ctx.globalAlpha = 1;

        // Everything asleep AND faded out means there is nothing left to compute or show.
        // Holding a rAF task alive to redraw an identical still frame is exactly the kind of
        // idle wake-up gfx-core exists to prevent.
        // A pile never self-terminates (holdMs is Infinity, so `fade` is pinned at 1 and this
        // comparison is false). It is stopped by its component's teardown instead — the reactions
        // are the user's own marks on the page and are theirs until they leave it.
        if (fade <= 0 || (awake === 0 && age > holdMs + fadeMs)) {
            stop(id);
        }
    });

    instances.set(id, instance);
    canvas.dataset.physics = mode;
    return id;
}

/**
 * Throws one reaction into a running `pile`.
 *
 * This is what a reaction bar had no way to express before: a tally next to a glyph says how
 * many people pressed it, and says nothing at all about the fact that YOU just did. A body that
 * arcs off the chip, tumbles down the comic and lands unevenly on whatever is already there is
 * the same information with a sense of having been added to.
 *
 * The launch is upward and slightly outward from the tap point, because a reaction that simply
 * drops reads as something falling off rather than as something thrown on.
 *
 * @param {number} id handle from start() with mode 'pile'
 * @param {{ glyph: string, originX?: number, originY?: number, count?: number }} options
 *        originX/originY are 0..1 fractions of the canvas, so callers do not handle DPR.
 * @returns {boolean} whether anything was launched
 */
export function emit(id, options = {}) {
    const instance = instances.get(id);
    if (!instance || instance.mode !== 'pile') return false;

    const glyph = String(options.glyph ?? '').trim();
    if (!glyph || !glyphSprite(glyph)) return false;

    const { world } = instance;
    const size = measure(instance.canvas);
    world.width = size.width;
    world.height = size.height;

    const originX = (options.originX ?? 0.5) * size.width;
    const originY = (options.originY ?? 0.5) * size.height;
    const base = Math.max(8, Math.min(size.width, size.height) * 0.045);
    const count = Math.max(1, Math.min(6, options.count ?? 3));

    // Launch speed derived from the canvas, not hardcoded. Under this solver's gravity a body
    // rises v^2 / 2g, so picking a speed picks an arc height only for one particular panel size —
    // and the comic strip is very different heights on a phone and a desktop. Solving for
    // "about half the panel" keeps the toss reading the same on both.
    const apex = Math.sqrt(2 * world.gravity * size.height * 0.45);

    for (let i = 0; i < count; i++) {
        // Spread in size as well as direction. A monodisperse handful lands in a neat arc; mixed
        // sizes settle into a heap, which is the whole reason this is a solver and not a
        // keyframed animation.
        const radius = base * (0.8 + Math.random() * 0.5);
        addBody(world,
            originX + (Math.random() - 0.5) * base * 2,
            originY,
            (Math.random() - 0.5) * 260,
            // Negative y is up in canvas coordinates. Every body falls a moment later.
            -apex * (0.8 + Math.random() * 0.35),
            radius, 0, glyph);
    }

    // Anything already asleep next to the landing zone has to be woken, or the new bodies fall
    // straight through a settled pile — the pile is skipped by integration AND collision while
    // asleep. Cheap enough to do wholesale at this body count, and only on a tap.
    for (let i = 0; i < world.count; i++) {
        world.asleep[i] = 0;
        world.quiet[i] = 0;
    }

    return true;
}

export function stop(id) {
    const instance = instances.get(id);
    if (!instance) return;
    instance.stop?.();
    try {
        instance.ctx.clearRect(0, 0, instance.canvas.width, instance.canvas.height);
    } catch { /* canvas already detached */ }
    delete instance.canvas.dataset.physics;
    instances.delete(id);
}

gfx.onTierChanged((tier) => {
    if (tier !== 'full') {
        for (const id of [...instances.keys()]) stop(id);
    }
});

// Exported for tests and for any future effect that wants the solver without the ink look.
export { createWorld, addBody };
