// Clipboard-paste capture for the generate page: image blobs become base64
// payloads delivered to Blazor via the registered .NET reference. init/dispose
// are paired so navigation never accumulates duplicate document-level handlers.
window.mocksmithPaste = {
    _handler: null,

    init(dotNetRef, elementId) {
        this.dispose();
        const handler = async (event) => {
            if (!document.getElementById(elementId)) {
                return; // page content replaced without dispose (defensive)
            }
            const items = event.clipboardData ? Array.from(event.clipboardData.items) : [];
            for (const item of items) {
                if (!item.type || !item.type.startsWith("image/")) {
                    continue;
                }
                event.preventDefault();
                const file = item.getAsFile();
                if (!file || file.size > 5 * 1024 * 1024) {
                    continue;
                }
                const buffer = await file.arrayBuffer();
                const bytes = new Uint8Array(buffer);
                let binary = "";
                const chunk = 0x8000;
                for (let i = 0; i < bytes.length; i += chunk) {
                    binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
                }
                await dotNetRef.invokeMethodAsync(
                    "OnPasteImage", btoa(binary), file.type, file.name || "pasted.png");
            }
        };
        this._handler = handler;
        document.addEventListener("paste", handler);
    },

    dispose() {
        if (this._handler) {
            document.removeEventListener("paste", this._handler);
            this._handler = null;
        }
    },
};
