// Geolocation service wrapper for Blazor interop
window.geolocation = {
    // Check if geolocation is supported
    isSupported: function () {
        return 'geolocation' in navigator;
    },

    // Get current position
    getCurrentPosition: function () {
        return new Promise((resolve, reject) => {
            if (!('geolocation' in navigator)) {
                reject({
                    success: false,
                    errorMessage: 'Geolocation is not supported by your browser'
                });
                return;
            }

            navigator.geolocation.getCurrentPosition(
                (position) => {
                    resolve({
                        success: true,
                        latitude: position.coords.latitude,
                        longitude: position.coords.longitude,
                        accuracy: position.coords.accuracy,
                        errorMessage: ''
                    });
                },
                (error) => {
                    let errorMessage = 'Unknown error occurred';
                    switch (error.code) {
                        case error.PERMISSION_DENIED:
                            errorMessage = 'Location permission denied. Please enable location access in your browser settings.';
                            break;
                        case error.POSITION_UNAVAILABLE:
                            errorMessage = 'Location information is unavailable.';
                            break;
                        case error.TIMEOUT:
                            errorMessage = 'Location request timed out.';
                            break;
                    }
                    reject({
                        success: false,
                        latitude: 0,
                        longitude: 0,
                        accuracy: null,
                        errorMessage: errorMessage
                    });
                },
                {
                    enableHighAccuracy: true,
                    timeout: 10000,
                    maximumAge: 0
                }
            );
        });
    }
};
