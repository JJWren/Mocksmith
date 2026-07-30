// Workspace-side half of the edit-panel bridge: relays messages from the sandboxed
// preview iframe to Blazor and pushes live patch CSS back in. init/dispose are paired.
window.mocksmithWorkspace = {
    _handler: null,

    init(dotNetRef, iframeId) {
        this.dispose();
        const handler = (event) => {
            const frame = document.getElementById(iframeId);
            // The preview iframe is sandboxed without allow-same-origin, so legitimate
            // bridge messages always arrive from its window with the opaque "null" origin.
            if (!frame || event.source !== frame.contentWindow || event.origin !== "null") {
                return;
            }
            const data = event.data || {};
            if (data.type === "ms-ready" || data.type === "ms-selected" || data.type === "ms-tokens") {
                dotNetRef.invokeMethodAsync("OnBridgeMessage", JSON.stringify(data));
            }
        };
        this._handler = handler;
        window.addEventListener("message", handler);
    },

    sendPatch(iframeId, css) {
        const frame = document.getElementById(iframeId);
        if (frame && frame.contentWindow) {
            frame.contentWindow.postMessage({ type: "ms-apply-patch", css: css }, "*");
        }
    },

    dispose() {
        if (this._handler) {
            window.removeEventListener("message", this._handler);
            this._handler = null;
        }
    },
};
