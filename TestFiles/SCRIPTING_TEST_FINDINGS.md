# Scripting feature — test findings and fixes

End-to-end testing of the ST scripting feature (M3) against a real FRAM model
found six integration/behaviour bugs that made aspect variables unusable. All
are fixed and covered by regression tests.

> **Iteration 2:** this pass drove a *mimic* of the FMI pipeline and so could not
> catch model-loading bugs that stopped the example from running in the app at
> all. See [`SCRIPTING_E2E_FINDINGS.md`](./SCRIPTING_E2E_FINDINGS.md) for the
> app-driven pass (tested through the running app and judged by its own logs) and
> the additional fixes.

## How it was tested

* Example model: [`FMV_Scripting_Example.xfmv`](./FMV_Scripting_Example.xfmv) —
  a four-function fire-response model (`Detect fire` → `Assess severity` →
  `Alarm occupants` / `Dispatch crew`). Every function runs a per-cycle ST
  script instead of a metadata equation.
* [`ScriptScenarioTests.cs`](./ScriptScenarioTests.cs) drives the production
  `ScriptingService` like the app does: it loads the model, derives the aspects,
  and runs three FMI cycles while mimicking the coupling propagation performed
  by `FMIControl.bFMI_cycle_Couplings`
  (`nextFn["{toType}{outputFn}_{metaKey}"] = outputFn.metadata[metaKey].value`).
* [`ScriptLanguageTests.cs`](./ScriptLanguageTests.cs) keeps the language-level
  suite and now also contains regression tests for the bugs below.

Both files are standalone top-level programs, excluded from the Blazor build.
They are **not** part of the app and are **not** how the feature is verified.
The feature is exercised *through the running app, like a user*, and judged by
the app's own logs (see [`SCRIPTING_E2E_FINDINGS.md`](./SCRIPTING_E2E_FINDINGS.md)).

## Bugs found and fixed

### 1. Input aspects never resolved (read as 0 every cycle)

* **Symptom:** a script reading an Input aspect (e.g. `Fire_signal`) always
  saw `0`, even though the coupling pipeline had populated the value.
* **Root cause:** `STInterpreter.TryGetAspect` matched function variables by the
  raw human label (`Hazard signal`), but the FMI pipeline names variables with an
  identifier key (`I0_Hazard_signal`). `"I0_Hazard_signal".EndsWith("_Hazard signal")`
  is never true, so the lookup always fell through to `ZeroOf(...)`.
  The old unit test only passed because it used the fictional variable name
  `I0_Hazard signal` (with a space), which the pipeline never produces.
* **Fix:** [`STInterpreter.cs`](../Shared/MainApp/Services/Scripting/STInterpreter.cs:237)
  matches on the sanitized `AspectVariable.Identifier` instead of the label
  (keeping the `Script_<label>` alias for backwards compatibility).

### 2. Output aspects never reached coupled functions

* **Symptom:** a script on `Detect fire` computed `Fire_signal = 1`, but
  `Assess severity` still received the producer's stale metadata value (`0`).
* **Root cause:** `ScriptingService.WriteBackOutputs` stored outputs under the
  key `Script_<label>` only. Nothing in the FMI pipeline reads `Script_*`
  variables: coupling activation copies `outputFn.metadata[key].value` and
  `calcVariables` reads `outputFn.getVariable(key)`. So script outputs were
  invisible to every downstream function.
* **Fix:** [`ScriptingService.cs`](../Shared/MainApp/Services/Scripting/ScriptingService.cs:139)
  now stores the value under the aspect's sanitized identifier (the key the rest
  of the app uses) and writes it back into the matching metadata entry, so
  coupling activation and `calcVariables` carry the scripted value. The
  human-readable `Script_<label>` alias is kept for the variable table.

### 3. Unconnected (orphan) aspects were missing

* **Symptom:** `Alarm occupants` and `Dispatch crew` could not assign their
  declared outputs (`Alarm status`, `Dispatch order`) — the semantic analyzer
  rejected them as "not declared", so their scripts never ran.
* **Root cause:** `AspectVariable.DeriveFor` derived the aspect set from
  couplings only. A function's output that has no outgoing coupling (a terminal
  function, or one whose aspect is simply not wired yet) produced no Output
  aspect, even though it is declared in the model's `<Outputs>` list.
* **Fix:** [`AspectVariable.cs`](../Shared/MainApp/Models/AspectVariable.cs:96)
  adds a `declared` parameter and a
  [`DeclaredFromXml`](../Shared/MainApp/Models/AspectVariable.cs:148) helper that
  reads the function's aspects from the model's
  Inputs/Preconditions/Resources/Controls/Times/Outputs lists. The declared set
  is unioned with the coupling-derived set. `StateContainer.SyncScriptAspects`
  and the Script IDE page now pass it through.

### 4. `Global` variables declared in the function table were broken

* **Symptom:** declaring a variable with scope `Global` in the Script IDE's
  variable table produced the runtime error `'X' is not declared.` on every
  assignment, and the value never persisted.
* **Root cause:** `ScriptingService.ExecuteCycle` initialised the shared global
  store from `GlobalVariableDeclarations` (the editor's Globals panel) but not
  from `fn.Script.Variables` entries with `Scope == Global`. The symbol table
  accepted them (so compilation succeeded), but the interpreter's `Set` only
  writes to names already present in the global store, so at runtime the
  variable was unknown.
* **Fix:** [`ScriptingService.cs`](../Shared/MainApp/Services/Scripting/ScriptingService.cs:103)
  also seeds function-declared Global variables into the shared store.

### 5. Host getters/setters rejected human aspect labels

* **Symptom:** `GetInput("Hazard signal")` / `SetOutput("Hazard signal", v)` returned 0
  / were not propagated, forcing the user to know the internal identifier.
* **Root cause:** `HostGet` matched the argument verbatim against the pipeline's
  variable names, and `WriteBackOutputs` mapped `SetOutput` keys only against the
  sanitized identifier.
* **Fix:** [`STInterpreter.cs`](../Shared/MainApp/Services/Scripting/STInterpreter.cs)
  sanitizes the argument before matching, and `WriteBackOutputs` matches an output
  key against the aspect identifier **or** the human label.

### 6. FOR control variable ignored its declared scope

* **Symptom:** a `FOR` loop over a `Static`/`Global` variable silently lost its
  persisted value — the loop variable was always written to the Temp store,
  shadowing the declared scope.
* **Root cause:** `STInterpreter.ExecuteFor` assigned `tempStore[f.Variable]`
  directly instead of resolving the owning store.
* **Fix:** [`STInterpreter.cs`](../Shared/MainApp/Services/Scripting/STInterpreter.cs)
  writes the control variable through a scope-aware helper (Static → Global → Temp).

## Convention for scripting an output aspect

The FMI pipeline carries a coupling's value by **metadata key**, so for a script
output to reach coupled functions, the producing function should declare a
metadata key matching the sanitized aspect identifier — e.g. output aspect
`Fire signal` ↔ metadata `<Fire_signal>`. The example model follows this
convention. This is now the documented contract for aspect wiring.

## Verification

* `ScriptScenarioTests.cs`: 15/15 assertions pass (including the full four-function
  chain across three cycles, host getters/setters and loop scoping).
* `ScriptLanguageTests.cs`: 52/52 assertions pass (42 original + 10 regression).
* `dotnet build FMV_OG.csproj`: succeeds with 0 warnings, 0 errors.
