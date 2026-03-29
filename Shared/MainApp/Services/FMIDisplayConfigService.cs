using System.Globalization;
using System.Xml;

namespace FMV_OG.Shared.MainApp.Services
{
    public class FMIDisplayConfigService : IFMIDisplayConfigService
    {
        public Dictionary<string, string> DisplaySetup { get; } = new();
        public Dictionary<string, double> DisplayValue { get; } = new();
        public List<string> TypeFMIDisplay { get; private set; } = new() { "", "Blend", "Blend", "Blend", "Blend", "Blend", "Blend" };

        public void SetTypeFMIDisplay(XmlDocument projectData, List<string> typeFMIdisplay)
        {
            TypeFMIDisplay = typeFMIdisplay;
            if (projectData.SelectSingleNode("//FM/Functions/@typeFMIdisplay") == null)
            {
                projectData.SelectSingleNode("//FM/Functions")!.Attributes!.Append(projectData.CreateAttribute("typeFMIdisplay"));
            }

            projectData.SelectSingleNode("//FM/Functions/@typeFMIdisplay")!.Value = string.Join("|", TypeFMIDisplay);
        }

        public void UpdateDisplaySetup(XmlDocument projectData, string index, string attr, string value)
        {
            if (projectData.SelectSingleNode($"//FM/Functions/@{attr}") == null)
            {
                projectData.SelectSingleNode("//FM/Functions")!.Attributes!.Append(projectData.CreateAttribute(attr));
            }

            projectData.SelectSingleNode($"//FM/Functions/@{attr}")!.Value = value;
            DisplaySetup[attr] = value;

            double.TryParse(value, CultureInfo.InvariantCulture, out var dValue);
            DisplayValue[attr] = DisplaySetup[attr] == "" ? double.NaN : dValue;

            CheckDisplayValues(index);
        }

        public void CheckDisplayValues(string index)
        {
            var minA = Math.Min(DisplayValue[$"colour_{index}4"], DisplayValue[$"colour_{index}3"]);
            minA = Math.Max(minA, DisplayValue[$"colour_{index}2"]);
            minA = Math.Max(minA, DisplayValue[$"colour_{index}1"]);
            if (double.IsNaN(minA))
            {
                minA = 0;
            }

            var maxA = Math.Max(DisplayValue[$"colour_{index}0"], DisplayValue[$"colour_{index}1"]);
            maxA = Math.Max(maxA, DisplayValue[$"colour_{index}2"]);
            maxA = Math.Max(maxA, DisplayValue[$"colour_{index}3"]);
            if (double.IsNaN(maxA))
            {
                maxA = 1;
            }

            if (double.IsNaN(DisplayValue[$"colour_{index}0"])) { DisplayValue[$"colour_{index}0"] = minA - 1.0; }
            if (double.IsNaN(DisplayValue[$"colour_{index}4"])) { DisplayValue[$"colour_{index}4"] = maxA * 1.0; }
            if (double.IsNaN(DisplayValue[$"colour_{index}2"])) { DisplayValue[$"colour_{index}2"] = (DisplayValue[$"colour_{index}0"] + DisplayValue[$"colour_{index}4"]) * 0.5; }
            if (double.IsNaN(DisplayValue[$"colour_{index}1"])) { DisplayValue[$"colour_{index}1"] = (DisplayValue[$"colour_{index}0"] + DisplayValue[$"colour_{index}2"]) * 0.5; }
            if (double.IsNaN(DisplayValue[$"colour_{index}3"])) { DisplayValue[$"colour_{index}3"] = (DisplayValue[$"colour_{index}2"] + DisplayValue[$"colour_{index}4"]) * 0.5; }
        }

        public void InitialDisplaySetup(XmlDocument projectData, string attr, string value, bool overwrite = false)
        {
            if (projectData.SelectSingleNode($"//FM/Functions/@{attr}") == null)
            {
                projectData.SelectSingleNode("//FM/Functions")!.Attributes!.Append(projectData.CreateAttribute(attr));
                projectData.SelectSingleNode($"//FM/Functions/@{attr}")!.Value = value;
            }

            var dValue = double.NaN;
            if (projectData.SelectSingleNode($"//FM/Functions/@{attr}")!.Value is not null)
            {
                if (overwrite)
                {
                    projectData.SelectSingleNode($"//FM/Functions/@{attr}")!.Value = value;
                }

                DisplaySetup[attr] = projectData.SelectSingleNode($"//FM/Functions/@{attr}")!.Value!;
                double.TryParse(DisplaySetup[attr], CultureInfo.InvariantCulture, out dValue);
            }
            else
            {
                DisplaySetup[attr] = "";
            }

            DisplayValue[attr] = DisplaySetup[attr] == "" ? double.NaN : dValue;
        }
    }
}
