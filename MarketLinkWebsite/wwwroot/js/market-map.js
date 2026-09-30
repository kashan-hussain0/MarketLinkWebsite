/* A small map engine built on Leaflet, served from this site rather than a CDN
   so it always works. It renders markets and farmer stalls as separate markers,
   keeps a list beside the map, and hands directions to the customer for the
   exact pickup point. */
(function () {
    'use strict';

    const TILE = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png';
    const ATTRIBUTION = '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors';

    function readMarkers(root) {
        const raw = root.getAttribute('data-markers');
        if (!raw) return [];
        try {
            return JSON.parse(raw);
        } catch {
            return [];
        }
    }

    function directionUrl(lat, lon) {
        return 'https://www.openstreetmap.org/directions?to=' + lat + '%2C' + lon;
    }

    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function popupHtml(marker) {
        const parts = [];
        parts.push('<div class="map-popup">');

        if (marker.image) {
            parts.push('<img class="map-popup-image" src="' + escapeHtml(marker.image) + '" alt="" loading="lazy" />');
        }

        parts.push('<span class="map-popup-kind map-popup-kind-' + escapeHtml(marker.kind) + '">' +
            (marker.kind === 'farmer' ? 'Farmer stall' : 'Market') + '</span>');
        parts.push('<h3 class="map-popup-title">' + escapeHtml(marker.title) + '</h3>');

        if (marker.subtitle) {
            parts.push('<p class="map-popup-text">' + escapeHtml(marker.subtitle) + '</p>');
        }

        if (marker.meta && marker.meta.length) {
            parts.push('<ul class="map-popup-meta">');
            marker.meta.forEach(item => {
                parts.push('<li><span>' + escapeHtml(item.label) + '</span><strong>' + escapeHtml(item.value) + '</strong></li>');
            });
            parts.push('</ul>');
        }

        if (marker.stall) {
            parts.push('<p class="map-popup-stall">Stall ' + escapeHtml(marker.stall) + '</p>');
        }

        if (marker.href) {
            parts.push('<a class="map-popup-link" href="' + escapeHtml(marker.href) + '">View details</a>');
        }

        if (marker.kind === 'farmer' || marker.kind === 'market') {
            parts.push('<a class="map-popup-directions" target="_blank" rel="noopener noreferrer" href="' +
                directionUrl(marker.lat, marker.lon) + '">Get pickup directions</a>');
        }

        parts.push('</div>');
        return parts.join('');
    }

    function renderList(root, markers, onSelect) {
        const list = root.querySelector('[data-map-list]');
        if (!list) return;

        if (markers.length === 0) {
            list.innerHTML = '<p class="map-list-empty">Nothing with a pinned location is available yet.</p>';
            return;
        }

        list.innerHTML = markers.map((marker, index) => {
            const detail = marker.meta && marker.meta.length
                ? '<span class="map-list-meta">' + marker.meta.map(i => escapeHtml(i.value)).join(' · ') + '</span>'
                : '';
            return '<button type="button" class="map-list-item map-list-item-' + escapeHtml(marker.kind) + '" data-map-index="' + index + '">' +
                '<span class="map-list-dot" aria-hidden="true"></span>' +
                '<span class="map-list-text">' +
                    '<strong>' + escapeHtml(marker.title) + '</strong>' +
                    (marker.subtitle ? '<small>' + escapeHtml(marker.subtitle) + '</small>' : '') +
                    detail +
                '</span>' +
                (marker.kind === 'farmer' || marker.kind === 'market'
                    ? '<span class="map-list-go" aria-hidden="true"><i class="bi bi-sign-turn-right"></i></span>'
                    : '') +
                '</button>';
        }).join('');

        list.querySelectorAll('[data-map-index]').forEach(button => {
            button.addEventListener('click', () => onSelect(Number(button.getAttribute('data-map-index'))));
        });
    }

    function boot(root) {
        const markers = readMarkers(root);
        const canvas = root.querySelector('[data-map-canvas]');
        if (!canvas || typeof L === 'undefined') return;

        const placed = markers.filter(m => Number.isFinite(m.lat) && Number.isFinite(m.lon) && !(m.lat === 0 && m.lon === 0));
        const start = placed.length
            ? placed[0]
            : { lat: 33.6844, lon: 73.0479 };

        const map = L.map(canvas, { scrollWheelZoom: false, zoomControl: true })
            .setView([start.lat, start.lon], placed.length > 1 ? 12 : 14);

        L.tileLayer(TILE, { maxZoom: 19, attribution: ATTRIBUTION }).addTo(map);

        const layers = { market: L.layerGroup().addTo(map), farmer: L.layerGroup().addTo(map) };
        const byIndex = new Map();

        const greenIcon = L.icon({
            iconUrl: '/lib/leaflet/images/marker-icon.png',
            iconRetinaUrl: '/lib/leaflet/images/marker-icon-2x.png',
            shadowUrl: '/lib/leaflet/images/marker-shadow.png',
            iconSize: [25, 41],
            iconAnchor: [12, 41],
            popupAnchor: [1, -34],
            shadowSize: [41, 41],
            className: 'map-pin map-pin-' + start.kind
        });

        markers.forEach((marker, index) => {
            if (!Number.isFinite(marker.lat) || !Number.isFinite(marker.lon)) return;
            if (marker.lat === 0 && marker.lon === 0) return;

            const pin = L.marker([marker.lat, marker.lon], {
                icon: L.icon(Object.assign({}, greenIcon.options, { className: 'map-pin map-pin-' + marker.kind })),
                title: marker.title,
                alt: marker.title,
                keyboard: true
            });

            pin.bindPopup(popupHtml(marker), { maxWidth: 280, className: 'map-popup-wrap' });
            const group = layers[marker.kind === 'farmer' ? 'farmer' : 'market'];
            if (group) pin.addTo(group);

            byIndex.set(index, pin);
        });

        const focus = index => {
            const pin = byIndex.get(index);
            if (!pin) return;
            map.setView(pin.getLatLng(), Math.max(map.getZoom(), 15), { animate: true });
            pin.openPopup();
        };

        if (placed.length > 1) {
            const group = L.featureGroup(Array.from(byIndex.values()));
            map.fitBounds(group.getBounds().pad(0.18));
        }

        renderList(root, markers, focus);

        const marketToggle = root.querySelector('[data-map-toggle="market"]');
        const farmerToggle = root.querySelector('[data-map-toggle="farmer"]');

        if (marketToggle) {
            marketToggle.addEventListener('click', () => {
                const on = map.hasLayer(layers.market);
                if (on) map.removeLayer(layers.market); else map.addLayer(layers.market);
                marketToggle.setAttribute('aria-pressed', (!on).toString());
                marketToggle.classList.toggle('is-off', on);
            });
        }

        if (farmerToggle) {
            farmerToggle.addEventListener('click', () => {
                const on = map.hasLayer(layers.farmer);
                if (on) map.removeLayer(layers.farmer); else map.addLayer(layers.farmer);
                farmerToggle.setAttribute('aria-pressed', (!on).toString());
                farmerToggle.classList.toggle('is-off', on);
            });
        }

        const locate = root.querySelector('[data-map-locate]');
        if (locate) {
            locate.addEventListener('click', () => {
                if (!navigator.geolocation) return;
                locate.disabled = true;
                navigator.geolocation.getCurrentPosition(position => {
                    locate.disabled = false;
                    const here = [position.coords.latitude, position.coords.longitude];
                    L.circleMarker(here, {
                        radius: 8,
                        color: '#2f5d2a',
                        fillColor: '#7db2ea',
                        fillOpacity: 1,
                        weight: 3
                    }).addTo(map).bindPopup('You are here');
                    map.setView(here, 13);
                }, () => { locate.disabled = false; }, { enableHighAccuracy: true, timeout: 12000 });
            });
        }

        root.classList.add('is-ready');
    }

    window.marketMap = { boot: boot, directionUrl: directionUrl, popupHtml: popupHtml };

    function bootAll() {
        document.querySelectorAll('[data-market-map]').forEach(boot);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', bootAll);
    } else {
        bootAll();
    }
})();
