(function () {
    const DEFAULT_MAP_STYLE = "default";
    const DIRECT_TYPES = new Set(["", "gcj02", "amap", "gaode", "unknown"]);
    const hostState = {
        config: window.__TYSL_AMAP_CONFIG__ || {},
        currentMapStyle: DEFAULT_MAP_STYLE,
        map: null,
        markers: new Map(),
        points: [],
        ready: false,
        renderedState: null,
        selectedDeviceCode: null
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

    function truncateText(value, maxLength) {
        const text = String(value || "").trim();
        if (!text) {
            return "";
        }

        return text.length > maxLength
            ? `${text.slice(0, Math.max(1, maxLength - 1))}…`
            : text;
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

    function resolvePointName(point) {
        const name = point.alias || point.displayName || point.deviceName || point.deviceCode;
        return truncateText(name, 12);
    }

    function resolveMapStyle(mapStyleKey) {
        const normalized = String(mapStyleKey || "").trim();
        if (!normalized) {
            return null;
        }

        switch (normalized.toLowerCase()) {
            case DEFAULT_MAP_STYLE:
            case "native":
            case "normal":
                return null;
            default:
                return normalized;
        }
    }

    function buildMarkerContent(point, selected) {
        const selectedClass = selected ? " is-selected" : "";
        const dispatchStateKey = escapeHtml(point.dispatchStateKey || "none");
        const visualState = escapeHtml(point.visualState || "normal");
        const label = escapeHtml(resolvePointName(point));

        return [
            `<div class="marker-node marker-${visualState}${selectedClass}">`,
            "  <span class=\"marker-icon\">",
            "    <span class=\"marker-icon__core\"></span>",
            `    <span class="marker-state marker-state--${dispatchStateKey}"></span>`,
            "  </span>",
            `  <span class="marker-label" title="${label}">${label}</span>`,
            "</div>"
        ].join("");
    }

    function updateMapCursor(coordinatePickActive) {
        const shell = document.getElementById("map-shell");
        if (!shell) {
            return;
        }

        shell.classList.toggle("is-picking", Boolean(coordinatePickActive));
    }

    function updateMarkerSelection() {
        hostState.markers.forEach((entry, deviceCode) => {
            const selected = deviceCode === hostState.selectedDeviceCode;
            entry.marker.setContent(buildMarkerContent(entry.point, selected));
            entry.marker.setzIndex(selected ? 160 : 120);
        });
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

    async function renderState(state) {
        if (!hostState.map) {
            return;
        }

        hostState.renderedState = state || {
            points: [],
            coordinatePickActive: false,
            selectedDeviceCode: null
        };
        hostState.points = hostState.renderedState.points || [];
        hostState.selectedDeviceCode = hostState.renderedState.selectedDeviceCode || null;
        updateMapCursor(hostState.renderedState.coordinatePickActive);

        const resolvedPoints = await normalizePoints(hostState.points);
        clearMarkers();

        const renderedPoints = [];
        const markers = [];

        resolvedPoints.forEach((item) => {
            const point = item.point;
            const position = new AMap.LngLat(item.longitude, item.latitude);
            const selected = point.deviceCode === hostState.selectedDeviceCode;
            const marker = new AMap.Marker({
                anchor: "bottom-center",
                content: buildMarkerContent(point, selected),
                offset: new AMap.Pixel(0, 0),
                position: position,
                zIndex: selected ? 160 : 120
            });

            marker.on("click", function () {
                hostState.selectedDeviceCode = point.deviceCode;
                hostState.renderedState = {
                    ...(hostState.renderedState || {}),
                    selectedDeviceCode: point.deviceCode
                };
                updateMarkerSelection();
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
            hostState.map.setFitView(markers, false, [56, 56, 56, 56], 16);
        }

        updateMarkerSelection();
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

    function getCurrentViewport() {
        if (!hostState.map) {
            return null;
        }

        const center = hostState.map.getCenter();
        return {
            center: [Number(center.getLng().toFixed(6)), Number(center.getLat().toFixed(6))],
            zoom: Number(hostState.map.getZoom().toFixed(2))
        };
    }

    function buildMapOptions(viewport) {
        const mapStyle = resolveMapStyle(hostState.currentMapStyle);
        const options = {
            center: viewport?.center || hostState.config.center || [120.585316, 30.028105],
            pitch: 0,
            resizeEnable: true,
            rotateEnable: false,
            showBuildingBlock: false,
            viewMode: "2D",
            zoom: viewport?.zoom || hostState.config.zoom || 11,
            zooms: [3, 20]
        };

        if (mapStyle) {
            options.mapStyle = mapStyle;
        }

        return options;
    }

    function destroyMap() {
        if (!hostState.map) {
            return;
        }

        clearMarkers();
        hostState.map.destroy();
        hostState.map = null;
    }

    function createMap(viewport) {
        hostState.map = new AMap.Map("map", buildMapOptions(viewport));
        hostState.map.addControl(new AMap.Scale());
        hostState.map.addControl(new AMap.ToolBar({
            position: {
                right: "18px",
                top: "60px"
            }
        }));

        hostState.map.on("click", function (event) {
            bridge("map-click", {
                longitude: Number(event.lnglat.getLng().toFixed(6)),
                latitude: Number(event.lnglat.getLat().toFixed(6))
            });
        });
    }

    function applyMapStyle(mapStyleKey) {
        hostState.currentMapStyle = String(mapStyleKey || DEFAULT_MAP_STYLE).trim() || DEFAULT_MAP_STYLE;

        if (!window.AMap) {
            return;
        }

        const viewport = getCurrentViewport();
        destroyMap();
        createMap(viewport);

        if (hostState.renderedState) {
            void renderState(hostState.renderedState);
        }
    }

    async function initialize() {
        const config = window.__TYSL_AMAP_CONFIG__;
        if (!config || !config.isConfigured || !config.key || !config.securityJsCode) {
            return;
        }

        hostState.config = config;
        hostState.currentMapStyle = String(config.mapStyle || DEFAULT_MAP_STYLE).trim() || DEFAULT_MAP_STYLE;

        try {
            await loadAmapScript(config);
            applyMapStyle(hostState.currentMapStyle);
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
                void renderState(state);
            } catch (_error) {
                bridge("map-init-failed", {});
            }
        },
        applyMapStyle: function (mapStyleKey) {
            try {
                applyMapStyle(mapStyleKey);
            } catch (_error) {
                bridge("map-init-failed", {});
            }
        }
    };

    initialize();
})();
