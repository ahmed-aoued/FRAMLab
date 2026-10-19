using System.Collections.Generic;
using System.Linq;

namespace FRAMLab.Shared.MainApp.Services.Scripting
{
    /// <summary>A declared symbol visible to a script: local/static/global variables,
    /// read-only aspect variables, host functions, and built-ins.</summary>
    public sealed class Symbol
    {
        public string Name { get; }
        public string DataType { get; }      // BOOL/INT/REAL/STRING/TIME
        public bool IsWritable { get; }
        public string Category { get; }      // Temp/Static/Global/Input/Output/Precondition/Resource/Control/Time/Host
        public bool IsFunction { get; }

        public Symbol(string name, string dataType, bool isWritable, string category, bool isFunction = false)
        {
            Name = name; DataType = dataType; IsWritable = isWritable; Category = category; IsFunction = isFunction;
        }
    }

    /// <summary>Resolves identifiers for the semantic analyzer.</summary>
    public sealed class SymbolTable
    {
        private readonly Dictionary<string, Symbol> symbols = new(System.StringComparer.OrdinalIgnoreCase);

        public void Add(Symbol s) => symbols[s.Name] = s;
        public bool TryGet(string name, out Symbol? symbol) => symbols.TryGetValue(name, out symbol);
        public bool Contains(string name) => symbols.ContainsKey(name);
        public IEnumerable<Symbol> All => symbols.Values;
    }
}
