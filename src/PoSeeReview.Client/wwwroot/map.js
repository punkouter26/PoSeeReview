// Discovery map.
//
// WHY A LIBRARY HERE, WHEN three.js AND RAPIER WERE DELETED.
// Those two were ~2.4 MB of vendored decoration layered over a DOM list and a card grid that
// already worked — the scene said nothing the markup did not. A map is the opposite: it answers
// a question the grid physically cannot, which is "what is actually around me, and which of
// these will be instant and free". So the conditions from shelf.js apply instead of the verdict:
//
//   * LAZY. MapLibre is fetched by dynamic import() the first time someone opens the map, never
//     on load. This file itself is a few KB.
//   * THE LIST STAYS. The map is a panel ABOVE the results grid, not a replacement for it. The
//     grid remains in the DOM, focusable and readable by a screen reader, exactly as the
//     leaderboard list stays under the 3D shelf.
//   * FAILS QUIET. No CDN, no WebGL, no network: show() returns false, the caller hides the
//     panel, and discovery is what it always was.
//
// Published as `window.poseeMap`, matching the window.geolocation / window.shareUtils /
// window.poseeFx convention.

(function () {
    'use strict';

    // Pinned exactly. An unpinned CDN import is a third party silently shipping into this app.
    const MAPLIBRE_JS = 'https://cdn.jsdelivr.net/npm/maplibre-gl@4.7.1/+esm';
    const MAPLIBRE_CSS = 'https://cdn.jsdelivr.net/npm/maplibre-gl@4.7.1/dist/maplibre-gl.css';

    // TILE PROVIDER — read before shipping this to real traffic.
    // OpenStreetMap's own tile servers are a volunteer-funded resource with a usage policy that
    // rules out heavy application use. This default exists so the feature works with no key and
    // no account; a production deployment should point it at a provider it pays for (MapTiler,
    // Protomaps, Stadia) and update the attribution below to match. Attribution is not optional
    // and is rendered by MapLibre from this style.
    const RASTER_STYLE = {
        version: 8,
        sources: {
            osm: {
                type: 'raster',
                tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'],
                tileSize: 256,
                maxzoom: 19,
                attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
            }
        },
        layers: [{ id: 'osm', type: 'raster', source: 'osm' }]
    };

    let maplibre = null;      // the imported module, once
    let map = null;           // the live map instance
    let markers = [];         // live marker objects, so a reload can clear them
    let dotNetRef = null;     // .NET callback for a marker tap

    /** Loads MapLibre once. Returns null if it cannot be fetched. */
    async function loadLibrary() {
        if (maplibre) {
            return maplibre;
        }

        try {
            if (!document.querySelector('link[data-maplibre]')) {
                const link = document.createElement('link');
                link.rel = 'stylesheet';
                link.href = MAPLIBRE_CSS;
                link.setAttribute('data-maplibre', '');
                document.head.appendChild(link);
            }

            const module = await import(MAPLIBRE_JS);
            maplibre = module.default || module;
            return maplibre;
        } catch {
            // Blocked CDN, offline, or a browser that refuses the import. The list view is
            // already on screen, so there is nothing to recover — just decline.
            return null;
        }
    }

    /** Score-free colour ramp: cached comics read as "ready", the rest as "not drawn yet". */
    function markerColour(place, styles) {
        return place.hasComic
            ? (styles.ready || '#16A34A')
            : (styles.pending || '#7C3AED');
    }

    function clearMarkers() {
        markers.forEach(function (marker) { marker.remove(); });
        markers = [];
    }

    window.poseeMap = {
        /**
         * Renders (or re-renders) the discovery map.
         * @param {string} containerId - Element id to mount into.
         * @param {object} payload - { centre: {lat, lng}, places: [{placeId, name, lat, lng, hasComic, reviews}] }
         * @param {object} styles - Resolved design-token colours: { ready, pending }.
         * @param {object} dotNet - DotNetObjectReference exposing OnPlaceSelected(placeId).
         * @returns {Promise<boolean>} True when a map is on screen.
         */
        show: async function (containerId, payload, styles, dotNet) {
            const container = document.getElementById(containerId);
            if (!container || !payload || !Array.isArray(payload.places)) {
                return false;
            }

            const lib = await loadLibrary();
            if (!lib) {
                return false;
            }

            dotNetRef = dotNet;

            try {
                if (!map) {
                    map = new lib.Map({
                        container: container,
                        style: RASTER_STYLE,
                        center: [payload.centre.lng, payload.centre.lat],
                        zoom: 14,
                        // The map is a discovery aid, not a flight simulator. Rotation and pitch
                        // on a phone are almost always an accidental two-finger drag.
                        pitchWithRotate: false,
                        dragRotate: false,
                        attributionControl: { compact: true }
                    });

                    map.addControl(new lib.NavigationControl({ showCompass: false }), 'top-right');
                } else {
                    map.setCenter([payload.centre.lng, payload.centre.lat]);
                }

                clearMarkers();

                payload.places.forEach(function (place) {
                    if (typeof place.lat !== 'number' || typeof place.lng !== 'number') {
                        return;
                    }

                    const el = document.createElement('button');
                    el.type = 'button';
                    el.className = 'map-pin' + (place.hasComic ? ' map-pin--ready' : '');
                    el.style.setProperty('--pin-colour', markerColour(place, styles || {}));
                    // The pin is a real button with a real label. The grid underneath is the
                    // accessible path, but a control on screen still has to say what it is.
                    el.setAttribute('aria-label', place.name + (place.hasComic ? ' — comic ready' : ''));
                    el.title = place.name + (place.hasComic ? ' (comic ready — free and instant)' : '');

                    el.addEventListener('click', function (event) {
                        event.stopPropagation();
                        if (dotNetRef) {
                            dotNetRef.invokeMethodAsync('OnPlaceSelected', place.placeId);
                        }
                    });

                    markers.push(new lib.Marker({ element: el })
                        .setLngLat([place.lng, place.lat])
                        .addTo(map));
                });

                // Resizes matter here: the panel is toggled open, so the container had zero
                // height when MapLibre first measured it.
                setTimeout(function () { if (map) { map.resize(); } }, 50);

                return true;
            } catch {
                return false;
            }
        },

        /** Tears the map down. Called when the panel closes or the component is disposed. */
        hide: function () {
            try {
                clearMarkers();
                if (map) {
                    map.remove();
                    map = null;
                }
                dotNetRef = null;
                return true;
            } catch {
                map = null;
                return false;
            }
        }
    };
})();
