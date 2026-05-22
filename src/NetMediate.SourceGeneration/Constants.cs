using System.Globalization;

namespace NetMediate.SourceGeneration;

internal static class Constants
{
    public const string PackName = "NetMediate";
    public const string CoverageTpl = "[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]\n";
    public const char TypedExtKeySeparator = '\u001F';
    public const string CoverageToken = "{{Coverage}}";
    public const string AssemblyNamespaceToken = "{{AssemblyNamespace}}";
    public const string TypedExtensionsToken = "{{TypedExtensions}}";
    public const string GlobalNamespace = "global::";
    public const string RequestHandlerIfce = "IRequestHandler";
    public const string StreamHandlerIfce = "IStreamHandler";
    public const string CommandHandlerIfce = "ICommandHandler";
    public const string NotificationHandlerIfce = "INotificationHandler";
    public const string RequestName = "Request";
    public const string StreamName = "Stream";
    public const string CommandName = "Command";
    public const string NotificationName = "Notification";
    public const string ImplementationTypeToken = "{{ImplementationType}}";
    public const string OrderToken = "{{Order}}";
    public const string RandomNameToken = "{{RandomName}}";
    public const string BehaviorNameToken = "{{BehaviorName}}";
    public const string BehaviorAbstractionToken = "{{BehaviorAbstraction}}";

    public static readonly string TemplateResourceName =
        $"{typeof(Constants).Namespace}.NetMediateGeneratedDI.template";
    public static readonly string TypedExtensionsTemplateResourceName =
        $"{typeof(Constants).Namespace}.NetMediateTypedExtensions.template";
    public static readonly string TemplateBehaviorResourceName =
        $"{typeof(Constants).Namespace}.NetMediateFrameworkBehavior.template";

    public const string TelemetryCommandBehaviorClassName = "TelemetryCommandBehavior";
    public const string CircuitBreakerCommandBehaviorClassName = "CircuitBreakerCommandBehavior";
    public const string RetryCommandBehaviorClassName = "RetryCommandBehavior";
    public const string TimeoutCommandBehaviorClassName = "TimeoutCommandBehavior";

    public const string TelemetryNotificationBehaviorClassName = "TelemetryNotificationBehavior";
    public const string CircuitBreakerNotificationBehaviorClassName = "CircuitBreakerNotificationBehavior";
    public const string RetryNotificationBehaviorClassName = "RetryNotificationBehavior";
    public const string TimeoutNotificationBehaviorClassName = "TimeoutNotificationBehavior";

    public const string TelemetryRequestBehaviorClassName = "TelemetryRequestBehavior";
    public const string CircuitBreakerRequestBehaviorClassName = "CircuitBreakerRequestBehavior";
    public const string RetryRequestBehaviorClassName = "RetryRequestBehavior";
    public const string TimeoutRequestBehaviorClassName = "TimeoutRequestBehavior";

    public const string TelemetryStreamBehaviorClassName = "TelemetryStreamBehavior";
    public const string CircuitBreakerStreamBehaviorClassName = "CircuitBreakerStreamBehavior";
    public const string RetryStreamBehaviorClassName = "RetryStreamBehavior";
    public const string TimeoutStreamBehaviorClassName = "TimeoutStreamBehavior";
    private const int TelemetryBehaviorOrder = int.MinValue;
    private const int CircuitBreakerBehaviorOrder = int.MinValue + 1;
    private const int RetryBehaviorOrder = int.MinValue + 2;
    private const int TimeoutBehaviorOrder = int.MinValue + 3;

    private static string RandomNameFrom(string interfaceName, string behaviorName, out string name)
    {
        var raw = interfaceName + behaviorName;
        var safe = ToSafeIdentifier(raw);
        name = $"{safe}_{ComputeStableHash(raw)}";
        return name;
    }

    private static string ToSafeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "_Decorator";

        var chars = new char[value.Length];
        var index = 0;

        foreach (var c in value)
        {
            chars[index++] = char.IsLetterOrDigit(c) || c == '_' ? c : '_';
        }

        var sanitized = new string(chars, 0, value.Length);
        return char.IsDigit(sanitized[0]) ? $"_{sanitized}" : sanitized;
    }

    private static string ComputeStableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return hash.ToString("X8", CultureInfo.InvariantCulture);
        }
    }

    private static string GetBehaviorConcretClass(BehaviorRegistration registration, int order, string behaviorName, string behaviorAbstration, out string randomName) =>
        registration.Template.Replace(AssemblyNamespaceToken, registration.AssemblyName)
                .Replace(ImplementationTypeToken, registration.InterfaceName)
                .Replace(OrderToken, order.ToString(CultureInfo.InvariantCulture))
                .Replace(RandomNameToken, RandomNameFrom(registration.InterfaceName, behaviorName, out randomName))
                .Replace(BehaviorAbstractionToken, behaviorAbstration);

    private static string GetTelemetryCommandClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}>" },
            TelemetryBehaviorOrder,
            TelemetryCommandBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{TelemetryCommandBehaviorClassName}<{registration.MessageFqn}>",
            out randomName
        );

    private static string GetTelemetryNotificationClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}>" },
            TelemetryBehaviorOrder,
            TelemetryNotificationBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{TelemetryNotificationBehaviorClassName}<{registration.MessageFqn}>",
            out randomName
        );

    private static string GetTelemetryRequestClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}, {registration.ResponseFqn}>" },
            TelemetryBehaviorOrder,
            TelemetryRequestBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{TelemetryRequestBehaviorClassName}<{registration.MessageFqn}, {registration.ResponseFqn}>",
            out randomName
        );

    private static string GetTelemetryStreamClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}, {registration.ResponseFqn}>" },
            TelemetryBehaviorOrder,
            TelemetryStreamBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{TelemetryStreamBehaviorClassName}<{registration.MessageFqn}, {registration.ResponseFqn}>",
            out randomName
        );

    private static string GetCircuitBreakerCommandClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}>" },
            CircuitBreakerBehaviorOrder,
            CircuitBreakerCommandBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{CircuitBreakerCommandBehaviorClassName}<{registration.MessageFqn}>",
            out randomName
        );

    private static string GetCircuitBreakerNotificationClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}>" },
            CircuitBreakerBehaviorOrder,
            CircuitBreakerNotificationBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{CircuitBreakerNotificationBehaviorClassName}<{registration.MessageFqn}>",
            out randomName
        );

    private static string GetCircuitBreakerRequestClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}, {registration.ResponseFqn}>" },
            CircuitBreakerBehaviorOrder,
            CircuitBreakerRequestBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{CircuitBreakerRequestBehaviorClassName}<{registration.MessageFqn}, {registration.ResponseFqn}>",
            out randomName
        );

    private static string GetCircuitBreakerStreamClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}, {registration.ResponseFqn}>" },
            CircuitBreakerBehaviorOrder,
            CircuitBreakerStreamBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{CircuitBreakerStreamBehaviorClassName}<{registration.MessageFqn}, {registration.ResponseFqn}>",
            out randomName
        );

    private static string GetRetryCommandClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}>" },
            RetryBehaviorOrder,
            RetryCommandBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{RetryCommandBehaviorClassName}<{registration.MessageFqn}>",
            out randomName
        );

    private static string GetRetryNotificationClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}>" },
            RetryBehaviorOrder,
            RetryNotificationBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{RetryNotificationBehaviorClassName}<{registration.MessageFqn}>",
            out randomName
        );

    private static string GetRetryRequestClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}, {registration.ResponseFqn}>" },
            RetryBehaviorOrder,
            RetryRequestBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{RetryRequestBehaviorClassName}<{registration.MessageFqn}, {registration.ResponseFqn}>",
            out randomName
        );

    private static string GetRetryStreamClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}, {registration.ResponseFqn}>" },
            RetryBehaviorOrder,
            RetryStreamBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{RetryStreamBehaviorClassName}<{registration.MessageFqn}, {registration.ResponseFqn}>",
            out randomName
        );

    private static string GetTimeoutCommandClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}>" },
            TimeoutBehaviorOrder,
            TimeoutCommandBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{TimeoutCommandBehaviorClassName}<{registration.MessageFqn}>",
            out randomName
        );

    private static string GetTimeoutNotificationClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}>" },
            TimeoutBehaviorOrder,
            TimeoutNotificationBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{TimeoutNotificationBehaviorClassName}<{registration.MessageFqn}>",
            out randomName
        );

    private static string GetTimeoutRequestClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}, {registration.ResponseFqn}>" },
            TimeoutBehaviorOrder,
            TimeoutRequestBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{TimeoutRequestBehaviorClassName}<{registration.MessageFqn}, {registration.ResponseFqn}>",
            out randomName
        );

    private static string GetTimeoutStreamClass(BehaviorRegistration registration, out string randomName) =>
        GetBehaviorConcretClass(
            registration with { InterfaceName = $"{GlobalNamespace}{PackName}.{registration.InterfaceName}<{registration.MessageFqn}, {registration.ResponseFqn}>" },
            TimeoutBehaviorOrder,
            TimeoutStreamBehaviorClassName,
            $"{GlobalNamespace}{PackName}.{TimeoutStreamBehaviorClassName}<{registration.MessageFqn}, {registration.ResponseFqn}>",
            out randomName
        );

    public static IEnumerable<(string classDefinition, string className)> GetBehaviorClasses(this BehaviorRegistration registration)
    {
        if (registration.HasDiagnostics)
        {
            yield return registration.InterfaceName switch
            {
                NotificationHandlerIfce => (GetTelemetryNotificationClass(registration, out var name), name),
                RequestHandlerIfce => (GetTelemetryRequestClass(registration, out var name), name),
                StreamHandlerIfce => (GetTelemetryStreamClass(registration, out var name), name),
                _ => (GetTelemetryCommandClass(registration, out var name), name)
            };
        }

        if (!registration.HasResilience)
            yield break;

        if (registration.InterfaceName is NotificationHandlerIfce)
        {
            yield return (GetCircuitBreakerNotificationClass(registration, out var nameCB), nameCB);
            yield return (GetRetryNotificationClass(registration, out var nameR), nameR);
            yield return (GetTimeoutNotificationClass(registration, out var nameT), nameT);
        }

        if (registration.InterfaceName is RequestHandlerIfce)
        {
            yield return (GetCircuitBreakerRequestClass(registration, out var nameCB), nameCB);
            yield return (GetRetryRequestClass(registration, out var nameR), nameR);
            yield return (GetTimeoutRequestClass(registration, out var nameT), nameT);
        }

        if (registration.InterfaceName is StreamHandlerIfce)
        {
            yield return (GetCircuitBreakerStreamClass(registration, out var nameCB), nameCB);
            yield return (GetRetryStreamClass(registration, out var nameR), nameR);
            yield return (GetTimeoutStreamClass(registration, out var nameT), nameT);
        }

        if (registration.InterfaceName is CommandHandlerIfce)
        {
            yield return (GetCircuitBreakerCommandClass(registration, out var name), name);
            yield return (GetRetryCommandClass(registration, out var nameR), nameR);
            yield return (GetTimeoutCommandClass(registration, out var nameT), nameT);
        }
    }
}
