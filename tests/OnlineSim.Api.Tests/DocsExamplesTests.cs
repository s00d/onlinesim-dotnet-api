#if NET
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace OnlineSim.Api.Tests;

/// <summary>
/// Compiles the C# examples of README.md and docs/content/*.md against the SDK, so the docs cannot drift from the API.
/// A ```csharp block that declares a class is compiled as a file; one that uses <c>client</c>, <c>await</c> or a
/// <c>using</c> directive is an example and is compiled as the body of an async method (its using directives hoisted,
/// undeclared <c>client</c> / <c>tzid</c> declared); the other blocks are signatures and are skipped.
/// </summary>
public class DocsExamplesTests
{
    private static readonly Regex Block = new Regex("```csharp[^\\n]*\\n(.*?)```", RegexOptions.Singleline);
    private static readonly Regex UsingDirective = new Regex(@"^using [A-Za-z0-9_.]+;\s*$", RegexOptions.Multiline);
    private static readonly Regex ClassDeclaration = new Regex(@"^\s*(public |internal )?(sealed |static )?class \w+", RegexOptions.Multiline);
    private static readonly Regex Example = new Regex(@"\bclient\b|\bawait\b|^using ", RegexOptions.Multiline);
    private static readonly Regex DeclaresClient = new Regex(@"\b(var|OnlineSimClient)\s+client\s*=");
    private static readonly Regex DeclaresTzid = new Regex(@"\b(var|long)\s+tzid\s*=|var\s*\([^)]*\btzid\b");

    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using OnlineSim.Api;
        """;

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OnlineSim.Api.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    internal static IEnumerable<(string Source, string Code)> Blocks()
    {
        var root = Root();
        var files = new[] { Path.Combine(root, "README.md") }
            .Concat(Directory.GetFiles(Path.Combine(root, "docs", "content"), "*.md").OrderBy(f => f, StringComparer.Ordinal));
        foreach (var file in files)
        {
            var i = 0;
            foreach (Match m in Block.Matches(File.ReadAllText(file)))
            {
                yield return ($"{Path.GetRelativePath(root, file)}#{i++}", m.Groups[1].Value);
            }
        }
    }

    private static string? Wrap(string code, int index)
    {
        if (ClassDeclaration.IsMatch(code)) return Usings + "\nusing Xunit;\n" + code;
        if (!Example.IsMatch(code)) return null;

        var usings = string.Join("\n", UsingDirective.Matches(code).Select(m => m.Value.Trim()));
        var body = UsingDirective.Replace(code, "");
        var prelude = new StringBuilder();
        if (!DeclaresClient.IsMatch(body)) prelude.AppendLine("OnlineSimClient client = null!;");
        if (!DeclaresTzid.IsMatch(body)) prelude.AppendLine("long tzid = 0;");
        return $$"""
            {{Usings}}
            {{usings}}

            public static class DocsExample{{index}}
            {
                public static async Task Run()
                {
                    {{prelude}}
                    {{body}}
                    await Task.CompletedTask;
                }
            }
            """;
    }

    [Fact]
    public void DocsExamplesCompileAgainstTheSdk()
    {
        var trees = new List<SyntaxTree>();
        var index = 0;
        foreach (var (source, code) in Blocks())
        {
            var text = Wrap(code, index++);
            if (text != null) trees.Add(CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.CSharp12), path: source));
        }
        Assert.True(trees.Count > 20, $"only {trees.Count} examples found");

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(new[] { typeof(OnlineSimClient).Assembly.Location, typeof(Assert).Assembly.Location, typeof(FactAttribute).Assembly.Location })
            .Distinct()
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "DocsExamples",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Location.SourceTree?.FilePath}: {d.GetMessage()}\n{d.Location.SourceTree}")
            .ToList();
        Assert.Empty(errors);
    }
}
#endif
