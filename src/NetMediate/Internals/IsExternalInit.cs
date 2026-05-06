#if NETSTANDARD2_0 || NETSTANDARD2_1
#pragma warning disable IDE0130
namespace System.Runtime.CompilerServices;

internal sealed class IsExternalInit { }

[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class CallerArgumentExpressionAttribute(string parameterName) : Attribute
{
    public string ParameterName { get; } = parameterName;
}
#pragma warning restore IDE0130
#endif
