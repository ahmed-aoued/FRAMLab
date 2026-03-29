using System.Data;
using System.Globalization;
using System.Xml;

namespace FMV_OG.Shared.MainApp.Services
{
    public class MetadataService : IMetadataService
    {
        public DataSet MetadataDS { get; set; } = new();
        public Dictionary<string, string> MetaDataKeys { get; set; } = new();
        public bool MetaIsDirty { get; set; }

        public void CreateMetadataTable(XmlNode fn)
        {
            if (!MetadataDS.Tables.Contains("Metadata"))
            {
                var metadataTable = new DataTable("Metadata");
                metadataTable.Columns.Add(new DataColumn("Cycle", typeof(int)));
                var colIdNr = new DataColumn("IDNr", typeof(int));
                metadataTable.Columns.Add(colIdNr);
                metadataTable.Columns.Add(new DataColumn("IDName", typeof(string)));
                MetadataDS.Tables.Add(metadataTable);
            }

            foreach (XmlElement key in fn.SelectSingleNode("metadata")!)
            {
                if (!MetadataDS.Tables["Metadata"]!.Columns.Contains(key.Name))
                {
                    MetaDataKeys.Add(key.Name, "");
                    MetadataDS.Tables["Metadata"]!.Columns.Add(new DataColumn(key.Name, typeof(string)));
                    MetadataDS.Tables["Metadata"]!.Columns.Add(new DataColumn($"{key.Name}-active", typeof(string)));
                    MetadataDS.Tables["Metadata"]!.Columns.Add(new DataColumn($"{key.Name}-equation", typeof(string)));
                }
            }

            var row = MetadataDS.Tables["Metadata"]!.NewRow();
            row["Cycle"] = -1;
            row["IDNr"] = fn.SelectSingleNode("IDNr")!.InnerText;
            row["IDName"] = fn.SelectSingleNode("IDName")!.InnerText;
            foreach (XmlElement key in fn.SelectSingleNode("metadata")!)
            {
                row[key.Name] = key.InnerText;
                row[$"{key.Name}-active"] = key.Attributes["active"]?.Value ?? "";
                row[$"{key.Name}-equation"] = key.Attributes["equation"]?.Value ?? "";
            }

            MetadataDS.Tables["Metadata"]!.Rows.Add(row);
        }

        public void UpdateMetadata(XmlDocument projectData, Function selectedFunction)
        {
            var fnMD = projectData.SelectSingleNode($"//FM/Functions/Function[IDNr={selectedFunction.IDNr}]/metadata");
            if (fnMD is not null)
            {
                fnMD.RemoveAll();
                foreach (var keyValue in selectedFunction.metadata)
                {
                    var addMeta = projectData.CreateElement(keyValue[0]);
                    addMeta.InnerText = keyValue[1];
                    var metaCheck = projectData.CreateAttribute("active");
                    metaCheck.Value = keyValue[2];
                    addMeta.Attributes!.Append(metaCheck);
                    var metaEquation = projectData.CreateAttribute("equation");
                    metaEquation.Value = keyValue[3];
                    addMeta.Attributes.Append(metaEquation);
                    fnMD.AppendChild(addMeta);
                }
            }

            MetaIsDirty = false;
        }

        public void AddMetadataResults(Function function, int cycleFMI)
        {
            var row = MetadataDS.Tables["Metadata"]!.NewRow();
            foreach (DataColumn col in MetadataDS.Tables["Metadata"]!.Columns)
            {
                switch (col.ColumnName)
                {
                    case "Cycle":
                        row[col] = cycleFMI;
                        break;
                    case "IDNr":
                        row[col] = function.IDNr;
                        break;
                    case "IDName":
                        row[col] = function.IDName;
                        break;
                    default:
                        var evalRow = MetadataDS.Tables["Metadata"]!.Select($"IDNr = '{function.IDNr}' and Cycle = -1")[0];
                        if (evalRow is not null)
                        {
                            row[col] = evalRow[col];
                        }
                        break;
                }
            }

            MetadataDS.Tables["Metadata"]!.Rows.Add(row);
        }

        public void CalcVariables(Function function, List<Coupling> couplingList, List<Function> functionList, int cycleFMI)
        {
            function.newVariable("Cycle", new string[] { cycleFMI.ToString(), "The FMI Cycle count" });
            foreach (var mdKeyValue in MetaDataKeys)
            {
                var mdKey = mdKeyValue.Key;
                var mdValue = mdKeyValue.Value;
                if (mdKey.StartsWith("Global_"))
                {
                    function.newVariable(mdKey, new string[] { mdValue, mdValue });
                }

                double Isum = 0;
                double In = 0;
                double Imin = double.NaN;
                double Imax = 0;
                double Iprd = double.NaN;
                double Psum = 0;
                double Pn = 0;
                double Pmin = double.NaN;
                double Pmax = 0;
                double Pprd = double.NaN;
                double Rsum = 0;
                double Rn = 0;
                double Rmin = double.NaN;
                double Rmax = 0;
                double Rprd = double.NaN;
                double Csum = 0;
                double Cn = 0;
                double Cmin = double.NaN;
                double Cmax = 0;
                double Cprd = double.NaN;
                double Tsum = 0;
                double Tn = 0;
                double Tmin = double.NaN;
                double Tmax = 0;
                double Tprd = double.NaN;
                double Amin = double.NaN;
                double Amax = double.NaN;
                double Aprd = double.NaN;

                foreach (var coupling in couplingList.FindAll(x => x.toFn == function.IDNr && x.fmiHighlight != ""))
                {
                    var outFn = functionList.Find(x => x.IDNr == coupling.outputFn);
                    var fnMD = outFn?.getVariable(mdKey);
                    if (fnMD is null && outFn?.metadata.Find(x => x[0] == mdKey) is not null)
                    {
                        fnMD = new string[] { "0", "0" };
                    }

                    if (fnMD is not null)
                    {
                        var fnMDString = fnMD[0];
                        if (!double.TryParse(string.IsNullOrEmpty(fnMDString) ? "0" : fnMDString, CultureInfo.InvariantCulture, out var fnMDValue))
                        {
                            if (fnMDString.ToLower() == "true")
                            {
                                fnMDValue = 1;
                            }
                        }

                        switch (coupling.toType)
                        {
                            case "I":
                                In++;
                                Isum += fnMDValue;
                                Imin = double.IsNaN(Imin) ? fnMDValue : Math.Min(Imin, fnMDValue);
                                Imax = Math.Max(Imax, fnMDValue);
                                Iprd = double.IsNaN(Iprd) ? fnMDValue : Iprd * fnMDValue;
                                break;
                            case "P":
                                Pn++;
                                Psum += fnMDValue;
                                Pmin = double.IsNaN(Pmin) ? fnMDValue : Math.Min(Pmin, fnMDValue);
                                Pmax = Math.Max(Pmax, fnMDValue);
                                Pprd = double.IsNaN(Pprd) ? fnMDValue : Pprd * fnMDValue;
                                break;
                            case "R":
                                Rn++;
                                Rsum += fnMDValue;
                                Rmin = double.IsNaN(Rmin) ? fnMDValue : Math.Min(Rmin, fnMDValue);
                                Rmax = Math.Max(Rmax, fnMDValue);
                                Rprd = double.IsNaN(Rprd) ? fnMDValue : Rprd * fnMDValue;
                                break;
                            case "C":
                                Cn++;
                                Csum += fnMDValue;
                                Cmin = double.IsNaN(Cmin) ? fnMDValue : Math.Min(Cmin, fnMDValue);
                                Cmax = Math.Max(Cmax, fnMDValue);
                                Cprd = double.IsNaN(Cprd) ? fnMDValue : Cprd * fnMDValue;
                                break;
                            case "T":
                                Tn++;
                                Tsum += fnMDValue;
                                Tmin = double.IsNaN(Tmin) ? fnMDValue : Math.Min(Tmin, fnMDValue);
                                Tmax = Math.Max(Tmax, fnMDValue);
                                Tprd = double.IsNaN(Tprd) ? fnMDValue : Tprd * fnMDValue;
                                break;
                        }
                    }
                }

                if (In > 0)
                {
                    function.newVariable($"In_{mdKey}", new string[] { In.ToString(CultureInfo.InvariantCulture), $"Number of {mdKey} from Inputs" });
                    function.newVariable($"Isum_{mdKey}", new string[] { Isum.ToString(CultureInfo.InvariantCulture), $"Sum of {mdKey} from Inputs" });
                    function.newVariable($"Iavg_{mdKey}", new string[] { (Isum / In).ToString(CultureInfo.InvariantCulture), $"Average of {mdKey} from Inputs" });
                    function.newVariable($"Imin_{mdKey}", new string[] { Imin.ToString(CultureInfo.InvariantCulture), $"Minimum of {mdKey} from Inputs" });
                    function.newVariable($"Imax_{mdKey}", new string[] { Imax.ToString(CultureInfo.InvariantCulture), $"Maximum of {mdKey} from Inputs" });
                    function.newVariable($"Iprd_{mdKey}", new string[] { Iprd.ToString(CultureInfo.InvariantCulture), $"Product of {mdKey} from Inputs" });
                    Amin = Imin;
                    Amax = Imax;
                    Aprd = Iprd;
                }

                if (Pn > 0)
                {
                    function.newVariable($"Pn_{mdKey}", new string[] { Pn.ToString(CultureInfo.InvariantCulture), $"Number of {mdKey} from Preconditions" });
                    function.newVariable($"Psum_{mdKey}", new string[] { Psum.ToString(CultureInfo.InvariantCulture), $"Sum of {mdKey} from Preconditions" });
                    function.newVariable($"Pavg_{mdKey}", new string[] { (Psum / Pn).ToString(CultureInfo.InvariantCulture), $"Average of {mdKey} from Preconditions" });
                    function.newVariable($"Pmin_{mdKey}", new string[] { Pmin.ToString(CultureInfo.InvariantCulture), $"Minimum of {mdKey} from Preconditions" });
                    function.newVariable($"Pmax_{mdKey}", new string[] { Pmax.ToString(CultureInfo.InvariantCulture), $"Maximum of {mdKey} from Preconditions" });
                    function.newVariable($"Pprd_{mdKey}", new string[] { Pprd.ToString(CultureInfo.InvariantCulture), $"Product of {mdKey} from Preconditions" });
                    Amin = double.IsNaN(Amin) ? Pmin : Math.Min(Amin, Pmin);
                    Amax = double.IsNaN(Amax) ? Pmax : Math.Max(Amax, Pmax);
                    Aprd = double.IsNaN(Aprd) ? Pprd : Aprd * Pprd;
                }

                if (Rn > 0)
                {
                    function.newVariable($"Rn_{mdKey}", new string[] { Rn.ToString(CultureInfo.InvariantCulture), $"Number of {mdKey} from Resources" });
                    function.newVariable($"Rsum_{mdKey}", new string[] { Rsum.ToString(CultureInfo.InvariantCulture), $"Sum of {mdKey} from Resources" });
                    function.newVariable($"Ravg_{mdKey}", new string[] { (Rsum / Rn).ToString(CultureInfo.InvariantCulture), $"Average of {mdKey} from Resources" });
                    function.newVariable($"Rmin_{mdKey}", new string[] { Rmin.ToString(CultureInfo.InvariantCulture), $"Minimum of {mdKey} from Resources" });
                    function.newVariable($"Rmax_{mdKey}", new string[] { Rmax.ToString(CultureInfo.InvariantCulture), $"Maximum of {mdKey} from Resources" });
                    function.newVariable($"Rprd_{mdKey}", new string[] { Rprd.ToString(CultureInfo.InvariantCulture), $"Product of {mdKey} from Resources" });
                    Amin = double.IsNaN(Amin) ? Rmin : Math.Min(Amin, Rmin);
                    Amax = double.IsNaN(Amax) ? Rmax : Math.Max(Amax, Rmax);
                    Aprd = double.IsNaN(Aprd) ? Rprd : Aprd * Rprd;
                }

                if (Cn > 0)
                {
                    function.newVariable($"Cn_{mdKey}", new string[] { Cn.ToString(CultureInfo.InvariantCulture), $"Number of {mdKey} from Controls" });
                    function.newVariable($"Csum_{mdKey}", new string[] { Csum.ToString(CultureInfo.InvariantCulture), $"Sum of {mdKey} from Controls" });
                    function.newVariable($"Cavg_{mdKey}", new string[] { (Csum / Cn).ToString(CultureInfo.InvariantCulture), $"Average of {mdKey} from Controls" });
                    function.newVariable($"Cmin_{mdKey}", new string[] { Cmin.ToString(CultureInfo.InvariantCulture), $"Minimum of {mdKey} from Controls" });
                    function.newVariable($"Cmax_{mdKey}", new string[] { Cmax.ToString(CultureInfo.InvariantCulture), $"Maximum of {mdKey} from Controls" });
                    function.newVariable($"Cprd_{mdKey}", new string[] { Cprd.ToString(CultureInfo.InvariantCulture), $"Product of {mdKey} from Controls" });
                    Amin = double.IsNaN(Amin) ? Cmin : Math.Min(Amin, Cmin);
                    Amax = double.IsNaN(Amax) ? Cmax : Math.Max(Amax, Cmax);
                    Aprd = double.IsNaN(Aprd) ? Cprd : Aprd * Cprd;
                }

                if (Tn > 0)
                {
                    function.newVariable($"Tn_{mdKey}", new string[] { Tn.ToString(CultureInfo.InvariantCulture), $"Number of {mdKey} from Times" });
                    function.newVariable($"Tsum_{mdKey}", new string[] { Tsum.ToString(CultureInfo.InvariantCulture), $"Sum of {mdKey} from Times" });
                    function.newVariable($"Tavg_{mdKey}", new string[] { (Tsum / Tn).ToString(CultureInfo.InvariantCulture), $"Average of {mdKey} from Times" });
                    function.newVariable($"Tmin_{mdKey}", new string[] { Tmin.ToString(CultureInfo.InvariantCulture), "Minimum of {mdKey} from Times" });
                    function.newVariable($"Tmax_{mdKey}", new string[] { Tmax.ToString(CultureInfo.InvariantCulture), $"Maximum of {mdKey} from Times" });
                    function.newVariable($"Tprd_{mdKey}", new string[] { Tprd.ToString(CultureInfo.InvariantCulture), $"Product of {mdKey} from Times" });
                    Amin = double.IsNaN(Amin) ? Tmin : Math.Min(Amin, Tmin);
                    Amax = double.IsNaN(Amax) ? Tmax : Math.Max(Amax, Tmax);
                    Aprd = double.IsNaN(Aprd) ? Tprd : Aprd * Tprd;
                }

                if (In + Pn + Rn + Cn + Tn > 0)
                {
                    function.newVariable($"An_{mdKey}", new string[] { (In + Pn + Rn + Cn + Tn).ToString(CultureInfo.InvariantCulture), $"Number of {mdKey} from All Aspects" });
                    function.newVariable($"Asum_{mdKey}", new string[] { (Isum + Psum + Rsum + Csum + Tsum).ToString(CultureInfo.InvariantCulture), $"Sum of {mdKey} from All Aspects" });
                    function.newVariable($"Aavg_{mdKey}", new string[] { ((Isum + Psum + Rsum + Csum + Tsum) / (In + Pn + Rn + Cn + Tn)).ToString(CultureInfo.InvariantCulture), $"Average of {mdKey} from All Aspects" });
                    function.newVariable($"Amin_{mdKey}", new string[] { Amin.ToString(CultureInfo.InvariantCulture), $"Minimum of {mdKey} from All Aspects" });
                    function.newVariable($"Amax_{mdKey}", new string[] { Amax.ToString(CultureInfo.InvariantCulture), $"Maximum of {mdKey} from All Aspects" });
                    function.newVariable($"Aprd_{mdKey}", new string[] { Aprd.ToString(CultureInfo.InvariantCulture), $"Product of {mdKey} from All Aspects" });
                }
            }
        }
    }
}
