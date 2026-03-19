(function () {
    const DIRECT_TYPES = new Set(["", "gcj02", "amap", "gaode", "unknown"]);
    const hostState = {
        hoverInfoWindow: null,
        map: null,
        markers: new Map(),
        points: [],
        ready: false,
        selectedDeviceCode: null,
        selectedInfoWindow: null
    };

    function bridge(type, payload) {
        window.TyslAmapBridge && window.TyslAmapBridge.postMessage(type, payload);
    }

    function escapeHtml(value) {
        return String(value || "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function normalizeCoordinateType(value) {
        const normalized = String(value || "").trim().toLowerCase();

        if (!normalized) {
            return "";
        }

        if (normalized.includes("bd09") || normalized.includes("baidu")) {
            return "bd09";
        }

        if (normalized.includes("gps") || normalized.includes("wgs84")) {
            return "gps";
        }

        if (normalized.includes("mapbar")) {
            return "mapbar";
        }

        if (normalized.includes("gcj02") || normalized.includes("amap") || normalized.includes("gaode")) {
            return "gcj02";
        }

        return normalized;
    }

    function buildMarkerContent(point, selected) {
        const selectedClass = selected ? " is-selected" : "";
        const dispatchStateKey = escapeHtml(point.dispatchStateKey || "none");
        return [
            `<div class="marker-shell marker-${escapeHtml(point.visualState)}${selectedClass}">`,
            `  <span class="marker-dot"></span>`,
            `  <span class="marker-status marker-status--${dispatchStateKey}"></span>`,
            `  <span class="marker-label">${escapeHtml(point.displayName || point.deviceName || point.deviceCode)}</span>`,
            `</div>`
        ].join("");
    }

    function buildCardContent(point) {
        const dispatchBadge = point.dispatchStateText && point.dispatchStateText !== "未处置"
            ? `<span class="point-card__badge point-card__badge--dispatch">${escapeHtml(point.dispatchStateText)}</span>`
            : "";
        return [
            '<div class="point-card">',
            `  <div class="point-card__title">${escapeHtml(point.displayName || point.deviceName || point.deviceCode)}</div>`,
            '  <div class="point-card__badges">',
            `    <span class="point-card__badge">${escapeHtml(point.statusText)}</span>`,
            `    <span class="point-card__badge">${escapeHtml(point.monitoringText)}</span>`,
            `    ${dispatchBadge}`,
            "  </div>",
            `  <div class="point-card__summary">${escapeHtml(point.summaryText)}</div>`,
            "</div>"
        ].join("");
    }

    function ensureHoverInfoWindow() {
        if (!hostState.hoverInfoWindow) {
            hostState.hoverInfoWindow = new AMap.InfoWindow({
                offset: new AMap.Pixel(0, -22),
                closeWhenClickMap: false,
                isCustom: true
            });
        }

        return hostState.hoverInfoWindow;
    }

    function ensureSelectedInfoWindow() {
        if (!hostState.selectedInfoWindow) {
            hostState.selectedInfoWindow = new AMap.InfoWindow({
                offset: new AMap.Pixel(0, -22),
                closeWhenClickMap: false,
                isCustom: true
            });
        }

        return hostState.selectedInfoWindow;
    }

    function applyMarkerSelection() {
        hostState.markers.forEach((entry, deviceCode) => {
            entry.marker.setContent(buildMarkerContent(entry.point, deviceCode === hostState.selectedDeviceCode));
        });

        if (!hostState.selectedDeviceCode) {
            if (hostState.selectedInfoWindow) {
                hostState.selectedInfoWindow.close();
            }

            return;
        }

        const entry = hostState.markers.get(hostState.selectedDeviceCode);
        if (!entry) {
            if (hostState.selectedInfoWindow) {
                hostState.selectedInfoWindow.close();
            }

            return;
        }

        const infoWindow = ensureSelectedInfoWindow();
        infoWindow.setContent(buildCardContent(entry.point));
        infoWindow.open(hostState.map, entry.position);
        hostState.map && hostState.map.panTo(entry.position);
    }

    function showHoverCard(deviceCode) {
        if (hostState.selectedDeviceCode === deviceCode) {
            return;
        }

        const entry = hostState.markers.get(deviceCode);
        if (!entry) {
            return;
        }

        const infoWindow = ensureHoverInfoWindow();
        infoWindow.setContent(buildCardContent(entry.point));
        infoWindow.open(hostState.map, entry.position);
    }

    function hideHoverCard(deviceCode) {
        if (hostState.selectedDeviceCode === deviceCode) {
            return;
        }

        if (hostState.hoverInfoWindow) {
            hostState.hoverInfoWindow.close();
        }
    }

    function convertBatch(points, type) {
        return new Promise((resolve) => {
            const locations = points.map((point) => [point.platformRawLongitude, point.platformRawLatitude]);
            AMap.convertFrom(locations, type, function (status, result) {
                if (status !== "complete" || !result || !Array.isArray(result.locations)) {
                    resolve([]);
                    return;
                }

                resolve(result.locations.map((location, index) => ({
                    deviceCode: points[index].deviceCode,
                    latitude: Number(location.lat),
                    longitude: Number(location.lng)
                })));
            });
        });
    }

    async function normalizePoints(points) {
        const direct = [];
        const converted = [];
        const groups = new Map();

        for (const point of points || []) {
            const hasManualCoordinate = typeof point.manualLongitude === "number" && typeof point.manualLatitude === "number";
            const hasPlatformCoordinate = typeof point.platformRawLongitude === "number" && typeof point.platformRawLatitude === "number";

            if (hasManualCoordinate && !hasPlatformCoordinate) {
                direct.push({
                    point: point,
                    latitude: point.manualLatitude,
                    longitude: point.manualLongitude
                });
                continue;
            }

            if (!hasPlatformCoordinate) {
                if (hasManualCoordinate) {
                    direct.push({
                        point: point,
                        latitude: point.manualLatitude,
                        longitude: point.manualLongitude
                    });
                }

                continue;
            }

            const coordinateType = normalizeCoordinateType(point.rawCoordinateType);
            if (DIRECT_TYPES.has(coordinateType)) {
                direct.push({
                    point: point,
                    latitude: point.platformRawLatitude,
                    longitude: point.platformRawLongitude
                });
                continue;
            }

            if (!groups.has(coordinateType)) {
                groups.set(coordinateType, []);
            }

            groups.get(coordinateType).push(point);
        }

        for (const [coordinateType, group] of groups.entries()) {
            const mappedType = coordinateType === "bd09" ? "baidu" : coordinateType;
            const results = await convertBatch(group, mappedType);
            results.forEach((item) => {
                const point = group.find((candidate) => candidate.deviceCode === item.deviceCode);
                if (!point) {
                    return;
                }

                converted.push({
                    point: point,
                    latitude: item.latitude,
                    longitude: item.longitude
                });
            });
        }

        return direct.concat(converted);
    }

    function clearMarkers() {
        hostState.markers.forEach((entry) => {
            hostState.map && hostState.map.remove(entry.marker);
        });
        hostState.markers.clear();
    }

    function updateMapCursor(coordinatePickActive) {
        const shell = document.getElementById("map-shell");
        if (!shell) {
            return;
        }

        shell.classList.toggle("is-picking", Boolean(coordinatePickActive));
    }

    async function renderState(state) {
        if (!hostState.map) {
            return;
        }

        hostState.points = state.points || [];
        hostState.selectedDeviceCode = state.selectedDeviceCode || null;
        updateMapCursor(state.coordinatePickActive);

        const resolvedPoints = await normalizePoints(hostState.points);
        clearMarkers();

        const renderedPoints = [];
        const markers = [];

        resolvedPoints.forEach((item) => {
            const point = item.point;
            const position = new AMap.LngLat(item.longitude, item.latitude);
            const marker = new AMap.Marker({
                anchor: "bottom-center",
                content: buildMarkerContent(point, point.deviceCode === hostState.selectedDeviceCode),
                offset: new AMap.Pixel(0, 0),
                position: position
            });

            marker.on("mouseover", function () {
                showHoverCard(point.deviceCode);
            });

            marker.on("mouseout", function () {
                hideHoverCard(point.deviceCode);
            });

            marker.on("click", function () {
                hostState.selectedDeviceCode = point.deviceCode;
                applyMarkerSelection();
                bridge("marker-click", {
                    deviceCode: point.deviceCode
                });
            });

            hostState.map.add(marker);
            hostState.markers.set(point.deviceCode, {
                marker: marker,
                point: point,
                position: position
            });
            markers.push(marker);
            renderedPoints.push({
                deviceCode: point.deviceCode,
                longitude: Number(item.longitude.toFixed(6)),
                latitude: Number(item.latitude.toFixed(6))
            });
        });

        if (!hostState.selectedDeviceCode && markers.length > 0) {
            hostState.map.setFitView(markers, false, [72, 72, 72, 72], 16);
        }

        applyMarkerSelection();
        bridge("rendered-points", renderedPoints);
    }

    function loadAmapScript(config) {
        return new Promise((resolve, reject) => {
            if (window.AMap) {
                resolve(window.AMap);
                return;
            }

            const script = document.createElement("script");
            script.src = `https://webapi.amap.com/maps?v=2.0&key=${encodeURIComponent(config.key)}&plugin=AMap.Scale,AMap.ToolBar`;
            script.async = true;
            script.onload = () => resolve(window.AMap);
            script.onerror = reject;
            document.head.appendChild(script);
        });
    }

    async function initialize() {
        const config = window.__TYSL_AMAP_CONFIG__;
        if (!config || !config.isConfigured || !config.key || !config.securityJsCode) {
            return;
        }

        try {
            await loadAmapScript(config);

            hostState.map = new AMap.Map("map", {
                center: config.center || [120.585316, 30.028105],
                mapStyle: config.mapStyle || "amap://styles/darkblue",
                pitch: 0,
                resizeEnable: true,
                rotateEnable: false,
                showBuildingBlock: false,
                viewMode: "2D",
                zoom: config.zoom || 11,
                zooms: [3, 20]
            });

            hostState.map.addControl(new AMap.Scale());
            hostState.map.addControl(new AMap.ToolBar({
                position: {
                    right: "18px",
                    top: "18px"
                }
            }));

            hostState.map.on("click", function (event) {
                bridge("map-click", {
                    longitude: Number(event.lnglat.getLng().toFixed(6)),
                    latitude: Number(event.lnglat.getLat().toFixed(6))
                });
            });

            hostState.ready = true;
            bridge("host-ready", {});
        } catch (_error) {
            bridge("map-init-failed", {});
        }
    }

    window.TyslAmapHost = {
        applyStateFromJson: function (json) {
            if (!hostState.ready || !json) {
                return;
            }

            try {
                const state = JSON.parse(json);
                renderState(state);
            } catch (_error) {
                bridge("map-init-failed", {});
            }
        }
    };

    initialize();
})();
