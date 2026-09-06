// A lit, shadow-casting 3D shelf behind the Hall of Fame list.
//
// READ THIS BEFORE CHANGING ANYTHING HERE.
//
// A Three.js version of this existed and was deliberately deleted: ~2.4 MB of vendored library
// for decoration layered over a DOM list that already worked. Re-adding it was an explicit
// product decision, and it comes back under conditions that address why it left:
//
//   1. NO LIBRARY. Hand-rolled WebGL2 and hand-rolled 4x4 matrix maths, in one file. The mesh is
//      generated procedurally at load, so there is no model to download either.
//   2. LAZY. Nothing here is imported statically by fx.js. The module is fetched on first use,
//      on one route, so it never touches the first-load path that SCRIPTS/fx-perf-check.mjs
//      guards.
//   3. 'full' TIER ONLY, and it tears itself down on any downgrade — like every other effect.
//   4. THE DOM LIST STAYS. This canvas renders BEHIND the real <div class="leaderboard-list">,
//      aria-hidden and pointer-events:none. That is not a nicety: the list is the only
//      keyboard-reachable, screen-reader-legible, and copy-pasteable form of the leaderboard.
//      A 3D scene that replaced it would make the page's actual content unreachable.
//
// RENDERING. Two passes and a composite:
//
//   1. shadow — the scene from the light's point of view into a depth texture.
//   2. scene  — the camera view, sampling that depth with 3x3 PCF for a soft contact shadow.
//   3. blit   — the scene colour buffer onto the pooled surface.
//
// Real shadow mapping rather than a projected blob, because the whole point of the shelf is that
// the cards sit ON something: the contact shadow is what sells the third dimension, and a blob
// under a rotating card is exactly where a fake one falls apart.

import { gfx, createSurface, compileProgram, FULLSCREEN_VERTEX_SHADER } from './gfx-core.js';

const SHADOW_SIZE = 1024;

// ── Matrix maths ─────────────────────────────────────────────────────────────────────────
// Column-major, matching GLSL. Enough for one camera and one light; a general-purpose maths
// library here would be more code than the renderer.

function identity() {
    return new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
}

function multiply(a, b) {
    const out = new Float32Array(16);
    for (let column = 0; column < 4; column++) {
        for (let row = 0; row < 4; row++) {
            let sum = 0;
            for (let k = 0; k < 4; k++) {
                sum += a[k * 4 + row] * b[column * 4 + k];
            }
            out[column * 4 + row] = sum;
        }
    }
    return out;
}

function perspective(fovY, aspect, near, far) {
    const f = 1 / Math.tan(fovY / 2);
    const out = new Float32Array(16);
    out[0] = f / aspect;
    out[5] = f;
    out[10] = (far + near) / (near - far);
    out[11] = -1;
    out[14] = (2 * far * near) / (near - far);
    return out;
}

function orthographic(size, near, far) {
    const out = identity();
    out[0] = 1 / size;
    out[5] = 1 / size;
    out[10] = -2 / (far - near);
    out[14] = -(far + near) / (far - near);
    return out;
}

function normalise(v) {
    const length = Math.hypot(v[0], v[1], v[2]) || 1;
    return [v[0] / length, v[1] / length, v[2] / length];
}

function cross(a, b) {
    return [
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0]
    ];
}

function lookAt(eye, target, up) {
    const z = normalise([eye[0] - target[0], eye[1] - target[1], eye[2] - target[2]]);
    const x = normalise(cross(up, z));
    const y = cross(z, x);

    const out = new Float32Array(16);
    out[0] = x[0]; out[4] = x[1]; out[8] = x[2];
    out[1] = y[0]; out[5] = y[1]; out[9] = y[2];
    out[2] = z[0]; out[6] = z[1]; out[10] = z[2];
    out[12] = -(x[0] * eye[0] + x[1] * eye[1] + x[2] * eye[2]);
    out[13] = -(y[0] * eye[0] + y[1] * eye[1] + y[2] * eye[2]);
    out[14] = -(z[0] * eye[0] + z[1] * eye[1] + z[2] * eye[2]);
    out[15] = 1;
    return out;
}

/**
 * Model matrix for a card: rotate about Y, then translate. Deliberately no scale — the meshes
 * are built at their final size. That keeps the upper 3x3 orthonormal, which is what lets the
 * vertex shader transform normals with `mat3(aModel)` instead of an inverse transpose.
 */
function cardMatrix(x, y, z, rotationY) {
    const c = Math.cos(rotationY);
    const s = Math.sin(rotationY);
    const out = identity();
    out[0] = c;  out[2] = -s;
    out[8] = s;  out[10] = c;
    out[12] = x; out[13] = y; out[14] = z;
    return out;
}

/**
 * Fan order. Slot 0 is the centre — closest to the camera and largest in perspective — and the
 * ranks alternate outward from it, so #1 is the card the eye lands on.
 *
 * The obvious layout (rank order left to right along the arc) puts #1 at one end, which is the
 * furthest and smallest position in the frame. That is precisely backwards for a leaderboard,
 * and it is not a subtle effect: at the edge of a 0.62 radian fan, #1 renders about two thirds
 * the size of whatever mediocre entry happened to land in the middle.
 */
function fanSlot(index) {
    const step = Math.ceil(index / 2);
    return index % 2 === 1 ? step : -step;
}

// ── Mesh generation ──────────────────────────────────────────────────────────────────────

/**
 * A rounded box, built by pushing the vertices of a subdivided cube onto the surface of a
 * rounded-box distance field. Generated rather than downloaded — the whole mesh is about 40
 * lines of arithmetic against a model file that would be a network request and a parser.
 *
 * Subdivision drives the silhouette quality: at 8 the corners are visibly faceted, at 12 they
 * read as rounded at the size these cards are drawn.
 */
function buildRoundedBox(halfExtents, radius, subdivisions = 12) {
    const positions = [];
    const normals = [];
    const indices = [];

    // Six faces of a unit cube, each as a subdivided grid, then projected outward.
    const faces = [
        { axis: [1, 0, 0], u: [0, 1, 0], v: [0, 0, 1] },
        { axis: [-1, 0, 0], u: [0, 0, 1], v: [0, 1, 0] },
        { axis: [0, 1, 0], u: [0, 0, 1], v: [1, 0, 0] },
        { axis: [0, -1, 0], u: [1, 0, 0], v: [0, 0, 1] },
        { axis: [0, 0, 1], u: [1, 0, 0], v: [0, 1, 0] },
        { axis: [0, 0, -1], u: [0, 1, 0], v: [1, 0, 0] }
    ];

    for (const face of faces) {
        const base = positions.length / 3;

        for (let i = 0; i <= subdivisions; i++) {
            for (let j = 0; j <= subdivisions; j++) {
                const s = (i / subdivisions) * 2 - 1;
                const t = (j / subdivisions) * 2 - 1;

                // Point on the cube face, in -1..1 box space.
                const cube = [
                    face.axis[0] + face.u[0] * s + face.v[0] * t,
                    face.axis[1] + face.u[1] * s + face.v[1] * t,
                    face.axis[2] + face.u[2] * s + face.v[2] * t
                ];

                // Rounded-box projection: clamp to the inner box, then step out by the radius
                // along the direction from that clamped point. This is exactly the rounded-box
                // SDF read backwards, and it gives correct normals for free.
                const inner = [
                    Math.max(-1, Math.min(1, cube[0])) * (halfExtents[0] - radius),
                    Math.max(-1, Math.min(1, cube[1])) * (halfExtents[1] - radius),
                    Math.max(-1, Math.min(1, cube[2])) * (halfExtents[2] - radius)
                ];
                const direction = normalise([
                    cube[0] * halfExtents[0] - inner[0],
                    cube[1] * halfExtents[1] - inner[1],
                    cube[2] * halfExtents[2] - inner[2]
                ]);

                positions.push(
                    inner[0] + direction[0] * radius,
                    inner[1] + direction[1] * radius,
                    inner[2] + direction[2] * radius);
                normals.push(direction[0], direction[1], direction[2]);
            }
        }

        const stride = subdivisions + 1;
        for (let i = 0; i < subdivisions; i++) {
            for (let j = 0; j < subdivisions; j++) {
                const a = base + i * stride + j;
                indices.push(a, a + 1, a + stride, a + 1, a + stride + 1, a + stride);
            }
        }
    }

    return {
        positions: new Float32Array(positions),
        normals: new Float32Array(normals),
        indices: new Uint16Array(indices)
    };
}

// ── Shaders ──────────────────────────────────────────────────────────────────────────────

const DEPTH_VERTEX = `#version 300 es
layout(location = 0) in vec3 aPosition;
layout(location = 2) in mat4 aModel;
uniform mat4 uLightViewProjection;
void main() {
    gl_Position = uLightViewProjection * aModel * vec4(aPosition, 1.0);
}`;

const DEPTH_FRAGMENT = `#version 300 es
precision mediump float;
void main() { }`;

const SCENE_VERTEX = `#version 300 es
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in mat4 aModel;
layout(location = 6) in vec4 aTint;      // rgb = albedo, a = emissive rim strength

uniform mat4 uViewProjection;
uniform mat4 uLightViewProjection;

out vec3 vNormal;
out vec3 vWorld;
out vec4 vTint;
out vec4 vLightSpace;

void main() {
    vec4 world = aModel * vec4(aPosition, 1.0);

    // The model matrix here is only ever rotation and translation — no scale, no shear — so the
    // upper 3x3 is orthonormal and its inverse transpose is itself. Computing a normal matrix
    // would be correct and pointless.
    vNormal = mat3(aModel) * aNormal;
    vWorld = world.xyz;
    vTint = aTint;
    vLightSpace = uLightViewProjection * world;

    gl_Position = uViewProjection * world;
}`;

const SCENE_FRAGMENT = `#version 300 es
precision highp float;
precision highp sampler2DShadow;

in vec3 vNormal;
in vec3 vWorld;
in vec4 vTint;
in vec4 vLightSpace;
out vec4 fragColor;

uniform sampler2DShadow uShadowMap;
uniform vec3 uLightDir;
uniform vec3 uLightColor;
uniform vec3 uCameraPos;
uniform vec2 uShadowTexel;

/**
 * 3x3 percentage-closer filter. Comparison happens in hardware via sampler2DShadow, so each tap
 * is one fetch that returns an already-filtered 0..1 rather than a depth to compare by hand.
 * Nine taps is the smallest kernel that turns a stair-stepped shadow edge into a gradient.
 */
float shadowFactor() {
    vec3 projected = vLightSpace.xyz / vLightSpace.w;
    projected = projected * 0.5 + 0.5;

    // Outside the light's frustum is lit, not shadowed. The opposite convention puts a hard
    // black band around everything the shadow map does not happen to cover.
    if (projected.z > 1.0 || any(lessThan(projected.xy, vec2(0.0))) || any(greaterThan(projected.xy, vec2(1.0)))) {
        return 1.0;
    }

    // Slope-scaled bias. A constant bias either acne-stripes surfaces facing the light or
    // detaches the shadow from the object at grazing angles; scaling by the angle fixes both.
    float cosTheta = clamp(dot(normalize(vNormal), uLightDir), 0.0, 1.0);
    float bias = mix(0.0035, 0.0006, cosTheta);

    float total = 0.0;
    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            vec2 offset = vec2(float(x), float(y)) * uShadowTexel;
            total += texture(uShadowMap, vec3(projected.xy + offset, projected.z - bias));
        }
    }
    return total / 9.0;
}

void main() {
    vec3 normal = normalize(vNormal);
    vec3 viewDir = normalize(uCameraPos - vWorld);

    float shadow = shadowFactor();
    float diffuse = max(0.0, dot(normal, uLightDir)) * shadow;

    vec3 halfway = normalize(uLightDir + viewDir);
    float specular = pow(max(0.0, dot(normal, halfway)), 42.0) * shadow * 0.35;

    // Fresnel rim. Cards at the edge of the shelf turn away from the camera and would otherwise
    // sink into the backdrop; the rim is what keeps their silhouette readable.
    float rim = pow(1.0 - max(0.0, dot(normal, viewDir)), 3.0) * vTint.a;

    // Hemisphere ambient — sky above, warm bounce from the shelf below. Flat ambient makes
    // everything look like it is lit from inside.
    float upness = normal.y * 0.5 + 0.5;
    vec3 ambient = mix(vec3(0.16, 0.13, 0.22), vec3(0.30, 0.28, 0.40), upness);

    vec3 color = vTint.rgb * (ambient + uLightColor * diffuse * 0.95);
    color += uLightColor * specular;
    color += vTint.rgb * rim * 0.85;

    fragColor = vec4(color, 1.0);
}`;

const BLIT_FRAGMENT = `#version 300 es
precision mediump float;
in vec2 vUv;
out vec4 fragColor;
uniform sampler2D uScene;
void main() {
    fragColor = texture(uScene, vUv);
}`;

// ── Instance ─────────────────────────────────────────────────────────────────────────────

const instances = new Map();
let nextId = 1;

/** Rank-driven card colour. Gold, silver, bronze, then the brand violet for everyone else. */
function tintForRank(rank) {
    if (rank === 1) return [1.00, 0.78, 0.28, 1.00];
    if (rank === 2) return [0.82, 0.84, 0.90, 0.75];
    if (rank === 3) return [0.85, 0.55, 0.32, 0.70];
    return [0.42, 0.27, 0.72, 0.45];
}

function makeSceneTargets(gl, width, height) {
    const color = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, color);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);

    // A renderbuffer, not a texture: nothing samples scene depth, and a renderbuffer is the
    // cheaper allocation for a write-only attachment.
    const depth = gl.createRenderbuffer();
    gl.bindRenderbuffer(gl.RENDERBUFFER, depth);
    gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT16, width, height);

    const fbo = gl.createFramebuffer();
    gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, color, 0);
    gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, depth);
    const ok = gl.checkFramebufferStatus(gl.FRAMEBUFFER) === gl.FRAMEBUFFER_COMPLETE;
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);

    return ok ? { color, depth, fbo, width, height } : null;
}

function makeShadowTarget(gl) {
    const texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.DEPTH_COMPONENT24, SHADOW_SIZE, SHADOW_SIZE, 0,
        gl.DEPTH_COMPONENT, gl.UNSIGNED_INT, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    // Comparison mode is what makes this a sampler2DShadow and gives hardware PCF on every tap.
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_COMPARE_MODE, gl.COMPARE_REF_TO_TEXTURE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_COMPARE_FUNC, gl.LEQUAL);

    const fbo = gl.createFramebuffer();
    gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.TEXTURE_2D, texture, 0);
    // A depth-only framebuffer must be told it has no colour output, or it is incomplete.
    gl.drawBuffers([gl.NONE]);
    gl.readBuffer(gl.NONE);
    const ok = gl.checkFramebufferStatus(gl.FRAMEBUFFER) === gl.FRAMEBUFFER_COMPLETE;
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);

    return ok ? { texture, fbo } : null;
}

/**
 * Starts the shelf on `canvas` for `entries` — an array of { rank, score }. Returns 0 if the
 * device, the tier, or the driver says no; the caller treats that as "the list is enough".
 */
export function start(canvas, entries = [], options = {}) {
    if (!canvas || !gfx.allows('full') || entries.length === 0) {
        return 0;
    }

    const surface = createSurface(canvas, { maxDpr: 1.5 });
    if (!surface) return 0;

    const { gl } = surface;

    let programs;
    try {
        programs = {
            depth: compileProgram(gl, DEPTH_VERTEX, DEPTH_FRAGMENT),
            scene: compileProgram(gl, SCENE_VERTEX, SCENE_FRAGMENT),
            blit: compileProgram(gl, FULLSCREEN_VERTEX_SHADER, BLIT_FRAGMENT)
        };
    } catch (err) {
        console.warn('[shelf] shaders unavailable; the list stands on its own', err);
        surface.release();
        return 0;
    }

    const shadow = makeShadowTarget(gl);
    if (!shadow) {
        console.warn('[shelf] depth texture unavailable; the list stands on its own');
        for (const program of Object.values(programs)) gl.deleteProgram(program);
        surface.release();
        return 0;
    }

    // One card mesh, drawn once per entry as an instance.
    const mesh = buildRoundedBox([0.62, 0.86, 0.055], 0.045, 12);
    const count = Math.min(entries.length, 24);

    const models = new Float32Array(count * 16);
    const tints = new Float32Array(count * 4);
    const layout = [];

    // Widest slot either side of centre, so the fan spans the same arc regardless of how many
    // entries the board actually has.
    const extent = Math.max(1, Math.ceil((count - 1) / 2));

    for (let i = 0; i < count; i++) {
        const entry = entries[i] ?? {};
        // A gentle arc rather than a straight row: a flat line of cards viewed in perspective
        // has almost no depth cue, and the arc is what makes the shelf read as a shelf. The
        // centre is nearest the camera, and fanSlot puts #1 there.
        const t = fanSlot(i) / extent;
        layout.push({
            x: t * 2.35,
            baseY: 0,
            z: -Math.abs(t) * 1.15,
            rotation: -t * 0.42,
            // Rank drives how far a card lifts on the idle bob — the winner floats highest.
            lift: 1 - Math.min(1, (entry.rank ?? i + 1) / 10) * 0.7
        });
        tints.set(tintForRank(entry.rank ?? i + 1), i * 4);
    }

    /**
     * Uploads a mesh plus its per-instance model and tint buffers into one VAO.
     * The mat4 instanced attribute occupies four consecutive vec4 locations (2,3,4,5) — the one
     * piece of attribute setup that is not obvious from reading the shader.
     */
    function uploadMesh(geometry, instanceModels, instanceTints, modelUsage) {
        const vao = gl.createVertexArray();
        gl.bindVertexArray(vao);

        const positionBuffer = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, positionBuffer);
        gl.bufferData(gl.ARRAY_BUFFER, geometry.positions, gl.STATIC_DRAW);
        gl.enableVertexAttribArray(0);
        gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 0, 0);

        const normalBuffer = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, normalBuffer);
        gl.bufferData(gl.ARRAY_BUFFER, geometry.normals, gl.STATIC_DRAW);
        gl.enableVertexAttribArray(1);
        gl.vertexAttribPointer(1, 3, gl.FLOAT, false, 0, 0);

        const modelBuffer = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, modelBuffer);
        gl.bufferData(gl.ARRAY_BUFFER, instanceModels, modelUsage);
        for (let column = 0; column < 4; column++) {
            const location = 2 + column;
            gl.enableVertexAttribArray(location);
            gl.vertexAttribPointer(location, 4, gl.FLOAT, false, 64, column * 16);
            gl.vertexAttribDivisor(location, 1);
        }

        const tintBuffer = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, tintBuffer);
        gl.bufferData(gl.ARRAY_BUFFER, instanceTints, gl.STATIC_DRAW);
        gl.enableVertexAttribArray(6);
        gl.vertexAttribPointer(6, 4, gl.FLOAT, false, 0, 0);
        gl.vertexAttribDivisor(6, 1);

        const indexBuffer = gl.createBuffer();
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, indexBuffer);
        gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, geometry.indices, gl.STATIC_DRAW);

        gl.bindVertexArray(null);

        return {
            vao,
            buffers: { positionBuffer, normalBuffer, modelBuffer, tintBuffer, indexBuffer },
            indexCount: geometry.indices.length,
            instances: instanceTints.length / 4
        };
    }

    const cards = uploadMesh(mesh, models, tints, gl.DYNAMIC_DRAW);

    // ── The plank the cards stand on ────────────────────────────────────────────────────
    //
    // This is not scenery. Without a surface to receive them, the shadow-map pass renders
    // shadows that land on nothing and the entire lighting model is invisible — cards float in
    // a void and read as flat sprites. The contact shadow on this plank is the single cue that
    // makes the scene three-dimensional, which is the whole reason the shelf exists.
    //
    // Its own mesh rather than a scaled card, because the model matrix carries no scale on
    // purpose: a non-uniform scale would break the `mat3(aModel)` normal transform in the vertex
    // shader and light every surface wrongly.
    // Deep enough to leave clear surface in FRONT of the cards. The shadows land there, and a
    // plank that stops at the cards' feet would receive them entirely in its own occluded strip.
    const plankMesh = buildRoundedBox([3.15, 0.07, 1.55], 0.06, 6);
    const plankModel = cardMatrix(0, -0.98, -0.15, 0);
    // Light enough for a shadow to register against. The first version was near-black, which
    // technically received the shadows and showed none of them — a shadow is a contrast, and
    // there is no contrast available below the ambient floor.
    // Alpha 0 = no rim light. A rim would outline the plank as an object; it should read as a
    // surface the cards are on, not as another card lying down.
    const plankTint = new Float32Array([0.34, 0.29, 0.46, 0.0]);
    const plank = uploadMesh(plankMesh, plankModel, plankTint, gl.STATIC_DRAW);

    const uniforms = {
        depth: { lightViewProjection: gl.getUniformLocation(programs.depth, 'uLightViewProjection') },
        scene: {
            viewProjection: gl.getUniformLocation(programs.scene, 'uViewProjection'),
            lightViewProjection: gl.getUniformLocation(programs.scene, 'uLightViewProjection'),
            shadowMap: gl.getUniformLocation(programs.scene, 'uShadowMap'),
            lightDir: gl.getUniformLocation(programs.scene, 'uLightDir'),
            lightColor: gl.getUniformLocation(programs.scene, 'uLightColor'),
            cameraPos: gl.getUniformLocation(programs.scene, 'uCameraPos'),
            shadowTexel: gl.getUniformLocation(programs.scene, 'uShadowTexel')
        },
        blit: { scene: gl.getUniformLocation(programs.blit, 'uScene') }
    };

    const id = nextId++;
    const instance = {
        gl, surface, canvas, programs, uniforms, shadow, count,
        models, layout,
        cards, plank,
        scene: null,
        blitVao: gl.createVertexArray(),
        startTime: performance.now(),
        stop: null
    };

    /** Draws the plank and then the cards. Used by both the depth and the camera pass. */
    function drawScene() {
        gl.bindVertexArray(plank.vao);
        gl.drawElementsInstanced(gl.TRIANGLES, plank.indexCount, gl.UNSIGNED_SHORT, 0, 1);
        gl.bindVertexArray(cards.vao);
        gl.drawElementsInstanced(gl.TRIANGLES, cards.indexCount, gl.UNSIGNED_SHORT, 0, instance.count);
        gl.bindVertexArray(null);
    }

    instance.stop = gfx.addTask(`shelf#${id}`, (now) => {
        if (!surface.beginFrame()) {
            stop(id);
            return;
        }

        const width = surface.width;
        const height = surface.height;
        if (!instance.scene || instance.scene.width !== width || instance.scene.height !== height) {
            if (instance.scene) {
                gl.deleteTexture(instance.scene.color);
                gl.deleteRenderbuffer(instance.scene.depth);
                gl.deleteFramebuffer(instance.scene.fbo);
            }
            instance.scene = makeSceneTargets(gl, width, height);
            if (!instance.scene) {
                console.warn('[shelf] scene target unavailable; the list stands on its own');
                stop(id);
                return;
            }
        }

        const seconds = (now - instance.startTime) / 1000;

        // Update instance transforms. count is at most 24, so rebuilding every frame on the CPU
        // is cheaper than any scheme for tracking which ones changed.
        for (let i = 0; i < instance.count; i++) {
            const card = instance.layout[i];
            const bob = Math.sin(seconds * 0.9 + i * 0.55) * 0.06 * card.lift;
            const sway = Math.sin(seconds * 0.5 + i * 0.31) * 0.10;
            instance.models.set(
                cardMatrix(card.x, card.baseY + bob + card.lift * 0.18, card.z, card.rotation + sway),
                i * 16);
        }
        gl.bindBuffer(gl.ARRAY_BUFFER, cards.buffers.modelBuffer);
        gl.bufferSubData(gl.ARRAY_BUFFER, 0, instance.models);

        // The light orbits slowly so the shadows move — a static shadow map is indistinguishable
        // from a painted-on gradient, which is precisely the criticism this scene has to answer.
        // Above and BEHIND the shelf, so shadows are cast toward the camera and land on the open
        // plank in front of the cards. A near-overhead light (the obvious choice) drops each
        // shadow straight down into the card's own footprint, where it is completely hidden —
        // the shadow pass runs, costs its full price, and shows nothing.
        const lightDir = normalise([
            Math.cos(seconds * 0.18) * 0.55,
            0.78,
            Math.sin(seconds * 0.18) * 0.30 - 0.62
        ]);
        const lightView = lookAt(
            [lightDir[0] * 6, lightDir[1] * 6, lightDir[2] * 6], [0, 0, -0.6], [0, 1, 0]);
        const lightViewProjection = multiply(orthographic(3.6, 0.5, 14), lightView);

        // ── Pass 1: shadow map ──────────────────────────────────────────────────────────
        gl.bindFramebuffer(gl.FRAMEBUFFER, instance.shadow.fbo);
        gl.viewport(0, 0, SHADOW_SIZE, SHADOW_SIZE);
        gl.enable(gl.DEPTH_TEST);
        gl.depthFunc(gl.LEQUAL);
        gl.clear(gl.DEPTH_BUFFER_BIT);
        // Front-face culling in the depth pass moves acne to surfaces the camera cannot see.
        gl.enable(gl.CULL_FACE);
        gl.cullFace(gl.FRONT);

        gl.useProgram(programs.depth);
        gl.uniformMatrix4fv(uniforms.depth.lightViewProjection, false, lightViewProjection);
        drawScene();

        // ── Pass 2: camera view ─────────────────────────────────────────────────────────
        gl.bindFramebuffer(gl.FRAMEBUFFER, instance.scene.fbo);
        gl.viewport(0, 0, width, height);
        gl.cullFace(gl.BACK);
        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);

        const cameraPos = [
            Math.sin(seconds * 0.11) * 0.55,
            1.15,
            4.35
        ];
        const view = lookAt(cameraPos, [0, 0.05, -0.5], [0, 1, 0]);
        const projection = perspective(0.62, width / Math.max(1, height), 0.1, 40);
        const viewProjection = multiply(projection, view);

        gl.useProgram(programs.scene);
        gl.uniformMatrix4fv(uniforms.scene.viewProjection, false, viewProjection);
        gl.uniformMatrix4fv(uniforms.scene.lightViewProjection, false, lightViewProjection);
        gl.uniform3fv(uniforms.scene.lightDir, lightDir);
        gl.uniform3f(uniforms.scene.lightColor, 1.0, 0.94, 0.82);
        gl.uniform3fv(uniforms.scene.cameraPos, cameraPos);
        gl.uniform2f(uniforms.scene.shadowTexel, 1 / SHADOW_SIZE, 1 / SHADOW_SIZE);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, instance.shadow.texture);
        gl.uniform1i(uniforms.scene.shadowMap, 0);
        drawScene();

        // ── Pass 3: onto the pooled surface ─────────────────────────────────────────────
        gl.disable(gl.DEPTH_TEST);
        gl.disable(gl.CULL_FACE);
        surface.bindTarget();
        gl.useProgram(programs.blit);
        gl.bindVertexArray(instance.blitVao);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, instance.scene.color);
        gl.uniform1i(uniforms.blit.scene, 0);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        gl.bindVertexArray(null);

        surface.present();
    });

    instances.set(id, instance);
    canvas.dataset.shelfFx = 'on';
    return id;
}

export function stop(id) {
    const instance = instances.get(id);
    if (!instance) return;

    instance.stop?.();
    const { gl } = instance;

    for (const program of Object.values(instance.programs)) gl.deleteProgram(program);
    for (const mesh of [instance.cards, instance.plank]) {
        for (const buffer of Object.values(mesh.buffers)) gl.deleteBuffer(buffer);
        gl.deleteVertexArray(mesh.vao);
    }
    gl.deleteVertexArray(instance.blitVao);
    gl.deleteTexture(instance.shadow.texture);
    gl.deleteFramebuffer(instance.shadow.fbo);
    if (instance.scene) {
        gl.deleteTexture(instance.scene.color);
        gl.deleteRenderbuffer(instance.scene.depth);
        gl.deleteFramebuffer(instance.scene.fbo);
    }
    instance.surface.release();

    if (instance.canvas) {
        delete instance.canvas.dataset.shelfFx;
    }
    instances.delete(id);
}

gfx.onTierChanged((tier) => {
    if (tier !== 'full') {
        for (const id of [...instances.keys()]) {
            stop(id);
        }
    }
});
