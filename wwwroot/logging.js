// FRAMLab global error logging.
// Unhandled browser errors would otherwise only surface as the generic
// "An unhandled error has occurred" banner with no detail. These handlers make
// them visible in the devtools console (search for the "[FRAMLab]" prefix) and
// buffer them so they can be exported together with the .NET logs.
(function () {
    "use strict";

    var PREFIX = "[FRAMLab]";
    var MAX = 200;
    var jsErrors = [];

    function remember(kind, detail) {
        jsErrors.push({ time: new Date().toISOString(), kind: kind, detail: String(detail) });
        if (jsErrors.length > MAX) {
            jsErrors.shift();
        }
    }

    window.addEventListener("error", function (event) {
        var detail;
        if (event.error) {
            detail = event.error.stack || event.error.message || event.error;
        } else {
            detail = event.message || "Unknown error";
        }
        var where = event.filename
            ? " @ " + event.filename + ":" + event.lineno + ":" + event.colno
            : "";
        console.error(PREFIX + " unhandled error" + where + ": " + detail);
        remember("error", detail + where);
    });

    window.addEventListener("unhandledrejection", function (event) {
        var reason = event.reason;
        var detail = reason ? (reason.stack || reason.message || reason) : "Unknown rejection";
        console.error(PREFIX + " unhandled promise rejection: " + detail);
        remember("unhandledrejection", detail);
    });

    // Console helpers, e.g.:
    //   await FRAMLab.logs()       -> return the .NET log text
    //   await FRAMLab.dumpLogs()   -> download the .NET log text as a file
    //   FRAMLab.jsErrors()         -> array of browser-side errors
    window.FRAMLab = {
        jsErrors: function () {
            return jsErrors.slice();
        },

        logs: async function () {
            try {
                return await DotNet.invokeMethodAsync("FMV_OG", "DumpLogs");
            } catch (e) {
                console.warn(PREFIX + " could not read the .NET log store: " + e);
                return "";
            }
        },

        dumpLogs: async function () {
            var text = await window.FRAMLab.logs();
            var body = text || "(no .NET log entries)";
            if (jsErrors.length) {
                body += "\n\n# Browser (JS) errors\n";
                jsErrors.forEach(function (e) {
                    body += e.time + " " + e.kind + " " + e.detail + "\n";
                });
            }
            if (typeof FileSaveAs === "function") {
                FileSaveAs("framlab-log.txt", body);
            } else {
                console.log(body);
            }
        }
    };

    console.debug(PREFIX + " global error handlers installed. window.FRAMLab.dumpLogs() exports the recent log.");
})();
