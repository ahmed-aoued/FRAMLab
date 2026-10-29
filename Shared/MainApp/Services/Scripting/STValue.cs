using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FRAMLab.Shared.MainApp.Models;

namespace FRAMLab.Shared.MainApp.Services.Scripting
{
    /// <summary>Runtime value types for the interpreter.</summary>
    public enum STValueType { Bool, Int, Real, String, Time, Array, Uninitialized, Void }

    /// <summary>
    /// Array value of rank 1 (a list) or rank 2 (a rectangular table). Indexes start at 0 and
    /// there are no declared bounds: writing an element grows the array (gaps become typed
    /// zeros); a rank-2 array grows rows and columns together so every row has the same width.
    /// At most <see cref="MaxLength"/> elements in total. Reading an element that was never
    /// written is an error at runtime. Rank-2 items are the rows (rank-1 arrays).
    /// </summary>
    public sealed class STArray
    {
        /// <summary>Safety cap (total elements) so a script cannot allocate unbounded memory.</summary>
        public const int MaxLength = 100_000;

        public ScriptVarType ElementType { get; }
        public int Rank { get; }
        public List<STValue> Items { get; }

        public STArray(ScriptVarType elementType, IEnumerable<STValue>? items = null, int rank = 1)
        {
            ElementType = elementType;
            Rank = rank == 2 ? 2 : 1;
            Items = items?.ToList() ?? new List<STValue>();
        }

        /// <summary>Elements (rank 1) or rows (rank 2).</summary>
        public int Length => Items.Count;
        /// <summary>Columns of a rank-2 array (every row has this width).</summary>
        public int Width => Rank == 2 && Items.Count > 0 ? Items[0].AsArray()!.Length : 0;

        /// <summary>A position a write may use (0 .. MaxLength-1).</summary>
        public static bool IsValidIndex(long index) => index >= 0 && index < MaxLength;

        /// <summary>The element exists (it was written or initialized).</summary>
        public bool Has(long index) => Rank == 1 && index >= 0 && index < Items.Count;
        public bool Has(long row, long col) => Rank == 2 && row >= 0 && row < Items.Count && col >= 0 && col < Width;

        public STValue Get(long index) => Has(index) ? Items[(int)index] : STValue.Void;
        public STValue Get(long row, long col) => Has(row, col) ? Items[(int)row].AsArray()!.Items[(int)col] : STValue.Void;

        /// <summary>Write element <paramref name="index"/>, growing the array as needed. Returns false when
        /// the index is negative or would overflow <see cref="MaxLength"/>.</summary>
        public bool Set(long index, STValue value)
        {
            if (Rank != 1 || !IsValidIndex(index)) return false;
            while (Items.Count <= index) Items.Add(ScriptArrayFactory.ZeroOf(ElementType));
            Items[(int)index] = value;
            return true;
        }

        /// <summary>Write cell (row, col) of a rank-2 array, adding rows and columns as needed so the
        /// table stays rectangular. Returns false when an index is negative or the table would exceed
        /// <see cref="MaxLength"/> cells.</summary>
        public bool Set(long row, long col, STValue value)
        {
            if (Rank != 2 || row < 0 || col < 0) return false;
            var rows = Math.Max(Items.Count, row + 1);
            var width = Math.Max(Width, col + 1);
            if (rows * width > MaxLength) return false;
            foreach (var r in Items) PadRow(r.AsArray()!, (int)width);
            while (Items.Count < rows)
            {
                var newRow = new STArray(ElementType);
                PadRow(newRow, (int)width);
                Items.Add(STValue.FromArray(newRow));
            }
            Items[(int)row].AsArray()!.Items[(int)col] = value;
            return true;
        }

        private void PadRow(STArray row, int width)
        {
            while (row.Items.Count < width) row.Items.Add(ScriptArrayFactory.ZeroOf(ElementType));
        }

        /// <summary>Build a rectangular rank-2 array from rows of possibly different lengths (short rows are padded).</summary>
        public static STArray FromRows(ScriptVarType elementType, IEnumerable<IEnumerable<STValue>> rows)
        {
            var table = new STArray(elementType, rank: 2);
            var list = rows.Select(r => r.ToList()).ToList();
            var width = list.Count == 0 ? 0 : list.Max(r => r.Count);
            foreach (var r in list)
            {
                var row = new STArray(elementType, r);
                table.PadRow(row, width);
                table.Items.Add(STValue.FromArray(row));
            }
            return table;
        }

        public STArray Clone() => Rank == 1
            ? new(ElementType, Items)
            : new(ElementType, Items.Select(r => STValue.FromArray(r.AsArray()!.Clone())), 2);

        public string Describe() => Rank == 1
            ? (Length == 0 ? "it is empty" : $"it has {Length} element(s), 0..{Length - 1}")
            : (Length == 0 ? "it is empty" : $"it has {Length} row(s) x {Width} column(s)");
    }

    /// <summary>A typed runtime value. Numbers are kept as long/double and promoted on demand.</summary>
    public readonly struct STValue
    {
        public STValueType Type { get; }
        private readonly object? raw;

        private STValue(STValueType type, object? raw) { Type = type; this.raw = raw; }

        public static STValue FromBool(bool b) => new(STValueType.Bool, b);
        public static STValue FromInt(long i) => new(STValueType.Int, i);
        public static STValue FromReal(double d) => new(STValueType.Real, d);
        public static STValue FromString(string s) => new(STValueType.String, s);
        public static STValue FromTime(TimeSpan t) => new(STValueType.Time, t);
        public static STValue FromArray(STArray array) => new(STValueType.Array, array);
        /// <summary>A declared variable for which no value has been assigned yet.</summary>
        public static STValue Uninitialized => new(STValueType.Uninitialized, null);
        public static STValue Void => new(STValueType.Void, null);

        public STArray? AsArray() => Type == STValueType.Array ? (STArray?)raw : null;

        public bool AsBool() => Type switch
        {
            STValueType.Bool => (bool)raw!,
            STValueType.Int => (long)raw! != 0,
            STValueType.Real => (double)raw! != 0.0,
            _ => false,
        };
        public long AsInt() => Type switch
        {
            STValueType.Int => (long)raw!,
            STValueType.Real => (long)(double)raw!,
            STValueType.Bool => (bool)raw! ? 1 : 0,
            _ => 0,
        };
        public double AsReal() => Type switch
        {
            STValueType.Real => (double)raw!,
            STValueType.Int => (long)raw!,
            STValueType.Bool => (bool)raw! ? 1.0 : 0.0,
            STValueType.Time => ((TimeSpan)raw!).TotalMilliseconds,
            _ => 0.0,
        };
        public string AsString() => Type switch
        {
            STValueType.String => (string)raw!,
            STValueType.Bool => (bool)raw! ? "TRUE" : "FALSE",
            STValueType.Int => ((long)raw!).ToString(CultureInfo.InvariantCulture),
            STValueType.Real => ((double)raw!).ToString(CultureInfo.InvariantCulture),
            STValueType.Time => $"T#{((TimeSpan)raw!).TotalMilliseconds}ms",
            STValueType.Array => "[" + string.Join(",", ((STArray)raw!).Items.Select(i => i.AsString())) + "]",
            _ => "",
        };
        public TimeSpan AsTime() => Type == STValueType.Time ? (TimeSpan)raw! : TimeSpan.FromMilliseconds(AsReal());

        public bool IsNumeric => Type is STValueType.Int or STValueType.Real;
        public override string ToString() => AsString();
    }
}
