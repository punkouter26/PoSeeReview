// The "versus" clash on the duel page: a live electric seam over the server-drawn duel card.
//
// THE MOMENT THIS IS FOR. `/duel/{a}/{b}` is one static 1200x630 card — two comics, a seam and a
// "VS". The page exists to answer "which is stranger?", and a still image announces that answer
// in the same breath as the question. This turns the first three seconds into the fight itself:
// a white-hot ignition at the VS, an arc that strikes the seam top to bottom, a beat of crackle,
// then the stranger comic's half floods from its score badge and keeps a faint heat over it.
//
// WHAT THE SCORES DO. The seam is pushed toward the LOWER score, by up to 8% of the card width:
// the stranger comic has taken ground. The push is pinned at the VS disc (it holds the line) and
// bends out below it, and stays small above it so the arc never crosses the left score badge. The
// closer the two scores, the more violent the arc — thicker, more branches, faster flicker,
// sparks. A tie is dead centre and as violent as it gets, and floods neither side.
//
// HOW IT IS DRAWN. One fullscreen triangle on a transparent canvas laid over the <img>. The arc is
// a centre line made of slow fbm wander plus two octaves of re-rolled jag (re-rolled on a
// quantised clock — real lightning does not glide, it jumps), sampled through an fbm domain warp,
// with two braided strands, short-lived downward branches that fork, and ballistic sparks. Card
// geometry (VS disc, score badges) is restated from ShareCardService in card-height units.
//
// THEME. The card is the same in both themes, but the page around it is not, and comic art is
// mostly light. Dark theme: additive glow around a white-hot core. Light theme: additive light
// is invisible on a light page, so it is composited NORMALLY — an ink core (comic ink, this is a
// comic app) in a saturated accent/brand halo.
//
// COST. The arc work only runs in a band around the seam (early-out on distance), and DPR is
// capped at 1.25. After the intro the effect is idle forever, so idle is cheap on purpose: it
// draws at ~30 fps (lightning reads better jumpy anyway) and scissors to the seam band plus the
// winner's half — the loser's half is transparent, and the browser clears the undrawn region of a
// non-preserved drawing buffer for free. 'full' tier only; at 'lite' the page shows a CSS seam.
//
// EXITS. A tier drop, a lost context, a detached canvas and stop() all end in the same teardown:
// the canvas cleared to transparent, the card underneath exactly as the server drew it. Off-screen,
// the frame task is unregistered (IntersectionObserver) and the timeline waits — the intro plays
// when someone can see it, and so do the audio cues hung on its callbacks.

import { gfx, createSurface, compileProgram, FULLSCREEN_VERTEX_SHADER } from './gfx-core.js';

const STRIKE_MS = 350;      // the arc's leader takes this long to reach the bottom
const WINNER_MS = 1100;     // the winner's half floods
const IDLE_MS = 3000;       // intro over: throttle and scissor
const IDLE_FRAME_MS = 32;   // ~30 fps idle
const MAX_STEP_MS = 64;     // a stall (GC, tab switch) must not skip the intro

const MAX_PUSH = 0.08;      // fraction of card width
const PUSH_FULL_AT = 45;    // score difference that earns the full push
const CALM_AT = 35;         // score difference at which the arc is at its calmest

// In card-height units (the card is 1200x630; x runs 0..1200/630). Mirrors ShareCardService.
const BAND = 0.36;          // the arc, its branches and sparks all live within this of the seam

const FRAGMENT_SHADER = `#version 300 es
precision highp float;

in vec2 vUv;
out vec4 fragColor;

uniform vec2  uRes;
uniform float uTime;       // timeline seconds
uniform float uPush;       // signed fraction of width; + pushes the seam right (left is winning)
uniform float uViolence;   // 0..1, 1 = a tie
uniform float uFront;      // the leader's height, card-height units; > 1 once struck
uniform float uEnergy;     // arc brightness envelope
uniform float uFlash;      // ignition flash 0..1
uniform float uFlashR;     // ignition flash radius
uniform float uRing;       // shockwave radius from the winner's badge; < 0 before it fires
uniform float uWash;       // tint behind the shockwave
uniform float uSide;       // -1 left won, +1 right won, 0 tie
uniform float uDark;       // 1 = additive (dark theme), 0 = normal blending (light)
uniform float uRate;       // jag re-roll rate, Hz
uniform float uBranches;   // live branch count
uniform float uSparks;     // live spark count
uniform vec3  uBrand;
uniform vec3  uAccent;
uniform vec3  uInk;
uniform vec3  uFlood;

const float VS_Y  = 0.3492;   // 220 / 630: the disc centre, above the caption scrim
const float VS_R  = 0.0921;   // (52 + 6) / 630: disc plus its ring
const float BADGE_Y = 0.1635; // 103 / 630
const float BAND  = ${BAND.toFixed(3)};

float asp;

float hash11(float p) { p = fract(p * 0.1031); p *= p + 33.33; p *= p + p; return fract(p); }
float hash21(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), u.x),
               mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p) {
    float s = 0.0;
    float a = 0.5;
    for (int i = 0; i < 3; i++) {
        s += a * vnoise(p);
        p = p * 2.03 + vec2(1.7, 9.2);
        a *= 0.5;
    }
    return s;
}

// Pinned at the VS disc, bending out below it; held small above it, where the left badge is.
float pushProfile(float y) {
    float d = y - VS_Y;
    return d > 0.0 ? smoothstep(0.0, 0.3, d) : 0.22 * smoothstep(0.0, 0.3, -d);
}

float seamBase(float y) { return 0.5 * asp + uPush * asp * pushProfile(y); }

float arcCentre(float y, float tq) {
    float amp = 0.018 + 0.016 * uViolence;
    float wander = fbm(vec2(y * 2.6, uTime * 0.45)) - 0.5;          // slow, continuous crawl
    float jag  = vnoise(vec2(y * 23.0, tq * 1.37)) - 0.5;           // re-rolled every flicker
    float jag2 = vnoise(vec2(y * 57.0 + 11.0, tq * 2.11)) - 0.5;
    float pin = 0.3 + 0.7 * smoothstep(0.02, 0.16, abs(y - VS_Y));
    return seamBase(y) + (wander * 2.2 * amp + jag * amp * 1.1 + jag2 * amp * 0.45) * pin;
}

float segDist(vec2 p, vec2 a, vec2 b) {
    vec2 pa = p - a, ba = b - a;
    float h = clamp(dot(pa, ba) / max(dot(ba, ba), 1e-7), 0.0, 1.0);
    return length(pa - ba * h);
}

vec4 over(vec4 dst, vec3 col, float a) {
    a = clamp(a, 0.0, 1.0);
    return vec4(col * a + dst.rgb * (1.0 - a), a + dst.a * (1.0 - a));
}

void flood(vec2 q, vec2 badge, float onSide, inout float tint, inout float ring) {
    float r = length(q - badge);
    float front = exp(-pow((r - uRing) / 0.03, 2.0)) * smoothstep(2.4, 1.0, uRing);
    float behind = smoothstep(uRing, uRing - 0.2, r);
    ring = max(ring, front * onSide);
    tint = max(tint, uWash * behind * onSide);
}

void main() {
    asp = uRes.x / max(uRes.y, 1.0);
    vec2 q = vec2(vUv.x * asp, 1.0 - vUv.y);   // card-height units, y down like the card
    float px = 1.0 / max(uRes.y, 1.0);
    float v = uViolence;

    float tq = floor(uTime * uRate);
    float flick = 0.7 + 0.3 * hash11(tq * 1.618 + 3.0);
    flick *= 1.0 + 0.7 * step(0.86 - 0.12 * v, hash11(tq * 0.731 + 9.0));   // the odd pop

    vec2 vs = vec2(0.5 * asp, VS_Y);
    float rv = length(q - vs);
    float sx = seamBase(q.y);

    // ── the winner's flood and its lingering heat ──────────────────────────────────────
    float tint = 0.0;
    float ring = 0.0;
    if (uRing > 0.0) {
        float onLeft  = smoothstep(sx + 0.02, sx - 0.04, q.x);
        float onRight = smoothstep(sx - 0.02, sx + 0.04, q.x);
        if (uSide <= 0.0) flood(q, vec2(asp * 0.4142, BADGE_Y), onLeft, tint, ring);
        if (uSide >= 0.0) flood(q, vec2(asp * 0.9142, BADGE_Y), onRight, tint, ring);
        // Heat shimmer: rising, slowly boiling brightness — there is no image to refract here.
        float boil = vnoise(vec2(q.x * 7.0, q.y * 4.0 + uTime * 0.9));
        float ripple = 0.5 + 0.5 * sin(q.y * 90.0 + uTime * 7.0 + boil * 9.0);
        tint *= 0.6 + 0.6 * boil + 0.3 * ripple * boil;
    }

    // ── ignition ───────────────────────────────────────────────────────────────────────
    vec2 n = (q - vs) / max(rv, 1e-4);
    float flash = 0.0;
    if (uFlash > 0.0) {
        flash = 1.6 * uFlash * exp(-rv * rv / max(uFlashR * uFlashR, 1e-4));
        flash += uFlash * 0.7 * exp(-pow((rv - uFlashR * 1.5) / 0.018, 2.0));
        // A starburst: ragged rays thrown out of the disc, longer than the bloom.
        float rays = pow(vnoise(n * 7.0 + 3.0), 4.0) + pow(vnoise(n * 13.0 + 8.0), 6.0);
        flash += uFlash * 2.5 * rays * exp(-rv / max(uFlashR * 1.1, 1e-3));
    }

    // ── the arc ────────────────────────────────────────────────────────────────────────
    float core = 0.0, glowI = 0.0, glowO = 0.0, spark = 0.0, corona = 0.0;
    if (abs(q.x - sx) < BAND) {
        float struck = smoothstep(uFront + 0.012, uFront - 0.02, q.y);
        float outsideDisc = smoothstep(VS_R - 0.004, VS_R + 0.008, rv);
        float warp = (fbm(q * vec2(5.0, 2.5) + vec2(0.0, uTime * 0.7)) - 0.5) * (0.03 + 0.04 * v);
        float yw = q.y + warp;

        float c = arcCentre(yw, tq);
        float slope = (arcCentre(yw + 0.004, tq) - c) / 0.004;
        float d = abs(q.x - c) / sqrt(1.0 + slope * slope);

        float cw = max(0.004 + 0.0032 * v, 1.0 * px);
        float I = uEnergy * flick * struck * outsideDisc;
        float edgeFade = smoothstep(BAND, BAND * 0.6, abs(q.x - sx));

        core  += I * smoothstep(cw, cw * 0.25, d);
        glowI += I * exp(-d / (cw * 3.2));
        glowO += I * exp(-d / (0.032 + 0.026 * v)) * edgeFade;

        // Two strands braided around the main arc: the plasma rope.
        for (int k = 0; k < 2; k++) {
            float fk = float(k);
            float off = (vnoise(vec2(yw * (13.0 + fk * 7.0) + fk * 31.0, uTime * (5.0 + fk * 3.0))) - 0.5)
                      * (0.03 + 0.03 * v);
            float ds = abs(q.x - c - off);
            float Is = I * (0.35 + 0.3 * v) * (0.55 + 0.45 * hash11(tq + fk * 17.0));
            core  += Is * smoothstep(cw * 0.65, 0.0, ds) * 0.7;
            glowI += Is * exp(-ds / (cw * 2.0));
        }

        // Downward branches, born on the arc, living a fraction of a second, biased into the
        // loser's ground. They start from this row's arc centre, so they inherit its wiggle.
        float bias = 0.5 + 0.22 * sign(uPush);
        for (int k = 0; k < 8; k++) {
            float fk = float(k);
            if (fk >= uBranches) break;
            float rate = (2.5 + 5.0 * v) * (0.7 + 0.6 * hash11(fk * 7.31));
            float ph = uTime * rate + hash11(fk * 3.17) * 13.0;
            float ep = floor(ph);
            float life = fract(ph);
            float h1 = hash21(vec2(ep, fk));
            float h2 = hash21(vec2(fk + 5.0, ep));
            float h3 = hash21(vec2(ep * 1.7, fk + 2.0));
            float h4 = hash21(vec2(fk * 3.0, ep + 7.0));
            float y0 = mix(0.03, 0.86, h1);
            float side = h2 < bias ? 1.0 : -1.0;
            float L = mix(0.08, 0.2, h3) * (0.7 + 0.5 * v);
            float sl = mix(0.45, 1.15, h4);
            float dy = yw - y0;
            float u = dy / L;
            if (u <= 0.0 || u >= 1.0) continue;

            float jag = (vnoise(vec2(yw * 38.0 + fk * 13.0, ep)) - 0.5) * 0.03 * u;
            float bx = c + side * (sl * dy + 0.8 * dy * dy) + jag;
            float db = abs(q.x - bx) / sqrt(1.0 + sl * sl);
            float fade = pow(1.0 - u, 1.4) * (1.0 - life) * (1.0 - life) * smoothstep(0.0, 0.12, u);
            float Ib = uEnergy * flick * struck * outsideDisc * fade * (0.6 + 0.35 * v);
            core  += Ib * smoothstep(cw * 0.6, 0.0, db);
            glowI += Ib * exp(-db / (cw * 2.0)) * 0.8;
            glowO += Ib * exp(-db / 0.018) * 0.25;

            if (u > 0.4) {   // one fork off the outer half
                float dy2 = dy - 0.4 * L;
                float bx2 = bx + side * 0.9 * dy2 * (h3 > 0.5 ? 1.0 : -0.6);
                float db2 = abs(q.x - bx2);
                core  += Ib * 0.6 * smoothstep(cw * 0.5, 0.0, db2);
                glowI += Ib * 0.5 * exp(-db2 / (cw * 1.8));
            }
        }

        // The leader's tip while it strikes.
        if (uFront < 1.0) {
            vec2 tip = vec2(c, uFront);
            float tg = exp(-dot(q - tip, q - tip) / 0.0006);
            glowI += tg * 1.6;
            core += tg * 0.8;
        }

        // Sparks: ballistic streaks thrown off the seam, dying as they fall.
        for (int k = 0; k < 16; k++) {
            float fk = float(k);
            if (fk >= uSparks) break;
            float P = mix(0.35, 0.7, hash11(fk * 5.7 + 1.0));
            float ph = uTime / P + hash11(fk * 1.93);
            float ep = floor(ph);
            float age = fract(ph) * P;
            float h1 = hash21(vec2(ep, fk + 40.0));
            float h2 = hash21(vec2(fk + 61.0, ep));
            float h3 = hash21(vec2(ep * 2.3, fk + 3.0));
            float y0 = mix(0.04, 0.95, h1);
            if (y0 > uFront || abs(y0 - VS_Y) < VS_R) continue;
            float side = h2 < 0.5 ? -1.0 : 1.0;
            vec2 o = vec2(seamBase(y0), y0);
            vec2 vel = vec2(side * mix(0.18, 0.42, h3), mix(-0.45, 0.05, h2));
            const vec2 g = vec2(0.0, 1.5);
            float a0 = max(age - 0.035, 0.0);
            vec2 p1 = o + vel * age + 0.5 * g * age * age;
            vec2 p0 = o + vel * a0 + 0.5 * g * a0 * a0;
            float life = 1.0 - age / P;
            spark += life * life * smoothstep(1.4 * px + 0.0012, 0.0, segDist(q, p0, p1));
        }
        spark *= uEnergy;

        // Corona crawling round the VS disc's rim once the leader has passed it.
        float cn = vnoise(n * 2.6 + vec2(tq * 0.37, uTime * 0.8));
        corona = uEnergy * flick * smoothstep(VS_Y - 0.08, VS_Y + 0.04, uFront)
               * exp(-abs(rv - VS_R) / (0.004 + 0.012 * cn)) * (0.25 + 0.75 * cn) * (0.45 + 0.55 * v);
    }

    if (uDark > 0.5) {
        // Additive: rgb above alpha is light added to the card underneath.
        vec3 hot = mix(uInk, vec3(1.0), 0.6);
        vec3 inner = mix(uBrand, hot, 0.3);
        vec3 rgb = hot * min(core, 1.6)
                 + inner * glowI * 0.6
                 + uBrand * glowO * 0.55
                 + mix(uAccent, hot, 0.3) * spark * 1.3
                 + inner * corona * 1.5
                 + mix(uAccent, hot, 0.6) * flash
                 + uFlood * (tint + ring * 0.9);
        // A little alpha under the glow dims what is behind it, so the arc still reads where it
        // crosses a white comic panel; additive light alone cannot brighten white.
        float a = clamp(min(core, 1.0) * 0.85 + glowO * 0.5 + glowI * 0.1 + flash * 0.5 + tint * 0.6 + ring * 0.3, 0.0, 1.0);
        fragColor = vec4(rgb, a);
    } else {
        // Normal "over" compositing, bottom layer first.
        vec4 o = vec4(0.0);
        o = over(o, uFlood, tint + ring * 0.75);
        o = over(o, uBrand, glowO * 0.75);
        o = over(o, uAccent, glowI * 0.85);
        o = over(o, uBrand, corona * 0.9);
        o = over(o, uAccent, spark * 1.2);
        o = over(o, uInk, core * 1.2);
        o = over(o, mix(uAccent, vec3(1.0), 0.7), flash);
        fragColor = o;
    }
}`;

const UNIFORMS = ['Res', 'Time', 'Push', 'Violence', 'Front', 'Energy', 'Flash', 'FlashR', 'Ring',
    'Wash', 'Side', 'Dark', 'Rate', 'Branches', 'Sparks', 'Brand', 'Accent', 'Ink', 'Flood'];

const instances = new Map();
let nextId = 1;

// A stopped canvas keeps its context: Blazor re-renders the same <canvas> when the duel's route
// parameters change, and a context released with loseContext() can never be had again from it.
const surfaces = new WeakMap();

const clamp01 = (value) => Math.min(1, Math.max(0, value));
const smooth = (a, b, x) => { const t = clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); };

function parseColor(hex, fallback) {
    const match = /^#?([0-9a-f]{6})$/i.exec((hex ?? '').trim());
    if (!match) return fallback;
    const int = parseInt(match[1], 16);
    return [((int >> 16) & 255) / 255, ((int >> 8) & 255) / 255, (int & 255) / 255];
}

function prefersDark() {
    try {
        const explicit = document.documentElement.dataset.theme;
        if (explicit === 'dark') return true;
        if (explicit === 'light') return false;
        return window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
    } catch {
        return false;
    }
}

function readTheme() {
    const dark = prefersDark();
    const fallback = dark
        ? { brand: [0.655, 0.545, 0.98], accent: [0.984, 0.749, 0.141], ink: [0.925, 0.918, 0.961] }
        : { brand: [0.486, 0.227, 0.929], accent: [0.961, 0.62, 0.043], ink: [0.118, 0.118, 0.18] };
    try {
        const style = getComputedStyle(document.documentElement);
        const pick = (name, or) => parseColor(style.getPropertyValue(name), or);
        return {
            dark,
            brand: pick('--color-brand', fallback.brand),
            accent: pick('--color-accent', fallback.accent),
            ink: pick('--color-comic-ink', fallback.ink)
        };
    } catch {
        return { dark, ...fallback };
    }
}

function call(fn, arg) {
    if (typeof fn !== 'function') return;
    try {
        fn(arg);
    } catch {
        // A cue failing (audio, .NET interop) must not stall the effect.
    }
}

/**
 * @param {HTMLCanvasElement} canvas transparent overlay exactly covering the duel card
 * @param {{ scoreA?: number, scoreB?: number, onIgnite?: () => void, onStrike?: () => void,
 *           onWinner?: (side: 'left'|'right'|'tie') => void }} options
 * @returns {number} handle, or 0 when nothing runs (below 'full', no WebGL2, shader refused)
 */
export function start(canvas, options = {}) {
    try {
        if (!canvas || !gfx.allows('full')) return 0;

        for (const [id, existing] of instances) {
            if (existing.canvas === canvas) stop(id);
        }

        let surface = surfaces.get(canvas);
        if (!surface || surface.dead || surface.gl.isContextLost()) {
            surface = createSurface(canvas, { maxDpr: 1.25 });
            if (!surface) return 0;
            surfaces.set(canvas, surface);
        }
        const { gl } = surface;
        if (gl.isContextLost()) return 0;

        let program;
        try {
            program = compileProgram(gl, FULLSCREEN_VERTEX_SHADER, FRAGMENT_SHADER);
        } catch (err) {
            console.warn('[duel-seam] shader unavailable; the card stays static', err);
            return 0;
        }

        const a = Number(options.scoreA) || 0;
        const b = Number(options.scoreB) || 0;
        const diff = Math.abs(a - b);
        const side = Math.round(a) === Math.round(b) ? 'tie' : a > b ? 'left' : 'right';
        const violence = 1 - smooth(0, CALM_AT, diff);

        const id = nextId++;
        const s = {
            id,
            canvas,
            surface,
            program,
            vao: gl.createVertexArray(),
            u: Object.fromEntries(UNIFORMS.map((name) => [name, gl.getUniformLocation(program, `u${name}`)])),
            side,
            sideSign: side === 'left' ? -1 : side === 'right' ? 1 : 0,
            // Toward the lower score: left winning pushes the seam right.
            push: side === 'tie' ? 0 : (side === 'left' ? 1 : -1) * MAX_PUSH * smooth(0, PUSH_FULL_AT, diff),
            violence,
            theme: readTheme(),
            clock: 0,
            lastDraw: -Infinity,
            fired: { ignite: false, strike: false, winner: false },
            onIgnite: options.onIgnite,
            onStrike: options.onStrike,
            onWinner: options.onWinner,
            task: null,
            visible: true,
            cleanups: []
        };
        instances.set(id, s);

        const onLost = (event) => {
            event.preventDefault();
            stop(id);
        };
        canvas.addEventListener('webglcontextlost', onLost);
        s.cleanups.push(() => canvas.removeEventListener('webglcontextlost', onLost));

        s.cleanups.push(gfx.onTierChanged(() => {
            if (!gfx.allows('full')) stop(id);
        }));

        const refreshTheme = () => { s.theme = readTheme(); };
        try {
            const media = window.matchMedia?.('(prefers-color-scheme: dark)');
            media?.addEventListener('change', refreshTheme);
            const observer = new MutationObserver(refreshTheme);
            observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
            s.cleanups.push(() => { media?.removeEventListener('change', refreshTheme); observer.disconnect(); });
        } catch {
            // Keeps the theme it started with.
        }

        const resumeTask = () => {
            if (!s.task && s.visible && instances.has(id)) {
                s.task = gfx.addTask(`duel-seam#${id}`, (now, elapsed) => frame(s, elapsed));
            }
        };
        if (typeof IntersectionObserver === 'function') {
            const io = new IntersectionObserver((entries) => {
                // Removal reports as "not intersecting" — and the paused task would never get
                // the frame that notices the canvas is gone.
                if (!canvas.isConnected) {
                    stop(id);
                    return;
                }
                s.visible = entries[entries.length - 1].isIntersecting;
                if (s.visible) {
                    resumeTask();
                } else if (s.task) {
                    s.task();
                    s.task = null;
                }
            });
            io.observe(canvas);
            s.cleanups.push(() => io.disconnect());
        }
        resumeTask();
        return id;
    } catch (err) {
        console.warn('[duel-seam] could not start', err);
        return 0;
    }
}

function frame(s, elapsed) {
    if (!s.canvas.isConnected) {
        stop(s.id);
        return;
    }
    // Hidden (display: none, as at 'lite') or not laid out yet: nothing to draw, and the
    // timeline waits rather than playing the intro to nobody.
    if (!s.canvas.clientWidth || !s.canvas.clientHeight) return;

    const T = (s.clock += Math.min(Math.max(elapsed, 0), MAX_STEP_MS));
    const idle = T >= IDLE_MS;

    if (!s.fired.ignite) { s.fired.ignite = true; call(s.onIgnite); }
    if (!s.fired.strike && T >= STRIKE_MS) { s.fired.strike = true; call(s.onStrike); }
    if (!s.fired.winner && T >= WINNER_MS) { s.fired.winner = true; call(s.onWinner, s.side); }

    if (idle && T - s.lastDraw < IDLE_FRAME_MS) return;   // the last frame stays on screen
    s.lastDraw = T;

    const { surface, u } = s;
    const { gl } = surface;
    if (!surface.beginFrame() || gl.isContextLost()) return;

    const v = s.violence;

    // Ignition: a flash that blooms and dies inside the leader's descent.
    const ig = clamp01(T / 380);
    const flashAmount = T < 380 ? Math.pow(1 - ig, 1.6) : 0;

    // The leader descends in jerks: a stepped, accelerating front.
    let front = 1.1;
    if (T < STRIKE_MS) {
        const p = T / STRIKE_MS;
        const eased = p * 0.6 + p * p * 0.4;
        const steps = 9;
        front = (Math.floor(eased * steps) + Math.pow((eased * steps) % 1, 4)) / steps;
    }

    // Brightness: hot leader, a return-stroke blast as it lands, a slow settle to idle, and a
    // kick when the winner floods.
    const idleEnergy = 0.75 + 0.25 * v;
    let energy = 1.25;
    if (T >= STRIKE_MS) {
        const since = T - STRIKE_MS;
        energy = idleEnergy + (1.15 - idleEnergy) * Math.exp(-since / 1100) + 1.8 * Math.exp(-since / 110);
    }
    let ring = -1;
    let wash = 0;
    if (T >= WINNER_MS) {
        const dt = (T - WINNER_MS) / 1000;
        energy += 0.9 * Math.exp(-dt / 0.16);
        ring = dt * 1.75;
        const linger = s.side === 'tie' ? 0 : (s.theme.dark ? 0.07 : 0.09);
        wash = linger + ((s.side === 'tie' ? 0.16 : 0.34) - linger) * Math.exp(-dt / 0.6);
        if (s.side === 'tie' && dt > 1.6) ring = -1;   // nothing lingers, so stop paying for it
    }

    const theme = s.theme;
    const flood = s.side === 'tie' ? theme.brand : theme.accent;

    gl.useProgram(s.program);
    gl.bindVertexArray(s.vao);
    gl.uniform2f(u.Res, surface.width, surface.height);
    gl.uniform1f(u.Time, T / 1000);
    gl.uniform1f(u.Push, s.push);
    gl.uniform1f(u.Violence, v);
    gl.uniform1f(u.Front, front);
    gl.uniform1f(u.Energy, energy);
    gl.uniform1f(u.Flash, flashAmount);
    gl.uniform1f(u.FlashR, 0.05 + 0.45 * Math.sqrt(ig));
    gl.uniform1f(u.Ring, ring);
    gl.uniform1f(u.Wash, wash);
    gl.uniform1f(u.Side, s.sideSign);
    gl.uniform1f(u.Dark, theme.dark ? 1 : 0);
    gl.uniform1f(u.Rate, (idle ? 9 : 14) + (idle ? 9 : 14) * v);
    gl.uniform1f(u.Branches, Math.round(2 + 5 * v + (idle ? 0 : 1)));
    gl.uniform1f(u.Sparks, Math.round((idle ? 2 : 6) + 10 * v));
    gl.uniform3fv(u.Brand, theme.brand);
    gl.uniform3fv(u.Accent, theme.accent);
    gl.uniform3fv(u.Ink, theme.ink);
    gl.uniform3fv(u.Flood, flood);

    if (idle) {
        // Only the seam band and the winner's half can hold anything now.
        const W = surface.width;
        const band = Math.ceil(BAND * surface.height) + 2;
        const seam = (0.5 + Math.max(0, s.push)) * W;      // the push's far reach
        const seamNear = (0.5 + Math.min(0, s.push)) * W;
        let x0 = seamNear - band;
        let x1 = seam + band;
        if (s.side === 'left') x0 = 0;
        if (s.side === 'right') x1 = W;
        x0 = Math.max(0, Math.floor(x0));
        x1 = Math.min(W, Math.ceil(x1));
        gl.enable(gl.SCISSOR_TEST);
        gl.scissor(x0, 0, x1 - x0, surface.height);
    }
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    gl.disable(gl.SCISSOR_TEST);
    gl.bindVertexArray(null);
    surface.present();
}

/** Idempotent. Leaves the canvas transparent, so the card shows exactly as the server drew it. */
export function stop(handle) {
    const s = instances.get(handle);
    if (!s) return;
    instances.delete(handle);

    try { s.task?.(); } catch { /* already gone */ }
    s.task = null;
    for (const cleanup of s.cleanups) {
        try { cleanup(); } catch { /* best effort */ }
    }

    try {
        const { gl } = s.surface;
        if (!gl.isContextLost()) {
            gl.deleteProgram(s.program);
            gl.deleteVertexArray(s.vao);
            // Shrinking drops the backing store; the clear makes sure no last frame lingers.
            s.canvas.width = s.canvas.height = 1;
            gl.bindFramebuffer(gl.FRAMEBUFFER, null);
            gl.disable(gl.SCISSOR_TEST);
            gl.clearColor(0, 0, 0, 0);
            gl.clear(gl.COLOR_BUFFER_BIT);
        }
        // Gone from the page, or lost: nothing will reuse it, so give the GPU its context back.
        if (!s.canvas.isConnected || gl.isContextLost()) {
            s.surface.release();
            surfaces.delete(s.canvas);
        }
    } catch {
        // The loop is already stopped, which is what matters.
    }
}
