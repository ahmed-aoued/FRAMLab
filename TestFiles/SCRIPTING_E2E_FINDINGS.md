# Scripting feature — app-driven test loop (iteration 2)

This is the second pass over the ST scripting feature (M3). The first pass
([`SCRIPTING_TEST_FINDINGS.md`](./SCRIPTING_TEST_FINDINGS.md)) drove
`ScriptingService` from a standalone program that *mimicked* the FMI pipeline and
therefore missed model-loading problems. This pass tests the feature **the way a
user does — in the running app — and reads the result from the app's own logs.**

## How it was tested

1. `dotnet run --project FMV_OG.csproj --launch-profile FMV_OG` → <http://localhost:5137>.
2. Load the model through the app's **Open** control.
3. Open the **Script (ST)** tab, edit the script, declare a `Static` variable,
   read the live diagnostics, press **Save**.
4. Run the **FMI** (`FMI zurücksetzen` → `Advance one cycle` × N).

Evidence is taken only from what the app produces:

* the **FMI session log** (`Metadata` → `Show FMI LOG`), where the scripting
  feature writes runtime issues and `Print`/`Trace` output;
* the **structured .NET log** — `await window.FRAMLab.logs()` (or
  `FRAMLab.dumpLogs()`), the same records the app writes through
  `LogEvents` (`Program`, `Pages.Index`, `ScriptIde`, `FmiScript`, …);
* `window.FRAMLab.jsErrors()` for unhandled browser errors.

Example model: [`FMV_Scripting_Example.xfmv`](./FMV_Scripting_Example.xfmv) —
`Detect fire` → `Assess severity` → `Alarm occupants` / `Dispatch crew` →
`Report outcome`. Every function runs a per-cycle ST script; there are no
metadata equations.

## Findings (each one surfaced by the app's own log)

### 1. `NullReferenceException` logged on every model open

* **Log:** `await window.FRAMLab.logs()` →
  ```
  WRN FMV_OG.Pages.Index - Could not persist the session backup to localStorage (file 'FMV_new.xfmv').
  System.NullReferenceException: Object reference not set to an instance of an object.
     at FMV_OG.Pages.Index.OnAfterRenderAsync(Boolean firstRender) in …/Pages/Index.razor:line 262
  ```
* **Cause:** the non-first-render branch of `OnAfterRenderAsync` dereferenced
  `projectData_Undo[0]` before any model was loaded.
* **Fix:** [`Index.razor`](../Pages/Index.razor:259) only persists the backup once
  `projectData_Undo` and `projectData_Undo[0]` exist.

### 2. The FMI ran nothing at all

* **Log (FMI session log):**
  ```
  Exit function <Assess severity>
  Exit function <Alarm occupants>
  Exit function <Dispatch crew>
  … BEGIN CYCLE 1
  No functions were activated during this cycle.
  ```
  `Detect fire` was never classified (no `Entry function <Detect fire>`), so no
  cycle ever started.
* **Cause:** the model carries `<Aspects … WebEnhanced="updated">`, and
  `FileIsLoaded()` only built `aspectsList`/`outputsList`/`couplingList` when
  `WebEnhanced != "updated"`. For an `"updated"` model those lists stayed empty,
  so no function could be classified as Entry/Exit/Passthrough.
* **Fix:** [`Index.razor`](../Pages/Index.razor:722) rebuilds the lists on every
  load. The geometry `ResetAspect` builds is idempotent, so this is safe.

### 3. Orphan terminal outputs aborted FMI initialisation

* **Log (FMI session log):**
  ```
  WARNING: <Alarm occupants> has an orphan
  WARNING: <Dispatch crew> has an orphan
  ```
  followed by an empty log — nothing ran.
* **Cause:** the FMI init loop treats `orphans > 0` as fatal. `Alarm status` and
  `Dispatch order` were terminal outputs with no consumer.
* **Fix:** the example now ends in `Report outcome`, which consumes both outputs,
  so the model has no orphan aspects.

### 4. `Print` / `Trace` rejected more than one argument

* **Log:** the Script IDE diagnostics panel showed
  `'Print' expects 1 argument(s), got 2.` for `Print('detector fired, hits=', H)`
  — a compile error that also stops the function's script from running.
* **Cause:** `SemanticAnalyzer` and `STInterpreter` hard-coded a single argument.
* **Fix:** [`SemanticAnalyzer.cs`](../Shared/MainApp/Services/Scripting/SemanticAnalyzer.cs)
  and [`STInterpreter.cs`](../Shared/MainApp/Services/Scripting/STInterpreter.cs)
  now treat `Print`/`Trace` as variadic (arguments concatenated in order).

## Verification (post-fix, from the app's logs)

After the fixes and a fresh load + 5 FMI cycles:

* **Structured .NET log** — one entry, no warnings/errors:
  ```
  INF FMV_OG.Program - FRAMLab started. Environment=Development BaseAddress=http://localhost:5137/
  ```
  `window.FRAMLab.jsErrors()` → empty.
* **FMI session log** — the chain runs in order and the scripts' own output appears:
  ```
  Entry function <Detect fire>
  [fn 0 cycle 0] detect cycle=0 hits=1
  …
  BEGIN CYCLE 1  Function <Assess severity> has been activated
  BEGIN CYCLE 2  Function <Alarm occupants> … Function <Dispatch crew> …
  BEGIN CYCLE 3  [fn 4 cycle 3] Reported alarms=1 effort=1
  BEGIN CYCLE 4  No functions were activated during this cycle.
  ```
  `detect cycle=0 hits=1` and `Reported alarms=1 effort=1` prove: the user's
  edited script ran, the `Static` variable persisted, the multi-argument `Print`
  works, and scripted outputs (`Fire signal` → `Severity assessment` →
  `Alarm status`/`Dispatch order`) propagated through real couplings.

## Model-authoring contract (confirmed)

For a scripted output to reach coupled functions, the producing function declares
a metadata key equal to the sanitized aspect identifier (Output `Severity assessment`
↔ `<Severity_assessment>`); `ScriptingService.WriteBackOutputs` writes the value
into that metadata entry and the coupling pipeline copies it to the target as
`"{toType}{outputFn}_{key}"`. Also, every Output must be consumed (no orphans) or
the FMI will not initialise.
