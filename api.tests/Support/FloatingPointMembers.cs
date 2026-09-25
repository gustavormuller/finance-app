using System.Reflection;

namespace Finance.Api.Tests;

/// <summary>
/// Principle 4 as a reflection check: money is <c>decimal</c>, so no declared property or
/// field of these types may be binary floating point, nullable or array included.
/// </summary>
internal static class FloatingPointMembers
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>Every offending member, as <c>Type.Member</c>; empty when there is none.</summary>
    public static IReadOnlyList<string> In(IEnumerable<Type> types) =>
        types
            .SelectMany(type => type.GetProperties(Declared).Select(member => (type, member.Name, member.PropertyType))
                .Concat(type.GetFields(Declared).Select(member => (type, member.Name, member.FieldType))))
            .Where(member => IsBinaryFloatingPoint(member.Item3))
            .Select(member => $"{member.type.FullName}.{member.Name}")
            .ToList();

    private static bool IsBinaryFloatingPoint(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsArray)
        {
            underlying = underlying.GetElementType()!;
        }

        return underlying == typeof(double) || underlying == typeof(float) || underlying == typeof(Half);
    }
}
