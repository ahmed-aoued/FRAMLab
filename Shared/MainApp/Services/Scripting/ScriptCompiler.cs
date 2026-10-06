using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace FMV_OG.Shared.MainApp.Services.Scripting
{
    /// <summary>Result of compiling a script: the AST plus all diagnostics.</summary>
    public sealed class CompiledScript
    {
        public ProgramNode? Program { get; }
        public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; }
        public SymbolTable Symbols { get; }
        public bool HasErrors => Diagnostics.Any(d => d.Severity == "Error");

        public CompiledScript(ProgramNode? program, IReadOnlyList<ScriptDiagnostic> diagnostics, SymbolTable symbols)
        {
            Program = program; Diagnostics = diagnostics; Symbols = symbols;
        }
    }

    /// <summary>Lex + parse + semantic-analyze a script source against a symbol table.</summary>
    public static class ScriptCompiler
    {
        public static CompiledScript Compile(string source, SymbolTable symbols, ILogger? logger = null)
        {
            var lexer = new STLexer(source);
            var tokens = lexer.Tokenize();

            var parser = new STParser(tokens, logger);
            var program = parser.ParseProgram();

            var diagnostics = new List<ScriptDiagnostic>();
            diagnostics.AddRange(lexer.Errors);
            diagnostics.AddRange(parser.Diagnostics);

            if (!diagnostics.Any(d => d.Severity == "Error"))
            {
                var analyzer = new SemanticAnalyzer(symbols);
                diagnostics.AddRange(analyzer.Analyze(program));
            }

            // order diagnostics by position for display
            diagnostics = diagnostics
                .OrderBy(d => d.StartLine).ThenBy(d => d.StartColumn)
                .ToList();

            return new CompiledScript(program, diagnostics, symbols);
        }
    }
}
