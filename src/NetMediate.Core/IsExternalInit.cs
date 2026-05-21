#if NETSTANDARD2_0 || NETSTANDARD2_1
#pragma warning disable IDE0130
using System.Diagnostics.CodeAnalysis;
namespace System.Runtime.CompilerServices;
[ExcludeFromCodeCoverage]
internal sealed class IsExternalInit { }

[ExcludeFromCodeCoverage]
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class CallerArgumentExpressionAttribute(string parameterName) : Attribute
{
    public string ParameterName { get; } = parameterName;
}
#pragma warning restore IDE0130
#endif
