#if NETSTANDARD2_0 || NETSTANDARD2_1
#pragma warning disable IDE0130
namespace System.Runtime.CompilerServices;

#pragma warning disable S2094
internal sealed class IsExternalInit { }
#pragma warning restore S2094

[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class CallerArgumentExpressionAttribute(string parameterName) : Attribute
{
    public string ParameterName { get; } = parameterName;
}
#pragma warning restore IDE0130
#endif
