// Rapier physics on the discovery grid: each restaurant card gets a body on a spring to its
// layout position, so tapping one knocks it and its neighbours settle.
//
// THIS IS THE MOST EXPENSIVE THING IN THE APP AND IT IS GATED ACCORDINGLY.
//
// rapier2d-compat is ~580KB gzipped — a second WASM runtime loading beside the Blazor one, on
// the landing page, which is exactly the worst place to spend bytes on a mobile-first product
// whose PRD asks for near-zero friction to the first comic. So:
//
//   * dynamic import(), never a static one;
//   * 'full' tier only;
//   * deferred to requestIdleCallback AFTER first paint, so it never competes with the restaurant
//     fetch or the first render;
//   * skipped entirely on Save-Data or a slow effective connection;
//   * cards keep their normal CSS layout — physics only ever writes a transform offset, so if
//     any of this fails or is torn down, the grid is simply a normal grid.
//
// Nothing here is on the critical path to a comic.

import { gfx } from './gfx-core.js';

const state = {
    rapier: null,
    loading: null,
    world: null
};

const PIXELS_PER_METRE = 90;

function connectionIsCheap() {
    try {
        const connection = navigator.connection;
        if (!connection) return true;
        if (connection.saveData) return false;
        if (typeof connection.effectiveType === 'string' && /2g/.test(connection.effectiveType)) {
            return false;
        }
    } catch {
        // No Network Information API — assume it is fine.
    }
    return true;
}

async function loadRapier() {
    if (state.rapier) return state.rapier;
    if (state.loading) return state.loading;

    state.loading = import('../lib/rapier/rapier2d-compat.js')
        .then(async (mod) => {
            // The compat build ships the wasm base64-inlined; init() decodes and instantiates it.
            await mod.init();
            state.rapier = mod;
            state.loading = null;
            return mod;
        })
        .catch((err) => {
            state.loading = null;
            throw err;
        });

    return state.loading;
}

/** Runs a callback when the browser is genuinely idle, with a timeout so it always fires. */
function whenIdle(callback, timeout = 3000) {
    if (typeof requestIdleCallback === 'function') {
        requestIdleCallback(callback, { timeout });
    } else {
        setTimeout(callback, 800);
    }
}

export async function start(container, options = {}) {
    if (!container || !gfx.allows('full') || !connectionIsCheap()) {
        return false;
    }

    return new Promise((resolve) => {
        whenIdle(async () => {
            // Conditions can change between scheduling and running — a downgrade may have fired.
            if (!gfx.allows('full') || !container.isConnected) {
                resolve(false);
                return;
            }

            let RAPIER;
            try {
                RAPIER = await loadRapier();
            } catch (err) {
                console.warn('[grid-physics] rapier failed to load; grid stays static', err);
                resolve(false);
                return;
            }

            if (!gfx.allows('full') || !container.isConnected) {
                resolve(false);
                return;
            }

            resolve(attach(RAPIER, container, options));
        });
    });
}

function attach(RAPIER, container, options) {
    stop();

    const elements = [...container.querySelectorAll('[data-physics-card]')];
    if (elements.length === 0) {
        return false;
    }

    // Gravity is zero: these are springs on a board, not objects falling off a shelf. Real
    // gravity would pile every card at the bottom of the grid, which destroys the layout the
    // user is trying to read.
    const world = new RAPIER.World({ x: 0, y: 0 });

    const bodies = elements.map((element) => {
        const rect = element.getBoundingClientRect();
        const containerRect = container.getBoundingClientRect();

        const homeX = (rect.left - containerRect.left + rect.width / 2) / PIXELS_PER_METRE;
        const homeY = (rect.top - containerRect.top + rect.height / 2) / PIXELS_PER_METRE;

        const bodyDesc = RAPIER.RigidBodyDesc.dynamic()
            .setTranslation(homeX, homeY)
            .setLinearDamping(3.4)
            .setAngularDamping(5.0);
        const body = world.createRigidBody(bodyDesc);

        const colliderDesc = RAPIER.ColliderDesc
            .cuboid(rect.width / 2 / PIXELS_PER_METRE, rect.height / 2 / PIXELS_PER_METRE)
            .setRestitution(0.25)
            .setDensity(1.1);
        world.createCollider(colliderDesc, body);

        return { element, body, homeX, homeY };
    });

    const onPointerDown = (event) => {
        const card = event.target.closest('[data-physics-card]');
        if (!card) return;

        const hit = bodies.find((b) => b.element === card);
        if (!hit) return;

        // Impulse away from the click point, so the card recoils from where it was poked.
        const rect = card.getBoundingClientRect();
        const dx = (event.clientX - (rect.left + rect.width / 2)) / rect.width;
        const dy = (event.clientY - (rect.top + rect.height / 2)) / rect.height;

        hit.body.applyImpulse({ x: -dx * 2.2, y: -dy * 2.2 }, true);
        hit.body.applyTorqueImpulse(-dx * 0.35, true);
    };

    container.addEventListener('pointerdown', onPointerDown, { passive: true });

    const stopTask = gfx.addTask('grid-physics', () => {
        // Spring each body back to its layout position. The layout is the source of truth; the
        // physics only ever perturbs around it.
        for (const item of bodies) {
            const position = item.body.translation();
            const toHomeX = item.homeX - position.x;
            const toHomeY = item.homeY - position.y;

            item.body.applyImpulse({ x: toHomeX * 0.55, y: toHomeY * 0.55 }, true);

            const rotation = item.body.rotation();
            item.body.applyTorqueImpulse(-rotation * 0.06, true);
        }

        world.step();

        for (const item of bodies) {
            const position = item.body.translation();
            const offsetX = (position.x - item.homeX) * PIXELS_PER_METRE;
            const offsetY = (position.y - item.homeY) * PIXELS_PER_METRE;
            const rotation = item.body.rotation();

            // Below a pixel there is nothing to see, and writing a transform on every card every
            // frame forces layer work the compositor would otherwise skip.
            if (Math.abs(offsetX) < 0.35 && Math.abs(offsetY) < 0.35 && Math.abs(rotation) < 0.002) {
                if (item.element.style.transform) {
                    item.element.style.transform = '';
                }
                continue;
            }

            item.element.style.transform =
                `translate3d(${offsetX.toFixed(2)}px, ${offsetY.toFixed(2)}px, 0) rotate(${rotation.toFixed(4)}rad)`;
        }
    });

    state.world = {
        dispose() {
            stopTask();
            container.removeEventListener('pointerdown', onPointerDown);
            for (const item of bodies) {
                item.element.style.transform = '';
            }
            // Rapier allocates inside the wasm heap; without free() the memory is held until the
            // module itself is collected, which for a cached module is never.
            world.free();
        }
    };

    return true;
}

export function stop() {
    state.world?.dispose();
    state.world = null;
}

gfx.onTierChanged((tier) => {
    if (tier !== 'full') {
        stop();
    }
});

window.poseeGridPhysics = { start, stop };
