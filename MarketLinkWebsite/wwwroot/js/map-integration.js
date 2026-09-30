// MarketLink map integration.
// Handles map provider switching, marker rendering, and directions links.
window.MarketLinkMaps = window.MarketLinkMaps || {
    provider: "OpenStreetMap",
    isReady: false,
    pins: [],

    init: function (options) {
        options = options || {};
        this.provider = options.provider || "OpenStreetMap";
        this.apiKey = options.apiKey || "";
        this.pins = options.pins || [];
        this.isReady = true;

        if (this.provider === "Google" && this.apiKey) {
            this.loadGoogleMaps();
        }

        this.bindMapLinks();
        this.bindMarketCards();
    },

    loadGoogleMaps: function () {
        if (window.google && window.google.maps) {
            this.renderGoogleMarkers();
            return;
        }

        var self = this;
        var script = document.createElement("script");
        script.src = "https://maps.googleapis.com/maps/api/js?key=" + encodeURIComponent(this.apiKey) + "&callback=__marketLinkGoogleReady";
        script.async = true;
        script.defer = true;
        document.head.appendChild(script);

        window.__marketLinkGoogleReady = function () {
            self.renderGoogleMarkers();
        };
    },

    renderGoogleMarkers: function () {
        var self = this;
        document.querySelectorAll("[data-google-map]").forEach(function (container) {
            var lat = parseFloat(container.getAttribute("data-lat")) || 0;
            var lon = parseFloat(container.getAttribute("data-lon")) || 0;
            var zoom = parseInt(container.getAttribute("data-zoom"), 10) || 14;

            if (!window.google || !window.google.maps || lat === 0 || lon === 0) {
                return;
            }

            var map = new window.google.maps.Map(container, {
                center: { lat: lat, lng: lon },
                zoom: zoom,
                mapTypeId: "roadmap"
            });

            self.pins.forEach(function (pin) {
                new window.google.maps.Marker({
                    position: { lat: pin.lat, lng: pin.lon },
                    map: map,
                    title: pin.label || ""
                });
            });
        });
    },

    bindMapLinks: function () {
        var self = this;
        document.querySelectorAll("[data-directions-link]").forEach(function (link) {
            link.addEventListener("click", function (event) {
                var lat = link.getAttribute("data-lat");
                var lon = link.getAttribute("data-lon");
                var label = link.getAttribute("data-label") || "destination";

                if (!lat || !lon) {
                    return;
                }

                event.preventDefault();
                var url = self.buildDirectionsUrl(lat, lon, label);
                window.open(url, "_blank", "noopener,noreferrer");
            });
        });
    },

    bindMarketCards: function () {
        var self = this;
        document.querySelectorAll("[data-market-map-trigger]").forEach(function (card) {
            card.addEventListener("click", function () {
                var lat = card.getAttribute("data-lat");
                var lon = card.getAttribute("data-lon");
                var name = card.getAttribute("data-name") || "Market";

                if (!lat || !lon) {
                    return;
                }

                var mapCard = document.querySelector(".market-map-card iframe");
                if (mapCard) {
                    mapCard.scrollIntoView({ behavior: "smooth", block: "center" });
                }

                var url = self.buildDirectionsUrl(lat, lon, name);
                window.open(url, "_blank", "noopener,noreferrer");
            });
        });
    },

    buildDirectionsUrl: function (lat, lon, label) {
        if (this.provider === "Google" && this.apiKey) {
            return "https://www.google.com/maps/dir/?api=1&destination=" + encodeURIComponent(lat + "," + lon) + "&destination_place_id=" + encodeURIComponent(label || "");
        }
        return "https://www.openstreetmap.org/directions?to=" + encodeURIComponent(lat + "," + lon);
    },

    buildEmbedUrl: function (lat, lon, zoom) {
        zoom = zoom || 14;
        if (this.provider === "Google" && this.apiKey) {
            return "https://www.google.com/maps/embed/v1/view?key=" + encodeURIComponent(this.apiKey) + "&center=" + lat + "," + lon + "&zoom=" + zoom + "&maptype=roadmap";
        }
        var offset = 0.01;
        var west = Math.max(-180, lon - offset);
        var east = Math.min(180, lon + offset);
        var south = Math.max(-90, lat - offset);
        var north = Math.min(90, lat + offset);
        return "https://www.openstreetmap.org/export/embed.html?bbox=" + west + "%2C" + south + "%2C" + east + "%2C" + north + "&layer=mapnik&marker=" + lat + "%2C" + lon + "&z=" + zoom;
    }
};

document.addEventListener("DOMContentLoaded", function () {
    var provider = document.body.getAttribute("data-map-provider") || "OpenStreetMap";
    var apiKey = document.body.getAttribute("data-map-api-key") || "";
    window.MarketLinkMaps.init({ provider: provider, apiKey: apiKey });
});
