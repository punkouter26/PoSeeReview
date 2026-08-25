// Hall of Fame as a 3D shelf: leaderboard entries as cards with depth, momentum scrolling and a
// reflective surface.
//
// Three.js is ~171KB gzipped, so it is loaded with a dynamic import() the moment this module's
// start() is called and NEVER at page load. A visitor who lands on discovery, generates a comic
// and shares it pays nothing for this file.
//
// ACCESSIBILITY: the DOM list is not replaced. It stays in the document as the real, focusable,
// screen-reader-navigable leaderboard; the canvas is layered over it and marked aria-hidden. A
// 3D list that only exists in a canvas is invisible to assistive technology and unreachable by
// keyboard, which is not a trade this leaderboard is worth making.

import { gfx } from './gfx-core.js';

const state = {
    three: null,
    loading: null,
    scene: null
};

/** Loads Three.js once and caches the module. */
async function loadThree() {
    if (state.three) return state.three;
    if (state.loading) return state.loading;

    state.loading = import('../lib/three/three.module.min.js')
        .then((mod) => {
            state.three = mod;
            state.loading = null;
            return mod;
        })
        .catch((err) => {
            state.loading = null;
            throw err;
        });

    return state.loading;
}

function makeCardTexture(THREE, entry, index) {
    // Cards are drawn to a 2D canvas and uploaded as a texture rather than composed from meshes:
    // one texture per card is far cheaper than text geometry, and the text stays crisp.
    const width = 512;
    const height = 320;
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');

    const score = Math.min(100, Math.max(0, entry.strangenessScore ?? 0));
    const heat = score / 100;

    const gradient = ctx.createLinearGradient(0, 0, width, height);
    gradient.addColorStop(0, `hsl(${268 - heat * 40}, 62%, ${18 + heat * 8}%)`);
    gradient.addColorStop(1, `hsl(${300 + heat * 40}, 58%, ${12 + heat * 6}%)`);
    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, width, height);

    ctx.fillStyle = 'rgba(255,255,255,0.10)';
    ctx.fillRect(0, 0, width, 6);

    ctx.fillStyle = '#ECEAF5';
    ctx.font = '600 30px "DM Sans", system-ui, sans-serif';
    ctx.textBaseline = 'top';

    const name = String(entry.restaurantName ?? 'Unknown');
    const maxWidth = width - 64;
    let line = name;
    while (ctx.measureText(line).width > maxWidth && line.length > 4) {
        line = line.slice(0, -2);
    }
    if (line !== name) line += '…';
    ctx.fillText(line, 32, 40);

    ctx.font = '500 18px "DM Sans", system-ui, sans-serif';
    ctx.fillStyle = 'rgba(236,234,245,0.62)';
    ctx.fillText(`#${index + 1}`, 32, 88);

    ctx.font = '700 108px "Bangers", system-ui, sans-serif';
    ctx.fillStyle = `hsl(${280 - heat * 60}, 90%, ${62 + heat * 12}%)`;
    ctx.fillText(String(score), 32, 150);

    ctx.font = '500 18px "DM Sans", system-ui, sans-serif';
    ctx.fillStyle = 'rgba(236,234,245,0.55)';
    ctx.fillText('STRANGENESS', 32, 272);

    const texture = new THREE.CanvasTexture(canvas);
    texture.colorSpace = THREE.SRGBColorSpace;
    texture.anisotropy = 4;
    return texture;
}

export async function start(container, entries, options = {}) {
    if (!container || !gfx.allows('full') || !Array.isArray(entries) || entries.length === 0) {
        return false;
    }

    let THREE;
    try {
        THREE = await loadThree();
    } catch (err) {
        // The DOM list underneath is the real leaderboard; failing to decorate it is harmless.
        console.warn('[hall-shelf] three.js failed to load; keeping the DOM list', err);
        return false;
    }

    // A tier downgrade may have landed while the ~171KB module was in flight.
    if (!gfx.allows('full')) {
        return false;
    }

    stop();

    const renderer = new THREE.WebGLRenderer({ alpha: true, antialias: true, powerPreference: 'low-power' });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.setSize(container.clientWidth, container.clientHeight, false);
    renderer.domElement.setAttribute('aria-hidden', 'true');
    renderer.domElement.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;pointer-events:none;';
    container.appendChild(renderer.domElement);

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(42, container.clientWidth / Math.max(1, container.clientHeight), 0.1, 100);
    camera.position.set(0, 0, 7.2);

    scene.add(new THREE.AmbientLight(0xffffff, 1.25));
    const key = new THREE.DirectionalLight(0xffffff, 1.6);
    key.position.set(2.5, 4, 6);
    scene.add(key);

    const cards = [];
    const cardGeometry = new THREE.PlaneGeometry(3.0, 1.875);
    const visible = entries.slice(0, 12);

    visible.forEach((entry, index) => {
        const texture = makeCardTexture(THREE, entry, index);
        const material = new THREE.MeshStandardMaterial({
            map: texture,
            roughness: 0.44,
            metalness: 0.16,
            transparent: true
        });
        const mesh = new THREE.Mesh(cardGeometry, material);
        mesh.position.set(0, -index * 2.35, 0);
        scene.add(mesh);
        cards.push({ mesh, material, texture });
    });

    const scroll = { value: 0, target: 0, velocity: 0 };
    const maxScroll = Math.max(0, (visible.length - 1) * 2.35);

    const onWheel = (event) => {
        scroll.target = Math.min(maxScroll, Math.max(0, scroll.target + event.deltaY * 0.006));
    };
    // passive: the canvas never calls preventDefault, and a non-passive wheel listener blocks
    // the compositor from scrolling until JS has run.
    container.addEventListener('wheel', onWheel, { passive: true });

    let pointerStart = null;
    const onPointerDown = (e) => { pointerStart = { y: e.clientY, scroll: scroll.target }; };
    const onPointerMove = (e) => {
        if (!pointerStart) return;
        scroll.target = Math.min(maxScroll, Math.max(0, pointerStart.scroll + (pointerStart.y - e.clientY) * 0.012));
    };
    const onPointerUp = () => { pointerStart = null; };
    container.addEventListener('pointerdown', onPointerDown);
    container.addEventListener('pointermove', onPointerMove);
    window.addEventListener('pointerup', onPointerUp);

    const stopTask = gfx.addTask('hall-shelf', (now) => {
        const width = container.clientWidth;
        const height = container.clientHeight;
        if (width > 0 && height > 0) {
            const size = renderer.getSize(new THREE.Vector2());
            if (Math.abs(size.x - width) > 1 || Math.abs(size.y - height) > 1) {
                renderer.setSize(width, height, false);
                camera.aspect = width / height;
                camera.updateProjectionMatrix();
            }
        }

        scroll.value += (scroll.target - scroll.value) * 0.09;

        const t = now / 1000;
        cards.forEach((card, index) => {
            const offset = -index * 2.35 + scroll.value;
            card.mesh.position.y = offset;
            // Push cards away from the camera as they leave the centre band, and tilt them —
            // this is what produces the shelf read rather than a flat stack.
            const distance = Math.abs(offset);
            card.mesh.position.z = -distance * 0.55;
            card.mesh.rotation.x = offset * -0.10;
            card.mesh.rotation.y = Math.sin(t * 0.5 + index) * 0.045;
            card.material.opacity = Math.max(0, 1 - distance * 0.28);
            card.mesh.visible = card.material.opacity > 0.02;
        });

        renderer.render(scene, camera);
    });

    state.scene = {
        dispose() {
            stopTask();
            container.removeEventListener('wheel', onWheel);
            container.removeEventListener('pointerdown', onPointerDown);
            container.removeEventListener('pointermove', onPointerMove);
            window.removeEventListener('pointerup', onPointerUp);

            // Three.js does not free GPU resources on garbage collection — every geometry,
            // material and texture has to be disposed explicitly or the VRAM leaks for the
            // lifetime of the tab.
            cards.forEach(({ material, texture }) => {
                material.dispose();
                texture.dispose();
            });
            cardGeometry.dispose();
            renderer.dispose();
            renderer.domElement.remove();
        }
    };

    return true;
}

export function stop() {
    state.scene?.dispose();
    state.scene = null;
}

gfx.onTierChanged((tier) => {
    if (tier !== 'full') {
        stop();
    }
});

window.poseeHallShelf = { start, stop };
