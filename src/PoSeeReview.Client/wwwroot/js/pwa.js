// Install-to-homescreen support.
//
// Publishes window.poseePwa to match the existing window.geolocation / window.shareUtils /
// window.poseeFx convention.
//
// The `beforeinstallprompt` event fires once, early, and is only usable if it was captured and
// its default prevented at that moment — by the time a Blazor component has rendered and asked,
// the browser has already shown (or suppressed) its own banner. So this script runs at document
// load, before the app boots, and stashes the event for the app to use later.
//
// Nothing here throws into .NET. A browser with no install support simply reports "not
// installable" and the app shows no prompt, which is the correct outcome on iOS Safari and in
// every desktop browser that has not implemented this.
(function () {
    'use strict';

    /** The stashed beforeinstallprompt event, or null once used/never fired. */
    let deferredPrompt = null;

    window.addEventListener('beforeinstallprompt', function (event) {
        // Suppresses the browser's own mini-infobar so the app can ask at a better moment —
        // after a comic has actually been generated, rather than on a cold first paint.
        event.preventDefault();
        deferredPrompt = event;
    });

    window.addEventListener('appinstalled', function () {
        deferredPrompt = null;
    });

    window.poseePwa = {
        /**
         * Whether a native install prompt is available right now.
         * @returns {boolean}
         */
        canInstall: function () {
            return deferredPrompt !== null;
        },

        /**
         * Whether the app is already running as an installed app, in which case no prompt of
         * any kind should ever be shown. `standalone` is the iOS Safari spelling.
         * @returns {boolean}
         */
        isInstalled: function () {
            try {
                return window.matchMedia('(display-mode: standalone)').matches
                    || window.navigator.standalone === true;
            } catch {
                return false;
            }
        },

        /**
         * Whether this is iOS Safari, which supports installation but exposes no prompt API —
         * the user has to go through the Share sheet, so the app has to say so in words.
         * @returns {boolean}
         */
        needsManualInstructions: function () {
            const ua = window.navigator.userAgent || '';
            const isIos = /iPad|iPhone|iPod/.test(ua)
                || (ua.includes('Macintosh') && 'ontouchend' in document);
            const isSafari = /Safari/.test(ua) && !/CriOS|FxiOS|EdgiOS|Chrome/.test(ua);
            return isIos && isSafari && !this.isInstalled();
        },

        /**
         * Shows the native install prompt.
         * @returns {Promise<'accepted'|'dismissed'|'unavailable'>}
         */
        promptInstall: async function () {
            if (!deferredPrompt) {
                return 'unavailable';
            }

            try {
                deferredPrompt.prompt();
                const choice = await deferredPrompt.userChoice;

                // The event is single-use whatever the answer: calling prompt() twice throws.
                deferredPrompt = null;

                return choice && choice.outcome === 'accepted' ? 'accepted' : 'dismissed';
            } catch (error) {
                console.warn('Install prompt failed:', error);
                deferredPrompt = null;
                return 'unavailable';
            }
        }
    };
})();
