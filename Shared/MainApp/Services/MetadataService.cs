using System.Data;
using System.Globalization;
using System.Xml;


//To Change
//CalcVariables()
//

namespace FRAMLab.Shared.MainApp.Services
{
    public class MetadataService : IMetadataService
    {
        public DataSet MetadataDS { get; set; } = new();
        public Dictionary<string, string> MetaDataKeys { get; set; } = new();
        public bool MetaIsDirty { get; set; }

        private static void SetStringVariable(Function function, string name, string value)
        {
            var v = function.getVariable(name) ?? function.newVariable(name, variableType.String);
            function.setVariable(v, value);
        }

        private static void SetIntVariable(Function function, string name, int value)
        {
            var v = function.getVariable(name) ?? function.newVariable(name, variableType.Int);
            function.setVariable(v, value);
        }

        private static void SetDoubleVariable(Function function, string name, double value)
        {
            var v = function.getVariable(name) ?? function.newVariable(name, variableType.Double);
            function.setVariable(v, value);
        }

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
            //function.newVariable("Cycle", new string[] { cycleFMI.ToString(), "The FMI Cycle count" });
            var vCycle = function.getVariable("Cycle") ?? function.newVariable("Cycle", variableType.Int);
            function.setVariable(vCycle, cycleFMI);
            foreach (var mdKeyValue in MetaDataKeys)
            {
                var mdKey = mdKeyValue.Key;
                var mdValue = mdKeyValue.Value;
                if (mdKey.StartsWith("Global_"))
                {
                    //function.newVariable(mdKey, new string[] { mdValue, mdValue });
                    var vGlobal = function.getVariable(mdKey) ?? function.newVariable(mdKey, variableType.String);
                    function.setVariable(vGlobal, mdValue);
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
                    var hasMetadataKey = outFn?.metadata.Find(x => x[0] == mdKey) is not null;

                    if (fnMD is not null || hasMetadataKey)
                    {
                        var fnMDString = fnMD?.Value?.ToString() ?? "0";
                        if (!double.TryParse(string.IsNullOrEmpty(fnMDString) ? "0" : fnMDString, CultureInfo.InvariantCulture, out double fnMDValue))
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
                    SetIntVariable(function, $"In_{mdKey}", (int)In);
                    SetDoubleVariable(function, $"Isum_{mdKey}", Isum);
                    SetDoubleVariable(function, $"Iavg_{mdKey}", Isum / In);
                    SetDoubleVariable(function, $"Imin_{mdKey}", Imin);
                    SetDoubleVariable(function, $"Imax_{mdKey}", Imax);
                    SetDoubleVariable(function, $"Iprd_{mdKey}", Iprd);
                    Amin = Imin;
                    Amax = Imax;
                    Aprd = Iprd;
                }

                if (Pn > 0)
                {
                    SetIntVariable(function, $"Pn_{mdKey}", (int)Pn);
                    SetDoubleVariable(function, $"Psum_{mdKey}", Psum);
                    SetDoubleVariable(function, $"Pavg_{mdKey}", Psum / Pn);
                    SetDoubleVariable(function, $"Pmin_{mdKey}", Pmin);
                    SetDoubleVariable(function, $"Pmax_{mdKey}", Pmax);
                    SetDoubleVariable(function, $"Pprd_{mdKey}", Pprd);
                    Amin = double.IsNaN(Amin) ? Pmin : Math.Min(Amin, Pmin);
                    Amax = double.IsNaN(Amax) ? Pmax : Math.Max(Amax, Pmax);
                    Aprd = double.IsNaN(Aprd) ? Pprd : Aprd * Pprd;
                }

                if (Rn > 0)
                {
                    SetIntVariable(function, $"Rn_{mdKey}", (int)Rn);
                    SetDoubleVariable(function, $"Rsum_{mdKey}", Rsum);
                    SetDoubleVariable(function, $"Ravg_{mdKey}", Rsum / Rn);
                    SetDoubleVariable(function, $"Rmin_{mdKey}", Rmin);
                    SetDoubleVariable(function, $"Rmax_{mdKey}", Rmax);
                    SetDoubleVariable(function, $"Rprd_{mdKey}", Rprd);
                    Amin = double.IsNaN(Amin) ? Rmin : Math.Min(Amin, Rmin);
                    Amax = double.IsNaN(Amax) ? Rmax : Math.Max(Amax, Rmax);
                    Aprd = double.IsNaN(Aprd) ? Rprd : Aprd * Rprd;
                }

                if (Cn > 0)
                {
                    SetIntVariable(function, $"Cn_{mdKey}", (int)Cn);
                    SetDoubleVariable(function, $"Csum_{mdKey}", Csum);
                    SetDoubleVariable(function, $"Cavg_{mdKey}", Csum / Cn);
                    SetDoubleVariable(function, $"Cmin_{mdKey}", Cmin);
                    SetDoubleVariable(function, $"Cmax_{mdKey}", Cmax);
                    SetDoubleVariable(function, $"Cprd_{mdKey}", Cprd);
                    Amin = double.IsNaN(Amin) ? Cmin : Math.Min(Amin, Cmin);
                    Amax = double.IsNaN(Amax) ? Cmax : Math.Max(Amax, Cmax);
                    Aprd = double.IsNaN(Aprd) ? Cprd : Aprd * Cprd;
                }

                if (Tn > 0)
                {
                    SetIntVariable(function, $"Tn_{mdKey}", (int)Tn);
                    SetDoubleVariable(function, $"Tsum_{mdKey}", Tsum);
                    SetDoubleVariable(function, $"Tavg_{mdKey}", Tsum / Tn);
                    SetDoubleVariable(function, $"Tmin_{mdKey}", Tmin);
                    SetDoubleVariable(function, $"Tmax_{mdKey}", Tmax);
                    SetDoubleVariable(function, $"Tprd_{mdKey}", Tprd);
                    Amin = double.IsNaN(Amin) ? Tmin : Math.Min(Amin, Tmin);
                    Amax = double.IsNaN(Amax) ? Tmax : Math.Max(Amax, Tmax);
                    Aprd = double.IsNaN(Aprd) ? Tprd : Aprd * Tprd;
                }

                if (In + Pn + Rn + Cn + Tn > 0)
                {
                    SetIntVariable(function, $"An_{mdKey}", (int)(In + Pn + Rn + Cn + Tn));
                    SetDoubleVariable(function, $"Asum_{mdKey}", Isum + Psum + Rsum + Csum + Tsum);
                    SetDoubleVariable(function, $"Aavg_{mdKey}", (Isum + Psum + Rsum + Csum + Tsum) / (In + Pn + Rn + Cn + Tn));
                    SetDoubleVariable(function, $"Amin_{mdKey}", Amin);
                    SetDoubleVariable(function, $"Amax_{mdKey}", Amax);
                    SetDoubleVariable(function, $"Aprd_{mdKey}", Aprd);
                }
            }
        }
    }
}
