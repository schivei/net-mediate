#if NETSTANDARD2_0 || NETSTANDARD2_1
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace System.Runtime.CompilerServices;

internal class IsExternalInit { }

[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
internal sealed class CallerArgumentExpressionAttribute(string parameterName) : Attribute
{
    public string ParameterName { get; } = parameterName;
}
#pragma warning restore IDE0130 // Namespace does not match folder structure
#endif
