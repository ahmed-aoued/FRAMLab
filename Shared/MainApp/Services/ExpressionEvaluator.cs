using System.Globalization;

namespace FMV_OG.Shared.MainApp.Services
{
    public class ExpressionEvaluator : IExpressionEvaluator
    {
        private readonly Random random = new();

        public string Eval(string expression, Dictionary<string, string[]> variables, List<string[]> dataRows, Dictionary<string, string> dataKeys)
        {
            var str = expression;
            int pos = -1;
            char ch = '\0';

            void NextChar()
            {
                ch = (++pos < str.Length) ? str[pos] : '`';
            }

            bool Eat(char charToEat)
            {
                while (char.IsWhiteSpace(ch))
                {
                    NextChar();
                }

                if (ch == charToEat)
                {
                    NextChar();
                    return true;
                }

                return false;
            }

            string Parse()
            {
                NextChar();
                var x = ParseExpression();
                if (pos < str.Length)
                {
                    return "NaN";
                }

                return x;
            }

            string ParseExpression()
            {
                var x = ParseTerm();
                while (true)
                {
                    if (Eat('+'))
                    {
                        x = (double.Parse(x, CultureInfo.InvariantCulture) + double.Parse(ParseTerm(), CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                    }
                    else if (Eat('-'))
                    {
                        x = (double.Parse(x, CultureInfo.InvariantCulture) - double.Parse(ParseTerm(), CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                    }
                    else if (Eat('?'))
                    {
                        if (double.Parse(x, CultureInfo.InvariantCulture) > 0)
                        {
                            str = str.Substring(pos, str.IndexOf(':') - pos);
                        }
                        else
                        {
                            str = str.Substring(str.IndexOf(':') + 1);
                        }

                        pos = -1;
                        NextChar();

                        if (Eat('"'))
                        {
                            var startPos = pos;
                            while (ch != '"' && pos < str.Length)
                            {
                                NextChar();
                            }

                            var subText = str.Substring(startPos, pos - startPos);
                            pos = str.Length;
                            return subText;
                        }

                        return ParseExpression();
                    }
                    else if (Eat('&'))
                    {
                        Eat('&');
                        var tA = double.Parse(ParseTerm(), CultureInfo.InvariantCulture) > 0;
                        x = double.Parse(x, CultureInfo.InvariantCulture) > 0 && tA ? "1" : "0";
                    }
                    else if (Eat('|'))
                    {
                        Eat('|');
                        var tO = double.Parse(ParseTerm(), CultureInfo.InvariantCulture) > 0;
                        x = double.Parse(x, CultureInfo.InvariantCulture) > 0 || tO ? "1" : "0";
                    }
                    else
                    {
                        return x;
                    }
                }
            }

            string ParseTerm()
            {
                var x = ParseFactor();
                while (true)
                {
                    if (Eat('*'))
                    {
                        x = (double.Parse(x, CultureInfo.InvariantCulture) * double.Parse(ParseFactor(), CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                    }
                    else if (Eat('/'))
                    {
                        x = (double.Parse(x, CultureInfo.InvariantCulture) / double.Parse(ParseFactor(), CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        return x;
                    }
                }
            }

            string ParseFactor()
            {
                if (Eat('+'))
                {
                    return ParseFactor();
                }

                if (Eat('-'))
                {
                    return '-' + ParseFactor();
                }

                if (Eat('!'))
                {
                    return double.Parse(ParseFactor(), CultureInfo.InvariantCulture) > 0 ? "0" : "1";
                }

                string x;
                var startPos = pos;

                if (Eat('"'))
                {
                    startPos = pos;
                    while (ch != '"' && pos < str.Length)
                    {
                        NextChar();
                    }

                    var subText = str.Substring(startPos, pos - startPos);
                    x = subText;
                    Eat('"');
                }
                else if (Eat('('))
                {
                    x = ParseExpression();
                    Eat(')');
                }
                else if (Eat('π'))
                {
                    x = Math.PI.ToString(CultureInfo.InvariantCulture);
                }
                else if ((ch >= '0' && ch <= '9') || ch == '.')
                {
                    while ((ch >= '0' && ch <= '9') || ch == '.')
                    {
                        NextChar();
                    }

                    x = str.Substring(startPos, pos - startPos);
                }
                else if (ch >= 'a' && ch <= 'z')
                {
                    while ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_')
                    {
                        NextChar();
                    }

                    var func = str.Substring(startPos, pos - startPos);
                    if (func == "lookup")
                    {
                        if (dataRows.Count <= 0 || !Eat('('))
                        {
                            return "NaN";
                        }

                        var row = int.Parse(ParseExpression());
                        if (!Eat(','))
                        {
                            return "NaN";
                        }

                        var col = int.Parse(ParseExpression());
                        if (!Eat(')') || row >= dataRows.Count || col >= dataRows[row].Length)
                        {
                            return "NaN";
                        }

                        x = dataRows[row][col];
                    }
                    else if (func == "xref")
                    {
                        if (dataKeys.Count <= 0 || !Eat('('))
                        {
                            return "NaN";
                        }

                        var key = ParseExpression();
                        if (!Eat(','))
                        {
                            return "NaN";
                        }

                        var value = ParseExpression();
                        if (!Eat(')'))
                        {
                            return "NaN";
                        }

                        key = $"{key},{value}";
                        if (!dataKeys.ContainsKey(key))
                        {
                            return "NaN";
                        }

                        x = dataKeys[key];
                    }
                    else if (func.StartsWith("rnorm"))
                    {
                        if (!Eat('('))
                        {
                            return "NaN";
                        }

                        var mu = double.Parse(ParseExpression(), CultureInfo.InvariantCulture);
                        if (!Eat(','))
                        {
                            return "NaN";
                        }

                        var sigma = double.Parse(ParseExpression(), CultureInfo.InvariantCulture);
                        if (!Eat(')'))
                        {
                            return "NaN";
                        }

                        var epsilon = double.MinValue;
                        var u = random.NextDouble();
                        while (u <= epsilon)
                        {
                            u = random.NextDouble();
                        }

                        var v = random.NextDouble();
                        var rnorm = sigma * (Math.Sqrt(-2.0 * Math.Log(u)) * Math.Sin(2.0 * Math.PI * v)) + mu;
                        x = func == "rnormi"
                            ? Convert.ToInt32(rnorm).ToString(CultureInfo.InvariantCulture)
                            : rnorm.ToString(CultureInfo.InvariantCulture);
                    }
                    else if (func == "rnd")
                    {
                        if (!Eat('('))
                        {
                            return "NaN";
                        }

                        var rangeFrom = 0.0;
                        var rangeTo = double.Parse(ParseExpression(), CultureInfo.InvariantCulture);
                        if (Eat(','))
                        {
                            rangeFrom = rangeTo;
                            rangeTo = double.Parse(ParseExpression(), CultureInfo.InvariantCulture);
                        }

                        if (!Eat(')'))
                        {
                            return "NaN";
                        }

                        var epsilon = double.MinValue;
                        var u = random.NextDouble();
                        while (u <= epsilon)
                        {
                            u = random.NextDouble();
                        }

                        if (double.IsNaN(rangeFrom) || double.IsNaN(rangeTo))
                        {
                            x = u.ToString(CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            x = (u * (rangeTo - rangeFrom) + rangeFrom).ToString(CultureInfo.InvariantCulture);
                        }
                    }
                    else if (func == "rndi")
                    {
                        if (!Eat('('))
                        {
                            return "NaN";
                        }

                        var rangeFrom = 0;
                        var rangeTo = 2;
                        var exp = ParseExpression();
                        int.TryParse(exp == "NaN" ? "2" : exp, CultureInfo.InvariantCulture, out rangeTo);
                        if (Eat(','))
                        {
                            rangeFrom = 0;
                            int.TryParse(exp == "NaN" ? "0" : exp, CultureInfo.InvariantCulture, out rangeFrom);
                            rangeTo = 2;
                            exp = ParseExpression();
                            int.TryParse(exp == "NaN" ? "2" : exp, CultureInfo.InvariantCulture, out rangeTo);
                        }

                        if (!Eat(')'))
                        {
                            return "NaN";
                        }

                        x = random.Next(rangeFrom, rangeTo).ToString(CultureInfo.InvariantCulture);
                    }
                    else if (func == "min")
                    {
                        if (!Eat('('))
                        {
                            return "NaN";
                        }

                        var a = double.Parse(ParseExpression(), CultureInfo.InvariantCulture);
                        if (!Eat(','))
                        {
                            return "NaN";
                        }

                        var b = double.Parse(ParseExpression(), CultureInfo.InvariantCulture);
                        if (!Eat(')'))
                        {
                            return "NaN";
                        }

                        x = Math.Min(a, b).ToString(CultureInfo.InvariantCulture);
                    }
                    else if (func == "max")
                    {
                        if (!Eat('('))
                        {
                            return "NaN";
                        }

                        var a = double.Parse(ParseExpression(), CultureInfo.InvariantCulture);
                        if (!Eat(','))
                        {
                            return "NaN";
                        }

                        var b = double.Parse(ParseExpression(), CultureInfo.InvariantCulture);
                        if (!Eat(')'))
                        {
                            return "NaN";
                        }

                        x = Math.Max(a, b).ToString(CultureInfo.InvariantCulture);
                    }
                    else if (func == "api")
                    {
                        return "NaN";
                    }
                    else if (func == "round")
                    {
                        if (!Eat('('))
                        {
                            return "NaN";
                        }

                        x = ParseExpression();
                        if (!Eat(','))
                        {
                            x = Math.Round(double.Parse(x, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            var f = Math.Pow(10, double.Parse(ParseExpression(), CultureInfo.InvariantCulture));
                            x = (Math.Round(double.Parse(x, CultureInfo.InvariantCulture) * f) / f).ToString(CultureInfo.InvariantCulture);
                        }

                        if (!Eat(')'))
                        {
                            return "NaN";
                        }
                    }
                    else
                    {
                        x = ParseFactor();
                        if (func == "sqrt")
                        {
                            x = Math.Sqrt(double.Parse(x, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                        }
                        else if (func == "sin")
                        {
                            x = Math.Sin(double.Parse(x, CultureInfo.InvariantCulture) * Math.PI / 180).ToString(CultureInfo.InvariantCulture);
                        }
                        else if (func == "cos")
                        {
                            x = Math.Cos(double.Parse(x, CultureInfo.InvariantCulture) * Math.PI / 180).ToString(CultureInfo.InvariantCulture);
                        }
                        else if (func == "tan")
                        {
                            x = Math.Tan(double.Parse(x, CultureInfo.InvariantCulture) * Math.PI / 180).ToString(CultureInfo.InvariantCulture);
                        }
                        else if (func == "abs")
                        {
                            x = Math.Abs(double.Parse(x, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                        }
                        else if (func == "exp")
                        {
                            x = Math.Exp(double.Parse(x, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                        }
                        else if (func == "log")
                        {
                            x = Math.Log(double.Parse(x, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                        }
                        else if (func == "ceiling")
                        {
                            x = Math.Ceiling(double.Parse(x, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                        }
                        else if (func == "floor")
                        {
                            x = Math.Floor(double.Parse(x, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                        }
                        else if (func == "int")
                        {
                            x = x.Split('.')[0];
                        }
                        else
                        {
                            x = func;
                        }
                    }
                }
                else if (ch >= 'A' && ch <= 'Z')
                {
                    while ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'z') || ch == '_')
                    {
                        NextChar();
                    }

                    var variable = str.Substring(startPos, pos - startPos);
                    x = variables.ContainsKey(variable) ? variables[variable][0] : "0";
                    if (string.IsNullOrEmpty(x))
                    {
                        x = "0";
                    }
                }
                else
                {
                    return "NaN";
                }

                if (Eat('^'))
                {
                    x = Math.Pow(double.Parse(x, CultureInfo.InvariantCulture), double.Parse(ParseFactor(), CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                }
                else if (Eat('π'))
                {
                    x = (double.Parse(x, CultureInfo.InvariantCulture) * Math.PI).ToString(CultureInfo.InvariantCulture);
                }
                else if (Eat('='))
                {
                    Eat('=');
                    if (double.TryParse(x, CultureInfo.InvariantCulture, out var isNumberE))
                    {
                        x = isNumberE == double.Parse(ParseFactor(), CultureInfo.InvariantCulture) ? "1" : "0";
                    }
                    else
                    {
                        x = x == ParseFactor() ? "1" : "0";
                    }
                }
                else if (Eat('<'))
                {
                    if (double.TryParse(x, CultureInfo.InvariantCulture, out var isNumberL))
                    {
                        if (Eat('='))
                        {
                            x = isNumberL <= double.Parse(ParseFactor(), CultureInfo.InvariantCulture) ? "1" : "0";
                        }
                        else if (Eat('>'))
                        {
                            x = isNumberL != double.Parse(ParseFactor(), CultureInfo.InvariantCulture) ? "1" : "0";
                        }
                        else
                        {
                            x = isNumberL < double.Parse(ParseFactor(), CultureInfo.InvariantCulture) ? "1" : "0";
                        }
                    }
                    else
                    {
                        var compareL = x.CompareTo(ParseFactor());
                        if (Eat('='))
                        {
                            x = compareL <= 0 ? "1" : "0";
                        }
                        else if (Eat('>'))
                        {
                            x = compareL != 0 ? "1" : "0";
                        }
                        else
                        {
                            x = compareL < 0 ? "1" : "0";
                        }
                    }
                }
                else if (Eat('>'))
                {
                    if (double.TryParse(x, CultureInfo.InvariantCulture, out var isNumberG))
                    {
                        if (Eat('<'))
                        {
                            var tX = double.Parse(ParseFactor(), CultureInfo.InvariantCulture) > 0;
                            x = (isNumberG > 0 || tX) && !(isNumberG > 0 && tX) ? "1" : "0";
                        }
                        else if (Eat('='))
                        {
                            x = isNumberG >= double.Parse(ParseFactor(), CultureInfo.InvariantCulture) ? "1" : "0";
                        }
                        else
                        {
                            x = isNumberG > double.Parse(ParseFactor(), CultureInfo.InvariantCulture) ? "1" : "0";
                        }
                    }
                    else
                    {
                        var compareG = x.CompareTo(ParseFactor());
                        if (Eat('='))
                        {
                            x = compareG >= 0 ? "1" : "0";
                        }
                        else
                        {
                            x = compareG > 0 ? "1" : "0";
                        }
                    }
                }
                else if (Eat('!') && Eat('='))
                {
                    if (double.TryParse(x, CultureInfo.InvariantCulture, out var isNumberN))
                    {
                        x = isNumberN != double.Parse(ParseFactor(), CultureInfo.InvariantCulture) ? "1" : "0";
                    }
                    else
                    {
                        var compareN = x.CompareTo(ParseFactor());
                        x = compareN != 0 ? "1" : "0";
                    }
                }

                return x ?? "NaN";
            }

            return Parse();
        }
    }
}
