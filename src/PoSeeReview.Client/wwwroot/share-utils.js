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
     * @returns {Promise<'shared'|'cancelled'|'unsupported'>}
     */
    share: async function (title, text, url) {
        if (!this.isSupported()) {
            console.warn('Web Share API is not supported in this browser');
            return 'unsupported';
        }

        try {
            await navigator.share({
                title: title,
                text: text,
                url: url
            });
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
            this.showToast('Link copied to clipboard!');
        } catch (error) {
            console.error('Error copying to clipboard:', error);
            throw error;
        }
    },

    /**
     * Display an accessible toast alert notification
     * @param {string} message
     */
    showToast: function (message) {
        let toast = document.getElementById('posee-toast');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'posee-toast';
            toast.setAttribute('role', 'status');
            toast.setAttribute('aria-live', 'polite');
            toast.style.cssText = 'position: fixed; bottom: 24px; right: 24px; background: #7C3AED; color: white; padding: 12px 24px; border-radius: 999px; box-shadow: 0 4px 16px rgba(0,0,0,0.3); font-weight: 600; z-index: 9999; transition: opacity 0.3s ease-in-out; opacity: 0; pointer-events: none;';
            document.body.appendChild(toast);
        }
        toast.textContent = message;
        toast.style.opacity = '1';
        setTimeout(() => {
            toast.style.opacity = '0';
        }, 3000);
    }
};
