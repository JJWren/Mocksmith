using System.Text.RegularExpressions;

namespace Mocksmith.Core.Generation;

/// <summary>
/// Injects the workspace bridge script into sample HTML at serve time (never persisted).
/// The bridge runs inside the sandboxed iframe (allow-scripts, no allow-same-origin) and
/// speaks to the workspace exclusively over postMessage: hover/click element picking,
/// computed-style reporting, token value reads, and live patch application.
/// </summary>
public static partial class HtmlBridgeInjector
{
    [GeneratedRegex("""<script\s+id\s*=\s*["']mocksmith-bridge["'][\s\S]*?</script>""", RegexOptions.IgnoreCase)]
    private static partial Regex ExistingBridgeRegex();

    public const string BridgeScript =
        """
        <script id="mocksmith-bridge">
        (function () {
            "use strict";
            var HOVER_ATTR = "data-ms-hover";
            var SELECT_ATTR = "data-ms-selected";
            var hoverEl = null;
            var selectedEl = null;

            var chrome = document.createElement("style");
            chrome.id = "mocksmith-bridge-chrome";
            chrome.textContent =
                "[" + HOVER_ATTR + "] { outline: 1px dashed rgba(232,118,58,.85) !important; outline-offset: 2px; cursor: crosshair !important; }" +
                "[" + SELECT_ATTR + "] { outline: 2px solid #e8763a !important; outline-offset: 2px; }";
            document.documentElement.appendChild(chrome);

            function selectorFor(el) {
                if (!el || el === document.body || el === document.documentElement) {
                    return "body";
                }
                var cls = Array.prototype.find.call(el.classList, function (c) {
                    return c && c.indexOf("ms-") !== 0;
                });
                return cls ? "." + (window.CSS && CSS.escape ? CSS.escape(cls) : cls) : el.tagName.toLowerCase();
            }

            function computedOf(el) {
                var cs = getComputedStyle(el);
                return {
                    fontFamily: cs.fontFamily,
                    fontSize: cs.fontSize,
                    fontWeight: cs.fontWeight,
                    color: cs.color,
                    backgroundColor: cs.backgroundColor,
                    letterSpacing: cs.letterSpacing,
                    lineHeight: cs.lineHeight
                };
            }

            function collectTokens() {
                var tokens = {};
                for (var s = 0; s < document.styleSheets.length; s++) {
                    var rules;
                    try { rules = document.styleSheets[s].cssRules; } catch (err) { continue; }
                    if (!rules) { continue; }
                    for (var r = 0; r < rules.length; r++) {
                        var rule = rules[r];
                        if (rule.selectorText === ":root" && rule.style) {
                            for (var p = 0; p < rule.style.length; p++) {
                                var prop = rule.style[p];
                                if (prop.indexOf("--") === 0) {
                                    tokens[prop] = rule.style.getPropertyValue(prop).trim();
                                }
                            }
                        }
                    }
                }
                return tokens;
            }

            document.addEventListener("mouseover", function (e) {
                if (hoverEl) { hoverEl.removeAttribute(HOVER_ATTR); }
                hoverEl = e.target;
                if (hoverEl && hoverEl !== document.body) { hoverEl.setAttribute(HOVER_ATTR, ""); }
            }, true);

            document.addEventListener("click", function (e) {
                e.preventDefault();
                e.stopPropagation();
                if (selectedEl) { selectedEl.removeAttribute(SELECT_ATTR); }
                selectedEl = e.target;
                selectedEl.setAttribute(SELECT_ATTR, "");
                parent.postMessage({
                    type: "ms-selected",
                    selector: selectorFor(selectedEl),
                    tag: selectedEl.tagName.toLowerCase(),
                    computed: computedOf(selectedEl)
                }, "*");
            }, true);

            window.addEventListener("message", function (e) {
                if (e.source !== parent) {
                    return; // only the hosting workspace may drive the bridge
                }
                var data = e.data || {};
                if (data.type === "ms-apply-patch") {
                    var style = document.getElementById("mocksmith-live-patch");
                    if (!style) {
                        style = document.createElement("style");
                        style.id = "mocksmith-live-patch";
                        document.documentElement.appendChild(style);
                    }
                    style.textContent = typeof data.css === "string" ? data.css : "";
                } else if (data.type === "ms-get-tokens" && Array.isArray(data.names)) {
                    var rootStyle = getComputedStyle(document.documentElement);
                    var values = {};
                    data.names.forEach(function (name) {
                        if (typeof name === "string" && name.indexOf("--") === 0) {
                            values[name] = rootStyle.getPropertyValue(name).trim();
                        }
                    });
                    parent.postMessage({ type: "ms-tokens", values: values }, "*");
                } else if (data.type === "ms-clear-selection") {
                    if (selectedEl) { selectedEl.removeAttribute(SELECT_ATTR); selectedEl = null; }
                }
            });

            parent.postMessage({ type: "ms-ready", tokens: collectTokens() }, "*");
        })();
        </script>
        """;

    /// <summary>Idempotently injects the bridge before &lt;/body&gt; (fallback: append).</summary>
    public static string Inject(string html)
    {
        var stripped = ExistingBridgeRegex().Replace(html, "");
        var bodyClose = stripped.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyClose >= 0
            ? stripped.Insert(bodyClose, BridgeScript + "\n")
            : stripped + "\n" + BridgeScript;
    }
}
