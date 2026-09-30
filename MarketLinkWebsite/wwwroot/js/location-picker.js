/* Fills the coordinates of a place from the device, or by tapping the map. The
   map itself is drawn from tiles, so no third party page has to load in a frame. */
document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('[data-location-picker]').forEach(picker => {
        const lat = picker.querySelector('[data-location-lat]');
        const lon = picker.querySelector('[data-location-lon]');
        const detect = picker.querySelector('[data-location-detect]');
        const status = picker.querySelector('[data-location-status]');
        const canvas = picker.querySelector('[data-location-canvas]');

        if (!lat || !lon) return;

        const say = (message, kind) => {
            if (!status) return;
            status.textContent = message;
            status.className = 'location-picker-status small' + (kind ? ' is-' + kind : '');
        };

        const write = (a, b) => {
            lat.value = Number(a).toFixed(6);
            lon.value = Number(b).toFixed(6);
        };

        const read = () => {
            const a = parseFloat(lat.value);
            const b = parseFloat(lon.value);
            if (!isFinite(a) || !isFinite(b)) return null;
            if (a < -90 || a > 90 || b < -180 || b > 180) return null;
            return [a, b];
        };

        const start = (picker.getAttribute('data-start') || '').split(',').map(Number);
        const origin = start.length === 2 && isFinite(start[0]) && isFinite(start[1]) ? start : [33.6844, 73.0479];

        let map = null;
        let pin = null;

        if (canvas && typeof L !== 'undefined') {
            map = L.map(canvas, { zoomControl: true }).setView(origin, 13);
            L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
                maxZoom: 19,
                attribution: '&copy; OpenStreetMap contributors'
            }).addTo(map);

            const current = read();
            if (current) {
                pin = L.marker(current, { draggable: true }).addTo(map);
                pin.bindTooltip('Drag to adjust', { direction: 'top' });
            }

            map.on('click', event => {
                write(event.latlng.lat, event.latlng.lng);
                if (pin) {
                    pin.setLatLng(event.latlng);
                } else {
                    pin = L.marker(event.latlng, { draggable: true }).addTo(map);
                }
                say('Pin dropped. Check the numbers below, or drag the pin to adjust.', 'good');
            });

            if (pin) {
                pin.on('dragend', event => {
                    const point = event.target.getLatLng();
                    write(point.lat, point.lng);
                    say('Pin moved. The numbers below were updated.', 'good');
                });
            }
        }

        const refresh = () => {
            const value = read();
            if (!value || !map) return;

            if (pin) {
                pin.setLatLng(value);
            } else {
                pin = L.marker(value, { draggable: true }).addTo(map);
                pin.on('dragend', event => {
                    const point = event.target.getLatLng();
                    write(point.lat, point.lng);
                    say('Pin moved. The numbers below were updated.', 'good');
                });
            }

            map.setView(value, 15);
        };

        lat.addEventListener('change', () => {
            if (!read()) {
                say('Latitude must be a number between -90 and 90.', 'warn');
                return;
            }
            refresh();
            say('Location updated from the numbers you typed.', 'good');
        });

        lon.addEventListener('change', () => {
            if (!read()) {
                say('Longitude must be a number between -180 and 180.', 'warn');
                return;
            }
            refresh();
            say('Location updated from the numbers you typed.', 'good');
        });

        if (detect) {
            detect.addEventListener('click', () => {
                if (!navigator.geolocation) {
                    say('This browser cannot share a location. Tap the map instead.', 'warn');
                    return;
                }

                say('Asking your browser for your current location...', 'busy');
                detect.disabled = true;

                navigator.geolocation.getCurrentPosition(
                    position => {
                        detect.disabled = false;
                        write(position.coords.latitude, position.coords.longitude);
                        refresh();
                        say('Location captured. Tap the map again if you need a different spot.', 'good');
                    },
                    error => {
                        detect.disabled = false;
                        const reason = error.code === 1
                            ? 'Location access was blocked. Allow it in your browser, or tap the map.'
                            : 'Your location could not be found. Tap the map to drop the pin instead.';
                        say(reason, 'warn');
                    },
                    { enableHighAccuracy: true, timeout: 12000, maximumAge: 30000 }
                );
            });
        }
    });
});
