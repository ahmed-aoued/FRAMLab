// Monaco-based ST (IEC 61131-3 Structured Text) editor interop for the FRAMLab Script IDE.
// Loads Monaco from CDN lazily on first use and registers a custom "framst" language
// (Monarch tokenizer) so highlighting can grow with the language.
(function () {
    // Monaco is bundled locally under wwwroot/lib/monaco (see plan: offline/PWA support).
    const MONACO_BASE = "lib/monaco";
    let loadPromise = null;
    let stRegistered = false;
    const editors = new Map();

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
                { open: "(*", close: "*)" }
            ]
        });

        m.languages.setMonarchTokensProvider("framst", {
            ignoreCase: true,
            defaultToken: "",
            keywords: [
                "IF", "THEN", "ELSIF", "ELSE", "END_IF",
                "FOR", "TO", "BY", "DO", "END_FOR",
                "WHILE", "END_WHILE",
                "REPEAT", "UNTIL", "END_REPEAT",
                "CASE", "OF", "END_CASE",
                "VAR", "VAR_INPUT", "VAR_OUTPUT", "VAR_GLOBAL", "END_VAR",
                "FUNCTION", "END_FUNCTION", "RETURN", "EXIT",
                "AND", "OR", "XOR", "NOT", "MOD",
                "TRUE", "FALSE"
            ],
            typeKeywords: ["BOOL", "INT", "DINT", "REAL", "LREAL", "STRING", "TIME", "DATE", "ARRAY"],
            hostFunctions: [
                "GetInput", "GetPrecondition", "GetResource", "GetControl", "GetTime",
                "SetOutput", "Cycle", "IsActive", "Print", "Trace",
                "ABS", "SQRT", "SIN", "COS", "TAN", "LN", "EXP", "MIN", "MAX", "LIMIT"
            ],
            tokenizer: {
                root: [
                    [/\(\*/, { token: "comment", next: "@comment" }],
                    [/\/\/.*$/, "comment"],
                    [/T#[\d.]+(ms|s|m|h|d)?/i, "number.time"],
                    [/\d+\.\d+([eE][+-]?\d+)?/, "number.float"],
                    [/\d+/, "number"],
                    [/'([^'\\]|\\.)*'/, "string"],
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
            loader.onerror = () => reject(new Error("Failed to load Monaco loader from CDN."));
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

            const sub = editor.onDidChangeModelContent(_.debounce(() => {
                dotNetRef.invokeMethodAsync("OnEditorChanged", editor.getValue());
            }, 300));

            editors.set(elementId, { editor: editor, subscription: sub });
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
        syncChannel = null;
    }

    window.ScriptSync = {
        // Called by the MAIN tab before opening the IDE tab: stash the model the IDE should load.
        prepare: function (xml, fnId, fileName) {
            try {
                localStorage.setItem("scriptIDE_data", xml);
                localStorage.setItem("scriptIDE_fnId", fnId);
                localStorage.setItem("scriptIDE_fileName", fileName || "");
            } catch (e) { }
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
                return { xml: "", fnId: "-1", fileName: "" };
            }
        },
        // Called by the IDE tab on save/close: write back the merged model and notify the main tab.
        save: function (xml) {
            try { localStorage.setItem("scriptIDE_result", xml); } catch (e) { }
            if (syncChannel) {
                syncChannel.postMessage({ type: "script-saved" });
            } else {
                // Fallback for browsers without BroadcastChannel: use the storage event.
                try { localStorage.setItem("scriptIDE_ping", Date.now().toString()); } catch (e) { }
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
            try { return localStorage.getItem("scriptIDE_result") || ""; } catch (e) { return ""; }
        }
    };
})();
