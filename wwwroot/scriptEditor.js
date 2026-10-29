// Monaco-based ST (IEC 61131-3 Structured Text) editor interop for the FRAMLab Script IDE.
// Loads Monaco from CDN lazily on first use and registers a custom "framst" language
// (Monarch tokenizer) so highlighting can grow with the language.
(function () {
    // Monaco is bundled locally under wwwroot/lib/monaco (see plan: offline/PWA support).
    const MONACO_BASE = "lib/monaco";
    let loadPromise = null;
    let stRegistered = false;
    const editors = new Map();

    // Small local debounce so the editor does not depend on lodash from a CDN.
    function debounce(fn, ms) {
        let timer = null;
        return function () {
            const args = arguments;
            clearTimeout(timer);
            timer = setTimeout(() => fn.apply(null, args), ms);
        };
    }

    function completionKind(m, kind) {
        const k = m.languages.CompletionItemKind;
        switch (kind) {
            case "variable": return k.Variable;
            case "aspect": return k.Field;
            case "function": return k.Function;
            case "keyword": return k.Keyword;
            case "snippet": return k.Snippet;
            default: return k.Text;
        }
    }

    function registerSTLanguage() {
        if (stRegistered || !window.monaco) return;
        const m = window.monaco;

        m.languages.register({ id: "framst" });

        m.languages.setLanguageConfiguration("framst", {
            comments: { blockComment: ["(*", "*)"] },
            brackets: [["(", ")"], ["[", "]"]],
            autoClosingPairs: [
                { open: "(", close: ")" },
                { open: "[", close: "]" },
                { open: "'", close: "'", notIn: ["string", "comment"] },
                { open: "\"", close: "\"", notIn: ["string", "comment"] },
                { open: "(*", close: "*)" }
            ]
        });

        m.languages.setMonarchTokensProvider("framst", {
            ignoreCase: true,
            defaultToken: "",
            // Only what STParser/STInterpreter actually support (variables are declared in the
            // IDE tables, so there are no VAR sections).
            keywords: [
                "IF", "THEN", "ELSIF", "ELSE", "END_IF",
                "FOR", "TO", "BY", "DO", "END_FOR",
                "WHILE", "END_WHILE",
                "REPEAT", "UNTIL", "END_REPEAT",
                "CASE", "OF", "END_CASE",
                "RETURN", "EXIT",
                "AND", "OR", "XOR", "NOT", "MOD",
                "TRUE", "FALSE"
            ],
            typeKeywords: ["BOOL", "INT", "REAL", "STRING", "TIME"],
            hostFunctions: [
                "GetInput", "GetPrecondition", "GetResource", "GetControl", "GetTime",
                "SetOutput", "Cycle", "IsActive", "Print", "Trace", "API",
                "ABS", "SQRT", "SIN", "COS", "TAN", "LN", "EXP", "MIN", "MAX", "LIMIT",
                "ROUND", "TRUNC"
            ],
            tokenizer: {
                root: [
                    [/\(\*/, { token: "comment", next: "@comment" }],
                    [/\/\/.*$/, "comment"],
                    [/T(IME)?#-?[\d.]+(ms|s|m|h|d)?/i, "number.time"],
                    [/\d+\.\d+([eE][+-]?\d+)?/, "number.float"],
                    [/\d+/, "number"],
                    [/'([^'\\]|\\.)*'/, "string"],
                    [/"([^"\\]|\\.)*"/, "string"],
                    [/:=|\.\.|[+\-*/=<>]+/, "operator"],
                    [/[a-zA-Z_][\w]*/, {
                        cases: {
                            "@keywords": "keyword",
                            "@typeKeywords": "type",
                            "@hostFunctions": "predefined",
                            "@default": "identifier"
                        }
                    }],
                    [/[;,.]/, "delimiter"],
                    [/[[\]()]/, "@brackets"],
                    [/\s+/, "white"]
                ],
                comment: [
                    [/\*\)/, "comment", "@pop"],
                    [/./, "comment"]
                ]
            }
        });

        // Autocomplete: suggestions come from .NET (ScriptCompletions) per editor via setCompletions.
        m.languages.registerCompletionItemProvider("framst", {
            provideCompletionItems: function (model, position) {
                let items = [];
                editors.forEach(entry => { if (entry.editor.getModel() === model) items = entry.completions || []; });
                const word = model.getWordUntilPosition(position);
                const range = {
                    startLineNumber: position.lineNumber, endLineNumber: position.lineNumber,
                    startColumn: word.startColumn, endColumn: word.endColumn
                };
                return {
                    suggestions: items.map(i => ({
                        label: i.label,
                        kind: completionKind(m, i.kind),
                        detail: i.detail,
                        documentation: i.documentation,
                        insertText: i.insertText,
                        insertTextRules: i.isSnippet ? m.languages.CompletionItemInsertTextRule.InsertAsSnippet : 0,
                        sortText: (i.kind === "variable" || i.kind === "aspect" ? "0" : i.kind === "function" ? "1" : i.kind === "snippet" ? "2" : "3") + i.label,
                        range: range
                    }))
                };
            }
        });

        stRegistered = true;
    }

    function loadMonaco() {
        if (window.monaco) {
            registerSTLanguage();
            return Promise.resolve();
        }
        if (loadPromise) return loadPromise;

        loadPromise = new Promise((resolve, reject) => {
            const loader = document.createElement("script");
            loader.src = MONACO_BASE + "/vs/loader.js";
            loader.onload = () => {
                window.require.config({ paths: { vs: MONACO_BASE + "/vs" } });
                window.require(["vs/editor/editor.main"], () => {
                    try {
                        registerSTLanguage();
                        resolve();
                    } catch (e) {
                        reject(e);
                    }
                });
            };
            loader.onerror = () => {
                const err = new Error("Failed to load the Monaco loader from " + loader.src);
                console.error("[FRAMLab] " + err.message);
                reject(err);
            };
            document.head.appendChild(loader);
        });
        return loadPromise;
    }

    window.ScriptEditor = {
        create: async function (elementId, dotNetRef, initialValue, readOnly) {
            await loadMonaco();
            const el = document.getElementById(elementId);
            if (!el) return;

            const existing = editors.get(elementId);
            if (existing) {
                existing.editor.dispose();
                editors.delete(elementId);
            }

            const editor = window.monaco.editor.create(el, {
                value: initialValue || "",
                language: "framst",
                readOnly: !!readOnly,
                automaticLayout: true,
                minimap: { enabled: false },
                fontSize: 13,
                scrollBeyondLastLine: false,
                tabSize: 4,
                insertSpaces: true,
                renderWhitespace: "none",
                fixedOverflowWidgets: true
            });

            const sub = editor.onDidChangeModelContent(debounce(() => {
                dotNetRef.invokeMethodAsync("OnEditorChanged", editor.getValue());
            }, 300));

            editors.set(elementId, { editor: editor, subscription: sub, completions: [] });
        },

        /// Replace the autocomplete suggestions for one editor (built in .NET by ScriptCompletions).
        setCompletions: function (elementId, items) {
            const entry = editors.get(elementId);
            if (entry) entry.completions = items || [];
        },

        setValue: function (elementId, value) {
            const entry = editors.get(elementId);
            if (entry && entry.editor.getValue() !== value) {
                entry.editor.setValue(value || "");
            }
        },

        getValue: function (elementId) {
            const entry = editors.get(elementId);
            return entry ? entry.editor.getValue() : "";
        },

        setMarkers: function (elementId, markers) {
            const entry = editors.get(elementId);
            if (!entry || !window.monaco) return;
            const model = entry.editor.getModel();
            window.monaco.editor.setModelMarkers(model, "framst", (markers || []).map(m => ({
                severity: m.severity === "Error" ? 8 : m.severity === "Warning" ? 4 : 2,
                message: m.message,
                startLineNumber: m.startLine,
                startColumn: m.startColumn,
                endLineNumber: m.endLine,
                endColumn: m.endColumn
            })));
        },

        revealLine: function (elementId, line) {
            const entry = editors.get(elementId);
            if (!entry) return;
            entry.editor.revealLineInCenter(line);
            entry.editor.setPosition({ lineNumber: line, column: 1 });
            entry.editor.focus();
        },

        dispose: function (elementId) {
            const entry = editors.get(elementId);
            if (entry) {
                entry.subscription.dispose();
                entry.editor.dispose();
                editors.delete(elementId);
            }
        }
    };

    // --- Cross-tab sync between the main editor tab and the Script IDE tab ---
    const SYNC_CHANNEL = "fram-script-sync";
    let syncChannel = null;
    try {
        syncChannel = ("BroadcastChannel" in window) ? new BroadcastChannel(SYNC_CHANNEL) : null;
    } catch (e) {
        console.warn("[FRAMLab] BroadcastChannel unavailable; falling back to storage events for cross-tab sync.", e);
        syncChannel = null;
    }

    window.ScriptSync = {
        // Called by the MAIN tab when the Script (ST) button is pressed. Tries to open
        // the IDE in a new tab and reports whether that worked, so the caller can fall
        // back to navigating in place when the environment blocks popups.
        open: function (url) {
            try {
                // Editor-embedded browsers (VS Code Simple Browser, Electron webviews)
                // hand back a window proxy for window.open but never display the tab,
                // so report failure and let the caller navigate in place.
                const ua = navigator.userAgent || "";
                if (/\bElectron\//.test(ua) || /\bCode\//.test(ua)) {
                    return false;
                }
                const win = window.open(url, "_blank");
                if (!win) {
                    return false;
                }
                try { win.opener = null; } catch (e) { /* cross-origin safety */ }
                return true;
            } catch (e) {
                console.error("[FRAMLab] Could not open the Script IDE tab.", e);
                return false;
            }
        },
        // Called by the IDE page when it was not opened as its own tab (in-place
        // fallback), where window.close() is ignored: return to the main editor.
        closeOrGoHome: function (homeUrl) {
            try { window.close(); } catch (e) { /* ignored */ }
            window.setTimeout(function () {
                if (!window.closed) {
                    // Back first so the browser can restore the editor tab from
                    // its back/forward cache; fall back to the app root.
                    if (window.history.length > 1) {
                        window.history.back();
                    } else {
                        window.location.href = homeUrl || "/";
                    }
                }
            }, 150);
        },
        // Called by the MAIN tab before opening the IDE tab: stash the model the IDE should load.
        prepare: function (xml, fnId, fileName) {
            try {
                localStorage.setItem("scriptIDE_data", xml);
                localStorage.setItem("scriptIDE_fnId", fnId);
                localStorage.setItem("scriptIDE_fileName", fileName || "");
            } catch (e) {
                console.error("[FRAMLab] Could not stash the model for the Script IDE tab (localStorage).", e);
            }
        },
        // Called by the IDE tab to read the stashed model.
        take: function () {
            try {
                return {
                    xml: localStorage.getItem("scriptIDE_data") || "",
                    fnId: localStorage.getItem("scriptIDE_fnId") || "-1",
                    fileName: localStorage.getItem("scriptIDE_fileName") || ""
                };
            } catch (e) {
                console.error("[FRAMLab] Could not read the stashed model for the Script IDE tab (localStorage).", e);
                return { xml: "", fnId: "-1", fileName: "" };
            }
        },
        // Called by the IDE tab on save/close: write back the merged model and notify the main tab.
        save: function (xml) {
            try {
                localStorage.setItem("scriptIDE_result", xml);
            } catch (e) {
                console.error("[FRAMLab] Could not store the saved model for the main tab (localStorage).", e);
            }
            if (syncChannel) {
                syncChannel.postMessage({ type: "script-saved" });
            } else {
                // Fallback for browsers without BroadcastChannel: use the storage event.
                try {
                    localStorage.setItem("scriptIDE_ping", Date.now().toString());
                } catch (e) {
                    console.error("[FRAMLab] Could not notify the main tab of the save (localStorage).", e);
                }
            }
        },
        // Called by the MAIN tab: invokes dotNetCallback(fullMethodName) when the IDE tab saves.
        listen: function (dotNetRef, methodName) {
            const notify = () => dotNetRef.invokeMethodAsync(methodName);
            if (syncChannel) {
                syncChannel.onmessage = (ev) => {
                    if (ev.data && ev.data.type === "script-saved") notify();
                };
            } else {
                window.addEventListener("storage", (e) => {
                    if (e.key === "scriptIDE_ping") notify();
                });
            }
        },
        // Called by the MAIN tab to read the saved (merged) model after a save notification.
        result: function () {
            try {
                return localStorage.getItem("scriptIDE_result") || "";
            } catch (e) {
                console.error("[FRAMLab] Could not read the model saved by the Script IDE tab (localStorage).", e);
                return "";
            }
        }
    };
})();
