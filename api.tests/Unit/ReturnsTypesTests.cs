using System.Reflection;
using System.Text.RegularExpressions;
using Finance.Api.Domain.Returns;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 008's definition of done: no binary floating point anywhere in the returns module,
/// locals included. Reflection covers declared members; the source scan covers locals,
/// casts and <c>System.Math</c>'s floating-point functions.
/// </summary>
public sealed partial class ReturnsTypesTests
{
    private static readonly string[] Namespaces = ["Finance.Api.Domain.Returns", "Finance.Api.Application.Returns"];

    private static readonly string[] Folders = [Path.Combine("Domain", "Returns"), Path.Combine("Application", "Returns")];

    [Fact]
    public void No_returns_type_declares_a_double_or_a_float()
    {
        const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        var types = typeof(Rate).Assembly.GetTypes().Where(type => Namespaces.Contains(type.Namespace)).ToList();

        Assert.Contains(typeof(BenchmarkAccumulator), types);
        var offenders = types
            .SelectMany(type => type.GetProperties(Declared).Select(member => (type, member.Name, member.PropertyType))
                .Concat(type.GetFields(Declared).Select(member => (type, member.Name, member.FieldType))))
            .Where(member => IsBinaryFloatingPoint(member.Item3))
            .Select(member => $"{member.type.FullName}.{member.Name}");

        Assert.Empty(offenders);
    }

    [Fact]
    public void No_returns_source_file_mentions_a_floating_point_type_outside_comments()
    {
        var files = Folders.SelectMany(folder =>
                Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "api", folder), "*.cs", SearchOption.AllDirectories))
            .Append(Path.Combine(RepositoryRoot(), "api", "Endpoints", "ReturnsEndpoints.cs"))
            .ToList();

        Assert.Contains(files, file => file.EndsWith("DecimalMath.cs", StringComparison.Ordinal));
        Assert.Contains(files, file => file.EndsWith("ReturnsQueries.cs", StringComparison.Ordinal));
        var offenders = files
            .SelectMany(file => File.ReadLines(file).Select((line, number) => (file, number, code: Comment().Replace(line, ""))))
            .Where(line => FloatingPoint().IsMatch(line.code))
            .Select(line => $"{Path.GetFileName(line.file)}:{line.number + 1}: {line.code.Trim()}");

        Assert.Empty(offenders);
    }

    [GeneratedRegex(@"//.*$")]
    private static partial Regex Comment();

    [GeneratedRegex(@"\b(double|float|Half|Single|Double)\b|\bMath\.(Pow|Exp|Log|Log10|Sqrt|Cbrt)\b|\d(d|f|D|F)\b")]
    private static partial Regex FloatingPoint();

    private static bool IsBinaryFloatingPoint(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsArray)
        {
            underlying = underlying.GetElementType()!;
        }

        return underlying == typeof(double) || underlying == typeof(float) || underlying == typeof(Half);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FinanceApp.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("FinanceApp.slnx not found above the test binaries.");
    }
}
