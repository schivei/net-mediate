#if NETSTANDARD2_0 || NETSTANDARD2_1
namespace System.Runtime.CompilerServices;

internal class IsExternalInit { }

[System.AttributeUsage(System.AttributeTargets.Parameter, Inherited = false)]
internal sealed class CallerArgumentExpressionAttribute(string parameterName) : System.Attribute
{
    public string ParameterName { get; } = parameterName;
}
#endif
