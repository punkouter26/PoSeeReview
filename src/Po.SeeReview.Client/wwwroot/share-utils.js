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
     * Share content using the Web Share API
     * @param {string} title - Title of the shared content
     * @param {string} text - Description text for the share
     * @param {string} url - URL to share
     * @returns {Promise<boolean>} True if share was successful, false if cancelled or failed
     */
    share: async function (title, text, url) {
        if (!this.isSupported()) {
            console.warn('Web Share API is not supported in this browser');
            return false;
        }

        try {
            await navigator.share({
                title: title,
                text: text,
                url: url
            });
            return true;
        } catch (error) {
            // User cancelled the share or an error occurred
            if (error.name === 'AbortError') {
                console.log('Share cancelled by user');
                return false;
            }
            console.error('Error sharing:', error);
            return false;
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
