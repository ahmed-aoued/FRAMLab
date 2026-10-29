using System.Globalization;

namespace FRAMLab.Shared.MainApp.Services
{
    /// <summary>
    /// What the FMI run loop needs from the application. StateContainer provides the real
    /// implementation (with UI concerns such as tracer colours); tests provide a plain one.
    /// </summary>
    public interface IFmiRunHost
    {
        List<Function> Functions { get; }
        List<Coupling> Couplings { get; }
        /// <summary>All I/P/R/C/T aspects (not Outputs).</summary>
        List<Aspect> Aspects { get; }
        List<Aspect> Outputs { get; }
        int Cycle { get; set; }

        void BeginCycle();
        void EndCycle();
        /// <summary>Evaluate one function for the current cycle: live data it reads on activation,
        /// metadata equations (and macros), then its ST script. Returns the variables (or api() reads)
        /// that have no value.</summary>
        Task<IReadOnlyList<FmiUnknownVariable>> EvaluateAsync(Function fn);
        /// <summary>After a function activated (e.g. capture live data it reads on exit).</summary>
        Task AfterActivateAsync(Function fn);
        /// <summary>Apply a FunctionType change requested by the "Variable" key (records undo).</summary>
        void SetFunctionType(Function fn, string functionType);

        /// <summary>Start a new run log (cycle 0).</summary>
        void StartLog();
        /// <summary>Append to the run log; <paramref name="fn"/> links the entry to a function.</summary>
        void Log(string text, Function? fn = null, FmiLogLevel level = FmiLogLevel.Info);
        /// <summary>Bring the run log to the user's attention (model check failed).</summary>
        void ShowLog();
    }

    /// <summary>
    /// The FMI cycle engine: model check and function classification (cycle 0), Entry timing,
    /// activation by profile, Activate/Hold/Consume/Variable keys, and coupling propagation.
    /// Extracted from FMIControl so the run logic can be tested without the UI.
    /// </summary>
    public sealed class FmiRunEngine
    {
        public const string ResultError = "error";
        public const string ResultEnd = "End";
        /// <summary>The cycle completed, but equations referenced unknown variables; the user must act.</summary>
        public const string ResultBlocked = "Blocked";

        private readonly IFmiRunHost host;
        private readonly Random rnd;
        private List<Coupling> activeAspectsArray = new();
        private List<Coupling> toActivateAspectsArray = new();
        private List<string> trackExitFunctions = new();
        private int trackHighlights;

        public FmiRunEngine(IFmiRunHost host, Random? random = null)
        {
            this.host = host;
            rnd = random ?? new Random();
        }

        /// <summary>Cycle limit: null = until no Exit function is pending, 0 = unlimited, n = stop at cycle n.</summary>
        public int? StepTotal { get; set; }

        /// <summary>Unknown variables found in the last cycle (distinct by function + variable).</summary>
        public List<FmiUnknownVariable> UnknownVariables { get; } = new();

        /// <summary>Forget the run's internal state (FMI Reset).</summary>
        public void Reset()
        {
            activeAspectsArray = new List<Coupling>();
            toActivateAspectsArray = new List<Coupling>();
            trackExitFunctions = new List<string>();
            trackHighlights = 0;
            UnknownVariables.Clear();
        }

        /// <summary>
        /// Run one FMI cycle. Returns <see cref="ResultError"/> when the cycle-0 model check fails,
        /// <see cref="ResultEnd"/> when the run is finished, <see cref="ResultBlocked"/> when unknown
        /// variables need the user's attention, otherwise the cycle number.
        /// </summary>
        public async Task<string> RunCycleAsync()
        {
            host.Cycle += 1;
            host.BeginCycle();
            UnknownVariables.Clear();
            var returnString = ResultError;
            var fmiError = false;
            trackHighlights = 0;
            toActivateAspectsArray = new List<Coupling>();
            foreach (var pCoupling in host.Couplings)
            {
                pCoupling.activated = false;
                if (host.Cycle == 0)
                {
                    pCoupling.wasActive = false;
                    pCoupling.fmiHighlight = "";
                    if (pCoupling.outputFn != pCoupling.toFn)
                    {
                        var getFn = host.Functions.Find(x => x.IDNr == pCoupling.toFn);
                        if (getFn is not null && pCoupling.toType is "I" or "P" or "R" or "C" or "T")
                        {
                            getFn.EnsureCoupling(pCoupling.toType, pCoupling.Name);
                        }
                    }
                }
            }
            if (host.Cycle == 0)
            {
                host.StartLog();
                activeAspectsArray = new List<Coupling>();
                trackExitFunctions = new List<string>();
                //Check for orphans, then for functions that have aspects but not an Input or Output
                //(they can't be Background functions if they have other aspects)
                foreach (var fnO in host.Functions)
                {
                    if (fnO.orphans > 0)
                    {
                        host.Log($"WARNING: <{fnO.IDName}> has an orphan\n", fnO, FmiLogLevel.Warning);
                        fnO.fmiHighlight = "exit-fill";
                        fmiError = true;
                    }
                    var otherAspects = host.Aspects.FindAll(x => x.FunctionIDNr == fnO.IDNr && x.Type != "Input").Count;
                    if (otherAspects > 0)
                    {
                        if (host.Aspects.FindAll(x => x.FunctionIDNr == fnO.IDNr && x.Type == "Input").Count == 0)
                        {
                            host.Log($"WARNING: <{fnO.IDName}> does not have any Inputs\n", fnO, FmiLogLevel.Warning);
                            fnO.fmiHighlight = "exit-fill";
                            fmiError = true;
                        }
                        if (host.Outputs.FindAll(x => x.FunctionIDNr == fnO.IDNr && x.Type == "Output").Count == 0)
                        {
                            host.Log($"WARNING: <{fnO.IDName}> does not have any Outputs\n", fnO, FmiLogLevel.Warning);
                            fnO.fmiHighlight = "exit-fill";
                            fmiError = true;
                        }
                    }
                }
                if (fmiError)
                {
                    host.ShowLog();
                    return returnString;
                }
                //The model check passed: classify every function and start the initialisation
                var iProfile = "";
                var iProfileExit = "";
                var iCycle0 = "BEGIN CYCLE 0\n";
                foreach (var fnBG in host.Functions)
                {
                    fnBG.wasActive = false;
                    fnBG.fmiHighlight = "";
                    var otherAspects = host.Aspects.FindAll(x => x.FunctionIDNr == fnBG.IDNr && x.Type != "Input").Count;
                    var outputs = 0;       //total number of outputs
                    var outputsToInputs = 0; //number of Outputs going to other function Inputs
                    foreach (var hasOutput in host.Outputs.FindAll(x => x.FunctionIDNr == fnBG.IDNr && x.Type == "Output"))
                    {
                        outputs += 1;
                        outputsToInputs += host.Aspects.FindAll(x => x.IDName == hasOutput.IDName && x.Type == "Input").Count;
                    }
                    var inputs = host.Aspects.FindAll(x => x.FunctionIDNr == fnBG.IDNr && x.Type == "Input").Count;
                    if (otherAspects == 0)
                    { //has only inputs or outputs (no other aspects)
                        if (inputs == 0 && outputsToInputs > 0) //Entry: no Input, at least 1 Output going to another function's Input
                        {
                            fnBG.fmiType = "Entry";
                            host.Log($"Entry function <{fnBG.IDName}>\n", fnBG);
                            iCycle0 += $"Entry function <{fnBG.IDName}> has been activated\n";
                            await EvaluateAsync(fnBG);
                            await ActivateAsync(fnBG);
                            iProfile = $"Function <{fnBG.IDName}>:\n    Output: {(fnBG.profileFn == "" ? "0" : fnBG.profileFn)}\n{iProfile}";
                        }
                        else if (outputs == 0 && inputs > 0) //Exit: no Output, at least 1 Input
                        {
                            fnBG.fmiType = "Exit";
                            host.Log($"Exit function <{fnBG.IDName}>\n", fnBG);
                            fnBG.fmiHighlight = "exit-fill";
                            trackHighlights += 1;
                            trackExitFunctions.Add(fnBG.IDNr);
                            iProfileExit += $"Function <{fnBG.IDName}>:\n    Input: {Function.ProfileToString(fnBG.PrInputs)}{"        ".Substring(0, 8 - 3)}\n";
                        }
                        else if (outputs == 0 && inputs == 0)
                        { //no aspects or couplings at all - ignore
                            fnBG.fmiType = "";
                        }
                        else if (outputs == 0 || inputs == 0)
                        { //other Background functions (e.g. an Output going to aspects that are not Inputs)
                            fnBG.fmiType = "Background";
                            iCycle0 += $"Background function <{fnBG.IDName}> has been activated\n";
                            await EvaluateAsync(fnBG);
                            await ActivateAsync(fnBG);
                        }
                        else
                        { //Passthrough: Input and Output but no other aspects
                            fnBG.fmiType = "Passthrough";
                            iProfile += ProfileText(fnBG);
                        }
                    }
                    else
                    {
                        fnBG.fmiType = "Foreground";
                        iProfile += ProfileText(fnBG);
                    }
                }
                host.Log($"\nInterpretation Profile\n{iProfile}{iProfileExit}\nSummary of FMIlog\nBegin initialisation\n");
                host.Log("---  MODEL INITIALISATION COMPLETED.\n");
                host.Log(iCycle0);
                returnString = "0";
            }
            else if (host.Cycle > 0)
            {
                host.Log($"BEGIN CYCLE {host.Cycle}\n");
                foreach (var fnBG2 in host.Functions.FindAll(x => x.fmiType != "Background"))
                {
                    if (fnBG2.fmiType == "Entry")
                    {
                        double.TryParse(fnBG2.profileFn, NumberStyles.Float, CultureInfo.InvariantCulture, out var checkProfileFn);
                        if (checkProfileFn > 0 && EntryFires(checkProfileFn))
                        {
                            await EvaluateAsync(fnBG2);
                            await ActivateAsync(fnBG2);
                            foreach (var fnBGb in host.Functions.FindAll(x => x.fmiType == "Background"))
                            {
                                await EvaluateAsync(fnBGb);
                                await ActivateAsync(fnBGb);
                            }
                        }
                        else
                        {
                            fnBG2.fmiHighlight = "";
                        }
                    }
                    else //every other function that is not Entry and not Background (all have Inputs; Exit included)
                    {
                        if (fnBG2.isActive())
                        {
                            await EvaluateAsync(fnBG2);
                            if (!await ActivateAsync(fnBG2))
                            {
                                ApplyConsume(fnBG2);
                            }
                        }
                        else
                        {
                            fnBG2.fmiHighlight = "";
                        }
                    }
                }
                returnString = host.Cycle.ToString(CultureInfo.InvariantCulture);
                if (StepTotal == null)
                {
                    if (trackExitFunctions.Count == 0)
                    {
                        returnString = ResultEnd;
                    }
                }
                else if (StepTotal != 0 && StepTotal <= host.Cycle)
                {
                    returnString = ResultEnd;
                }
            }
            foreach (var item in toActivateAspectsArray)
            {
                if (!activeAspectsArray.Contains(item))
                {
                    activeAspectsArray.Add(item);
                }
            }
            if (trackHighlights == 0)
            {
                host.Log("No functions were activated during this cycle.\n");
                if (!StepTotal.HasValue)
                {
                    returnString = ResultEnd;
                }
            }
            host.EndCycle();
            if (UnknownVariables.Count > 0 && returnString != ResultError)
            {
                return ResultBlocked;
            }
            return returnString;
        }

        /// <summary>After a cycle: activate the queued couplings and copy each upstream function's
        /// metadata into the downstream function as {toType}{outputFn}_{Key}.</summary>
        public void PropagateCouplings()
        {
            foreach (var coupling in activeAspectsArray)
            {
                coupling.activated = true;
                coupling.fmiHighlight = "entry-fill";
                coupling.wasActive = true;
                DeliverCoupling(coupling);
            }
        }

        /// <summary>When the run ends, highlight Exit functions that were never reached.</summary>
        public void MarkPendingExits()
        {
            foreach (var exitFn in host.Functions.FindAll(x => x.fmiType == "Exit" && !trackExitFunctions.Contains(x.IDNr)))
            {
                trackExitFunctions.Add(exitFn.IDNr);
                exitFn.fmiHighlight = "exit-fill";
            }
        }

        private bool EntryFires(double profileFn)
        {
            if (profileFn >= 1)
            {
                var ratio = host.Cycle / profileFn;
                return ratio == (int)ratio; //every N cycles
            }
            return rnd.NextDouble() < profileFn; //probability per cycle
        }

        private static string ProfileText(Function fn)
        {
            var text = $"Function <{fn.IDName}>:";
            text += $"\n    Input: {Function.ProfileToString(fn.PrInputs)}{"        ".Substring(0, 8 - 3)}";
            text += $"Precondition: {Function.ProfileToString(fn.PrPreconditions)}{"        ".Substring(0, 8 - Function.ProfileToString(fn.PrPreconditions).Length)}";
            text += $"Resource: {Function.ProfileToString(fn.PrResources)}{"        ".Substring(0, 8 - Function.ProfileToString(fn.PrResources).Length)}";
            text += $"Control: {Function.ProfileToString(fn.PrControls)}{"        ".Substring(0, 8 - Function.ProfileToString(fn.PrControls).Length)}";
            text += $"Time: All{"        ".Substring(0, 8 - 3)}\n";
            return text;
        }

        private async Task<bool> ActivateAsync(Function fn)
        {
            if (!ActivateFunction(fn)) return false;
            await host.AfterActivateAsync(fn);
            return true;
        }

        private async Task EvaluateAsync(Function fn)
        {
            foreach (var unknown in await host.EvaluateAsync(fn))
            {
                if (!UnknownVariables.Any(u => u.AcceptKey == unknown.AcceptKey && u.Key == unknown.Key))
                {
                    UnknownVariables.Add(unknown);
                }
            }
        }

        /// <summary>A function that could not activate may force selected Inputs to be consumed.</summary>
        private void ApplyConsume(Function fn)
        {
            var consume = fn.metadata.Find(x => x[0] == "Consume");
            if (consume is null || consume[1] is "0" or "false" or "1" or "true")
            {
                return;
            }
            var consumeOnly = consume[1];
            foreach (var coupling in host.Couplings.FindAll(x => x.toFn == fn.IDNr && !x.activated && x.toType == "I" && consumeOnly.Contains(x.label)))
            {
                fn.DeactivateCoupling("I", coupling.Name);
                activeAspectsArray.Remove(coupling);
                fn.variableList.RemoveAll(v => v.Name.StartsWith($"{coupling.toType}{coupling.outputFn}_"));
                coupling.fmiHighlight = "";
            }
        }

        private bool ActivateFunction(Function fnBG)
        {
            var canActivate = true;
            var activateAll = true;
            var activateOnly = "";
            var fnMD = fnBG.metadata.Find(x => x[0] == "Activate");
            if (fnMD is not null)
            {
                if (fnMD[1] == "0" || fnMD[1] == "false")
                {
                    canActivate = false;
                }
                if (fnMD[1] != "1" && fnMD[1] != "true")
                {
                    activateAll = false;
                    activateOnly = fnMD[1];
                }
            }
            var fnMDv2 = fnBG.metadata.Find(x => x[0] == "Variable");
            if (fnMDv2 is not null && fnBG.FunctionType != "2")
            {
                host.SetFunctionType(fnBG, fnMDv2[1] == "1" || fnMDv2[1] == "true" ? "1" : "0");
            }
            if (!canActivate)
            {
                fnBG.fmiHighlight = "hold-fill";
                return false;
            }

            fnBG.wasActive = true;
            trackHighlights += 1;
            fnBG.fmiHighlight = $"{fnBG.fmiType.ToLower()}-fill";
            if (fnBG.fmiType == "Entry")
            {
                host.Log($"Entry Function <{fnBG.IDName}> has been activated\n", fnBG);
                foreach (var coupling in host.Couplings.FindAll(x => x.outputFn == fnBG.IDNr && (activateAll || fnMD![1].Contains(x.label))))
                {
                    if (!toActivateAspectsArray.Contains(coupling))
                    {
                        toActivateAspectsArray.Add(coupling);
                    }
                }
            }
            else if (fnBG.fmiType == "Background")
            {
                //Background outputs propagate within the same cycle
                foreach (var coupling in host.Couplings.FindAll(x => x.outputFn == fnBG.IDNr && (activateAll || fnMD![1].Contains(x.label))))
                {
                    coupling.wasActive = true;
                    coupling.activated = true;
                    coupling.fmiHighlight = "background-fill";
                    DeliverCoupling(coupling);
                }
            }
            else
            {
                var holdAll = false;
                var holdOnly = "";
                var fnMDH = fnBG.metadata.Find(x => x[0] == "Hold");
                if (fnMDH is not null)
                {
                    holdAll = true;
                    if (fnMDH[1] == "0" || fnMDH[1] == "false")
                    {
                        holdAll = false;
                    }
                    if (fnMDH[1] != "1" && fnMDH[1] != "true")
                    {
                        holdAll = false;
                        holdOnly = fnMDH[1];
                    }
                }
                if (fnBG.fmiType != "Exit")
                {
                    fnBG.fmiHighlight = "entry-fill";
                    host.Log($"Function <{fnBG.IDName}> has been activated\n", fnBG);
                }
                else
                {
                    trackExitFunctions.Remove(fnBG.IDNr);
                    host.Log($"Exit function <{fnBG.IDName}> has been activated\n", fnBG);
                }
                foreach (var coupling in host.Couplings.FindAll(x => x.toFn == fnBG.IDNr && !x.activated && !holdAll && !holdOnly.Contains(x.label)))
                {//Clear the aspects that were consumed, but not if part of a repeating cycle, and not if explicitly on Hold
                    if (host.Functions.Find(x => x.IDNr == coupling.outputFn)?.fmiType != "Background")
                    {
                        if (coupling.toType is "I" or "P" or "R" or "C" or "T")
                        {
                            fnBG.DeactivateCoupling(coupling.toType, coupling.Name);
                        }
                        activeAspectsArray.Remove(coupling);
                        fnBG.variableList.RemoveAll(v => v.Name.StartsWith($"{coupling.toType}{coupling.outputFn}_"));
                    }
                    coupling.fmiHighlight = "";
                }
                foreach (var coupling in host.Couplings.FindAll(x => x.outputFn == fnBG.IDNr && x.outputFn != x.toFn && (activateAll || activateOnly.Contains(x.label))))
                { //Activate the Outputs of this function, unless selectively excluded by Activate
                    if (!toActivateAspectsArray.Contains(coupling))
                    {
                        toActivateAspectsArray.Add(coupling);
                    }
                }
            }
            return true;
        }

        private void DeliverCoupling(Coupling coupling)
        {
            var nextFn = host.Functions.Find(x => x.IDNr == coupling.toFn);
            if (nextFn is null)
            {
                return;
            }
            switch (coupling.toType)
            {
                case "I": case "P": case "R": case "C":
                    nextFn.ActivateCoupling(coupling.toType, coupling.Name, coupling.outputFn);
                    break;
                case "T":
                    nextFn.ActivateCoupling("T", coupling.Name);
                    break;
            }
            var outputFn = host.Functions.Find(x => x.IDNr == coupling.outputFn);
            if (outputFn is not null)
            {
                foreach (var meta in outputFn.metadata)
                {
                    var name = $"{coupling.toType}{coupling.outputFn}_{meta[0]}";
                    var v = nextFn.getVariable(name) ?? nextFn.newVariable(name, variableType.String);
                    v.Value = meta[1];
                }
            }
        }
    }
}
