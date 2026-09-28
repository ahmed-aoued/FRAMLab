using System.Globalization;
using System.Xml;

namespace FMV_OG.Shared.MainApp.Services
{
    public class ProjectDocumentService : IProjectDocumentService
    {
        public void DefaultFnAttributes(XmlDocument projectData, XmlNode fn)
        {
            if (fn.SelectSingleNode("@orphans") == null)
            {
                fn.Attributes!.Append(projectData.CreateAttribute("orphans"));
            }

            fn.SelectSingleNode("@orphans")!.Value = "0";
            EnsureAttribute(projectData, fn, "profileFn");
            EnsureAttribute(projectData, fn, "profileI");
            EnsureAttribute(projectData, fn, "profileP");
            EnsureAttribute(projectData, fn, "profileR");
            EnsureAttribute(projectData, fn, "profileC");
            EnsureAttribute(projectData, fn, "profileT");
            EnsureAttribute(projectData, fn, "groupType");
            EnsureAttribute(projectData, fn, "groupIDNr");
            EnsureAttribute(projectData, fn, "groupActive");
            EnsureAttribute(projectData, fn, "groupX");
            EnsureAttribute(projectData, fn, "groupY");

            if (fn.SelectSingleNode("metadata") == null)
            {
                fn.AppendChild(projectData.CreateElement("metadata"));
            }

            if (fn.SelectSingleNode("Description") == null)
            {
                fn.AppendChild(projectData.CreateElement("Description"));
            }
        }

        public void SetFunctionIsInput(XmlDocument projectData, string selectedFn, Function? selectedFunction, string isInput)
        {
            projectData.SelectSingleNode($"//FM/Functions/Function[IDNr={selectedFn}]/@isInput")!.InnerText = isInput;
            if (selectedFunction is not null)
            {
                selectedFunction.isInput = isInput;
            }
        }

        public void SetFunctionOrphans(XmlDocument projectData, string selectedFn, Function? selectedFunction, int orphans)
        {
            projectData.SelectSingleNode($"//FM/Functions/Function[IDNr={selectedFn}]/@orphans")!.InnerText = orphans.ToString();
            if (selectedFunction is not null)
            {
                selectedFunction.orphans = orphans;
            }
        }

        public void SetFunctionTypeByConnections(XmlDocument projectData, string selectedFn, Function? selectedFunction)
        {
            var check1 = projectData.SelectNodes($"//FM/Preconditions/Precondition[FunctionIDNr=\"{selectedFn}\"]")?.Count;
            check1 += projectData.SelectNodes($"//FM/Resources/Resource[FunctionIDNr=\"{selectedFn}\"]")?.Count;
            check1 += projectData.SelectNodes($"//FM/Controls/Control[FunctionIDNr=\"{selectedFn}\"]")?.Count;
            check1 += projectData.SelectNodes($"//FM/Times/Time[FunctionIDNr=\"{selectedFn}\"]")?.Count;
            var check2 = projectData.SelectNodes($"//FM/Outputs/Output[FunctionIDNr=\"{selectedFn}\"]")?.Count;
            var check3 = projectData.SelectNodes($"//FM/Inputs/Input[FunctionIDNr=\"{selectedFn}\"]")?.Count;
            if (check1 == 0 && (check2 == 0 || check3 == 0))
            {
                SetFunctionType(projectData, selectedFn, "2", selectedFunction);
            }
            else
            {
                SetFunctionType(projectData, selectedFn, "0", selectedFunction);
            }
        }

        public bool DeleteFunction(StateContainer stateContainer)
        {
            if (stateContainer.aspectsList.FindAll(x => x.FunctionIDNr == stateContainer.selectedFn).Count == 0
                && stateContainer.projectData_Undo[0].SelectNodes($"//FM//Outputs/Output[FunctionIDNr=\"{stateContainer.selectedFn}\"]")?.Count == 0)
            {
                stateContainer.updateUndo();
                stateContainer.projectData_Undo[0].SelectSingleNode("//FM/Functions")!
                    .RemoveChild(stateContainer.projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={stateContainer.selectedFn}]")!);

                stateContainer.functionList.RemoveAll(x => x.IDNr == stateContainer.selectedFn);
                stateContainer.couplingList.RemoveAll(x => x.outputFn == stateContainer.selectedFn || x.toFn == stateContainer.selectedFn);

                var oldChildren = stateContainer.projectData_Undo[0].SelectNodes($"//FM/Aspects/Aspect[@outputFn='{stateContainer.selectedFn}' or @toFn='{stateContainer.selectedFn}']")!;
                foreach (XmlNode oldChild in oldChildren)
                {
                    stateContainer.projectData_Undo[0].SelectSingleNode("//FM/Aspects")!.RemoveChild(oldChild);
                }

                return true;
            }

            return false;
        }

        public void SetFunctionIdName(StateContainer stateContainer, string idName)
        {
            stateContainer.updateUndo();
            stateContainer.projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={stateContainer.selectedFn}]/IDName")!.InnerText = idName;
            if (stateContainer.selectedFunction is not null)
            {
                stateContainer.selectedFunction.IDName = idName;
            }
        }

        public void SetFunctionDescription(StateContainer stateContainer, string description)
        {
            stateContainer.updateUndo();
            stateContainer.projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={stateContainer.selectedFn}]/Description")!.InnerText = description;
            if (stateContainer.selectedFunction is not null)
            {
                stateContainer.selectedFunction.Description = description;
            }
        }

        public void UpdateFunctionXY(XmlDocument projectData, string selectedFn, double x, double y, string? functionId = null)
        {
            if (functionId == null)
            {
                functionId = selectedFn;
            }

            projectData.SelectSingleNode($"//FM/Functions/Function[IDNr={functionId}]/@x")!.InnerText = x.ToString("0.##", CultureInfo.InvariantCulture);
            projectData.SelectSingleNode($"//FM/Functions/Function[IDNr={functionId}]/@y")!.InnerText = y.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public void SetFunctionScript(XmlDocument projectData, string functionId, FMV_OG.Shared.MainApp.Models.ScriptModel script)
        {
            var fnNode = projectData.SelectSingleNode($"//FM/Functions/Function[IDNr={functionId}]");
            if (fnNode is null)
            {
                return;
            }
            script.WriteXml(projectData, fnNode);
        }

        public void UpdateAspectXY(XmlDocument projectData, string selectedLabel, double x, double y, string directionX, string directionY)
        {
            projectData.SelectSingleNode($"//FM/Aspects/Aspect[Name=\"{selectedLabel}\"]/@x")!.InnerText = x.ToString("0.##", CultureInfo.InvariantCulture);
            projectData.SelectSingleNode($"//FM/Aspects/Aspect[Name=\"{selectedLabel}\"]/@y")!.InnerText = y.ToString("0.##", CultureInfo.InvariantCulture);
            projectData.SelectSingleNode($"//FM/Aspects/Aspect[Name=\"{selectedLabel}\"]/@directionX")!.InnerText = directionX;
            projectData.SelectSingleNode($"//FM/Aspects/Aspect[Name=\"{selectedLabel}\"]/@directionY")!.InnerText = directionY;
        }

        public void ResetAspect(StateContainer stateContainer, string[] dictArray, double xO, double yO, double aX, double aY, int aFW)
        {
            var projectData = stateContainer.projectData_Undo[0];
            var aName = string.Join("|", dictArray);
            var appendNew = false;
            double labelDx = 0;
            double labelDy = 0;
            var directionX = "from";
            var directionY = "to";
            var notGroup = "";

            var aspectI = projectData.SelectSingleNode($"//FM/Aspects/Aspect[Name=\"{aName}\"]");
            if (aspectI == null)
            {
                aspectI = projectData.CreateElement("Aspect");
                var aspectIx = projectData.CreateAttribute("x");
                aspectIx.Value = "0";
                aspectI.Attributes!.Append(aspectIx);
                var aspectIy = projectData.CreateAttribute("y");
                aspectIy.Value = "0";
                aspectI.Attributes.Append(aspectIy);
                var aspectIdx = projectData.CreateAttribute("directionX");
                aspectIdx.Value = "from";
                aspectI.Attributes.Append(aspectIdx);
                var aspectIdy = projectData.CreateAttribute("directionY");
                aspectIdy.Value = "to";
                aspectI.Attributes.Append(aspectIdy);
                var aspectIof = projectData.CreateAttribute("outputFn");
                aspectIof.Value = dictArray[0];
                aspectI.Attributes.Append(aspectIof);
                var aspectItf = projectData.CreateAttribute("toFn");
                aspectItf.Value = dictArray[2];
                aspectI.Attributes.Append(aspectItf);
                var addNotGroup = projectData.CreateAttribute("notGroup");
                addNotGroup.Value = "";
                aspectI.Attributes.Append(addNotGroup);
                var aspectIname = projectData.CreateElement("Name");
                aspectIname.InnerText = aName;
                aspectI.AppendChild(aspectIname);
                appendNew = true;
            }
            else
            {
                double.TryParse(aspectI.SelectSingleNode("@x")?.InnerText ?? "0", CultureInfo.InvariantCulture, out labelDx);
                double.TryParse(aspectI.SelectSingleNode("@y")?.InnerText ?? "0", CultureInfo.InvariantCulture, out labelDy);
                directionX = aspectI.SelectSingleNode("@directionX")?.InnerText ?? "from";
                directionY = aspectI.SelectSingleNode("@directionY")?.InnerText ?? "to";

                if (aspectI.SelectSingleNode("Curve") is not null)
                {
                    aspectI.RemoveChild(aspectI.SelectSingleNode("Curve")!);
                }

                if (aspectI.SelectSingleNode("Curve2") is not null)
                {
                    aspectI.RemoveChild(aspectI.SelectSingleNode("Curve2")!);
                }

                notGroup = aspectI.SelectSingleNode("@notGroup")?.InnerText ?? "";
                if (notGroup == "true")
                {
                    notGroup = "";
                    aspectI.SelectSingleNode("@notGroup")!.InnerText = "";
                }
            }

            var aspectIcurve = projectData.CreateElement("Curve");
            var aspectIcurve2 = projectData.CreateElement("Curve2");
            var addCoupling = new Coupling(
                aName,
                labelDx,
                labelDy,
                directionX,
                directionY,
                notGroup,
                dictArray[0],
                dictArray[1],
                dictArray[2],
                dictArray[3],
                stateContainer.functionList.Find(x => x.IDNr == dictArray[0])!.x + xO,
                stateContainer.functionList.Find(x => x.IDNr == dictArray[0])!.y + yO,
                0,
                0);
            stateContainer.couplingList.Add(addCoupling);
            addCoupling.ReturnTextLines(aFW);
            aspectIcurve2.InnerText = addCoupling.reDrawLines(
                stateContainer.functionList.Find(x => x.IDNr == dictArray[2])!.x + aX,
                stateContainer.functionList.Find(x => x.IDNr == dictArray[2])!.y + aY,
                false);
            aspectIcurve.InnerText = addCoupling.curve;
            aspectI.AppendChild(aspectIcurve);
            aspectI.AppendChild(aspectIcurve2);
            if (appendNew)
            {
                projectData.SelectSingleNode("//FM/Aspects")!.AppendChild(aspectI);
            }
        }

        public void SetFunctionType(XmlDocument projectData, string functionId, string functionType, Function? function = null)
        {
            projectData.SelectSingleNode($"//FM/Functions/Function[IDNr={functionId}]/FunctionType")!.InnerText = functionType;
            if (function is not null)
            {
                function.FunctionType = functionType;
            }
        }

        private static void EnsureAttribute(XmlDocument projectData, XmlNode node, string attributeName)
        {
            if (node.SelectSingleNode($"@{attributeName}") == null)
            {
                node.Attributes!.Append(projectData.CreateAttribute(attributeName));
            }
        }
    }
}
