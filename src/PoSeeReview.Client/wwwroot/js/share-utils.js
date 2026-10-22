// Web Share API utilities for sharing comics on social media
window.shareUtils = {
    /**
     * Check if the Web Share API is supported in the current browser
     * @returns {boolean} True if Web Share API is available
     */
    isSupported: function () {
        return navigator.share !== undefined;
    },

    /**
     * Share content using the Web Share API.
     * Cancelling is reported separately from failure so the caller can leave the user alone
     * instead of falling back to a clipboard copy they did not ask for.
     * @param {string} title - Title of the shared content
     * @param {string} text - Description text for the share
     * @param {string} url - URL to share
     * @param {string} [placeId] - When given, the comic PNG itself is attached where the
     *                             browser can share files; the link rides along in the text.
     * @returns {Promise<'shared'|'cancelled'|'unsupported'>}
     */
    share: async function (title, text, url, placeId) {
        if (!this.isSupported()) {
            console.warn('Web Share API is not supported in this browser');
            return 'unsupported';
        }

        try {
            // The image is the thing people post; a link unfurls into a card at best. Many share
            // targets drop `url` when files are present, so the link goes into the text instead.
            const file = placeId ? await this.comicFile(placeId) : null;
            const payload = file && navigator.canShare?.({ files: [file] })
                ? { title: title, text: `${text} ${url}`.trim(), files: [file] }
                : { title: title, text: text, url: url };

            await navigator.share(payload);
            return 'shared';
        } catch (error) {
            if (error.name === 'AbortError') {
                console.log('Share cancelled by user');
                return 'cancelled';
            }
            console.error('Error sharing:', error);
            return 'unsupported';
        }
    },

    /**
     * The comic's bytes from this origin, not the blob URL the page displays: Blob Storage sends
     * no CORS headers, so fetching the blob directly fails outright.
     */
    fetchComic: function (placeId) {
        return fetch(`/api/comics/${encodeURIComponent(placeId)}/image`, { credentials: 'same-origin' });
    },

    /**
     * The comic PNG as a File, fetched through the app's own origin (see fetchComic). Null on any failure (an expired comic 404s), so sharing falls back to the link.
     * @param {string} placeId
     * @returns {Promise<File|null>}
     */
    comicFile: async function (placeId) {
        try {
            const response = await this.fetchComic(placeId);
            if (!response.ok) {
                return null;
            }

            const blob = await response.blob();
            // Newer comics are WebP; a share target trusts the extension, so it must match the type.
            const type = blob.type || 'image/png';
            return new File([blob], `poseereview-comic.${type.split('/')[1]}`, { type });
        } catch {
            return null;
        }
    },

    /**
     * Copy text to clipboard as a fallback for browsers without Web Share API
     * @param {string} text - Text to copy to clipboard
     * @returns {Promise<void>}
     */
    copyToClipboard: async function (text) {
        try {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                await navigator.clipboard.writeText(text);
                console.log('Copied to clipboard:', text);
            } else {
                // Fallback for older browsers
                const textArea = document.createElement('textarea');
                textArea.value = text;
                textArea.style.position = 'fixed';
                textArea.style.left = '-999999px';
                textArea.style.top = '-999999px';
                document.body.appendChild(textArea);
                textArea.focus();
                textArea.select();
                
                try {
                    document.execCommand('copy');
                    console.log('Copied to clipboard (fallback):', text);
                } catch (err) {
                    console.error('Fallback: Could not copy text: ', err);
                }
                
                document.body.removeChild(textArea);
            }
        } catch (error) {
            console.error('Error copying to clipboard:', error);
            throw error;
        }
    }
};
