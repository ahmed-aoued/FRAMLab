using System.Collections.Generic;
using System.Linq;

namespace FRAMLab.Shared.MainApp.Services.Scripting
{
    /// <summary>A declared symbol visible to a script: local/static/global variables,
    /// read-only aspect variables, host functions, and built-ins.</summary>
    public sealed class Symbol
    {
        public string Name { get; }
        /// <summary>Element type for arrays, value type for scalars.</summary>
        public string DataType { get; }
        public bool IsWritable { get; }
        public string Category { get; }      // Temp/Static/Global/Input/Output/Precondition/Resource/Control/Time/Host
        public bool IsFunction { get; }
        public bool IsArray { get; }
        public int LowerBound { get; }
        public int UpperBound { get; }
        /// <summary>1 for a list, 2 for a table (m[i, j]); 0 for scalars.</summary>
        public int Rank { get; }
        /// <summary>True when the declaration supplies an initial value (or the symbol
        /// is inherently initialised, like aspects and Cycle).</summary>
        public bool HasInitialValue { get; }

        public Symbol(string name, string dataType, bool isWritable, string category, bool isFunction = false,
            bool isArray = false, int lowerBound = 0, int upperBound = 0, bool hasInitialValue = true, int rank = 1)
        {
            Name = name; DataType = dataType; IsWritable = isWritable; Category = category; IsFunction = isFunction;
            IsArray = isArray; LowerBound = lowerBound; UpperBound = upperBound; HasInitialValue = hasInitialValue;
            Rank = isArray ? (rank == 2 ? 2 : 1) : 0;
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
