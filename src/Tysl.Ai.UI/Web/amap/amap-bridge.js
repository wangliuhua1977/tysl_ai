(function () {
    function postMessage(type, payload) {
        if (!window.chrome || !window.chrome.webview) {
            return;
        }

        window.chrome.webview.postMessage({
            type: type,
            payload: payload || {}
        });
    }

    window.TyslAmapBridge = {
        postMessage: postMessage
    };
})();
