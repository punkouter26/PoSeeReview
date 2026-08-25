// Print-press post-process for the comic panel: halftone dots, paper grain, a touch of
// chromatic aberration at the edges, and a vignette. The goal is to make AI output read as
// deliberately printed rather than generated.
//
// THE IMPORTANT PART IS WHAT THIS DOES NOT DO.
//
// The comic PNG is the single most shareable thing the app produces. Replacing its <img> with a
// <canvas> would silently break long-press-save on mobile and right-click-save-image on desktop
// — the two ways people actually keep an image. So the original <img> stays in the DOM and stays
// the accessible, saveable element; the canvas is layered over it purely as decoration, marked
// aria-hidden and pointer-events:none. If anything here fails, the untouched <img> is already
// what the user sees.
//
// This also means the effect is never applied to a cross-origin blob URL without CORS: reading
// pixels from one taints the canvas, and while we never call readPixels, texImage2D on a tainted
// source throws a SecurityError. We ask for crossOrigin and fall back silently if it is refused.

import { gfx, createGl, compileProgram, FULLSCREEN_VERTEX_SHADER, resizeToDisplay, disposeGl } from './gfx-core.js';

const FRAGMENT_SHADER = `#version 300 es
precision mediump float;

in vec2 vUv;
out vec4 fragColor;

uniform sampler2D uImage;
uniform vec2  uResolution;
uniform float uTime;
uniform float uHalftone;    // 0 = off, 1 = full dot pattern
uniform float uGrain;
uniform float uAberration;

float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float luma(vec3 c) {
    return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

void main() {
    // Flip V: WebGL samples bottom-up, the image decodes top-down.
    vec2 uv = vec2(vUv.x, 1.0 - vUv.y);

    vec2 centered = uv - 0.5;
    float edge = dot(centered, centered);

    // Chromatic aberration scaled by distance from centre, the way a real lens behaves. Uniform
    // aberration across the frame just looks like a misregistered print.
    vec2 shift = centered * edge * uAberration * 0.06;
    vec3 color = vec3(
        texture(uImage, uv - shift).r,
        texture(uImage, uv).g,
        texture(uImage, uv + shift).b
    );

    // Halftone. The dot grid is computed in device pixels so the dots stay a constant physical
    // size regardless of how large the panel is laid out.
    if (uHalftone > 0.001) {
        float scale = 3.4;
        // 15 degrees is the classic screen angle for black plates; axis-aligned dots moire
        // badly against the panel borders the image generator tends to draw.
        float a = radians(15.0);
        mat2 rot = mat2(cos(a), -sin(a), sin(a), cos(a));
        vec2 grid = rot * (uv * uResolution / scale);

        vec2 cell = fract(grid) - 0.5;
        float dist = length(cell);

        // Dot radius tracks inverse luminance: dark areas grow their dots, exactly like ink
        // coverage on paper.
        float radius = sqrt(max(0.0, 1.0 - luma(color))) * 0.62;
        float dot = smoothstep(radius, radius - 0.16, dist);

        vec3 inked = mix(vec3(1.0), color * 0.86, dot);
        color = mix(color, inked, uHalftone * 0.55);
    }

    // Paper grain. Animated very slightly so a still image does not look like a dead texture,
    // but slowly enough that it never reads as video noise.
    float grain = hash(uv * uResolution + floor(uTime * 8.0)) - 0.5;
    color += grain * uGrain * 0.12;

    // Warm the whites a little: pure #FFF reads as screen, not paper.
    color = mix(color, color * vec3(1.02, 1.0, 0.96), 0.5);

    float vignette = smoothstep(0.95, 0.15, edge * 1.6);
    color *= 0.90 + vignette * 0.10;

    fragColor = vec4(clamp(color, 0.0, 1.0), 1.0);
}`;

const instances = new Map();
let nextId = 1;

export function attach(canvas, image, options = {}) {
    if (!canvas || !image || !gfx.allows('full')) {
        return 0;
    }

    // naturalWidth is 0 until decode finishes; uploading then produces a black panel.
    if (!image.complete || image.naturalWidth === 0) {
        return 0;
    }

    const gl = createGl(canvas);
    if (!gl) return 0;

    let program;
    try {
        program = compileProgram(gl, FULLSCREEN_VERTEX_SHADER, FRAGMENT_SHADER);
    } catch (err) {
        console.warn('[comic-fx] shader unavailable; leaving the plain image', err);
        disposeGl(gl);
        return 0;
    }

    const texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);
    // CLAMP_TO_EDGE matters: the aberration pass samples slightly outside the image, and REPEAT
    // would wrap the opposite edge into the frame as a coloured fringe.
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);

    try {
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, image);
    } catch (err) {
        // Tainted canvas: the blob SAS URL was served without CORS headers. The <img> underneath
        // is unaffected, so the user still sees their comic.
        console.warn('[comic-fx] image is not readable by WebGL (CORS); leaving the plain image', err);
        gl.deleteTexture(texture);
        gl.deleteProgram(program);
        disposeGl(gl);
        return 0;
    }

    const uniforms = {
        image: gl.getUniformLocation(program, 'uImage'),
        resolution: gl.getUniformLocation(program, 'uResolution'),
        time: gl.getUniformLocation(program, 'uTime'),
        halftone: gl.getUniformLocation(program, 'uHalftone'),
        grain: gl.getUniformLocation(program, 'uGrain'),
        aberration: gl.getUniformLocation(program, 'uAberration')
    };

    const vao = gl.createVertexArray();
    const id = nextId++;
    const startTime = performance.now();

    const instance = {
        gl, program, texture, vao, canvas,
        halftone: options.halftone ?? 1,
        grain: options.grain ?? 1,
        aberration: options.aberration ?? 1,
        stop: null
    };

    instance.stop = gfx.addTask(`comic-fx#${id}`, (now) => {
        resizeToDisplay(canvas, gl, 2);

        gl.useProgram(program);
        gl.bindVertexArray(vao);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, texture);
        gl.uniform1i(uniforms.image, 0);
        gl.uniform2f(uniforms.resolution, canvas.width, canvas.height);
        gl.uniform1f(uniforms.time, (now - startTime) / 1000);
        gl.uniform1f(uniforms.halftone, instance.halftone);
        gl.uniform1f(uniforms.grain, instance.grain);
        gl.uniform1f(uniforms.aberration, instance.aberration);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        gl.bindVertexArray(null);
    });

    instances.set(id, instance);
    canvas.dataset.comicFx = 'on';
    return id;
}

export function detach(id) {
    const instance = instances.get(id);
    if (!instance) return;

    instance.stop?.();
    instance.gl.deleteTexture(instance.texture);
    instance.gl.deleteProgram(instance.program);
    instance.gl.deleteVertexArray(instance.vao);
    disposeGl(instance.gl);
    if (instance.canvas) {
        delete instance.canvas.dataset.comicFx;
    }
    instances.delete(id);
}

gfx.onTierChanged((tier) => {
    if (tier !== 'full') {
        for (const id of [...instances.keys()]) {
            detach(id);
        }
    }
});

window.poseeComicFx = { attach, detach };
