// Leaflet glue for the job form's site map (SitePicker.razor). One map per
// element id; the pin is draggable and a click on an empty map drops one.
// Blazor is told about moves through OnPinMoved on the component reference.
window.workitSiteMap = {
    maps: {},

    show(id, lat, lng, dotnetRef) {
        const el = document.getElementById(id);
        if (!el || !window.L) return;
        let m = this.maps[id];
        if (!m) {
            const map = L.map(id, { scrollWheelZoom: false, attributionControl: true });
            L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
                maxZoom: 19,
                attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
            }).addTo(map);
            m = this.maps[id] = { map, marker: null, ref: dotnetRef };
            map.on("click", e => this.setPin(id, e.latlng.lat, e.latlng.lng, true));
        }
        m.ref = dotnetRef;
        if (lat != null && lng != null) {
            this.setPin(id, lat, lng, false);
            m.map.setView([lat, lng], Math.max(m.map.getZoom() || 0, 16));
        } else {
            if (m.marker) { m.marker.remove(); m.marker = null; }
            m.map.setView([64.96, -19.02], 6); // all of Iceland
        }
        // The form may have been laid out while hidden; Leaflet needs the real size.
        setTimeout(() => m.map.invalidateSize(), 50);
    },

    setPin(id, lat, lng, notify) {
        const m = this.maps[id];
        if (!m) return;
        if (!m.marker) {
            m.marker = L.marker([lat, lng], { draggable: true }).addTo(m.map);
            m.marker.on("dragend", () => {
                const p = m.marker.getLatLng();
                m.ref.invokeMethodAsync("OnPinMoved", p.lat, p.lng);
            });
        } else {
            m.marker.setLatLng([lat, lng]);
        }
        if (notify) m.ref.invokeMethodAsync("OnPinMoved", lat, lng);
    },

    destroy(id) {
        const m = this.maps[id];
        if (m) { m.map.remove(); delete this.maps[id]; }
    }
};
