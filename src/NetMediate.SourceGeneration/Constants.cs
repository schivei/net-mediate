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
    public const string ImplementationTypeSummaryToken = "{{ImplementationTypeSummary}}";
    public const string OrderToken = "{{Order}}";
    public const string RandomNameToken = "{{RandomName}}";
    public const string BehaviorNameToken = "{{BehaviorName}}";
    public const string BehaviorAbstractionToken = "{{BehaviorAbstraction}}";
    public const string BehaviorsDeclarationToken = "{{Behaviors}}";
    public const string OptionsTypeToken = "{{OptionsType}}";
    public const string AddNetMediateResilienceDIToken = "{{AddNetMediateResilienceDI}}";

    public static readonly string TemplateResourceName =
        $"{typeof(Constants).Namespace}.NetMediateGeneratedDI.template";
    public static readonly string TypedExtensionsTemplateResourceName =
        $"{typeof(Constants).Namespace}.NetMediateTypedExtensions.template";
    public static readonly string TemplateDiagnosticBehaviorResourceName =
        $"{typeof(Constants).Namespace}.NetMediateDiagnosticsBehavior.template";
    public static readonly string TemplateResilienceBehaviorResourceName =
        $"{typeof(Constants).Namespace}.NetMediateResilienceBehavior.template";

    public const string CircuitBreakerOptionsClassName = "CircuitBreakerBehaviorOptions";
    public const string RetryOptionsClassName = "RetryBehaviorOptions";
    public const string TimeoutOptionsClassName = "TimeoutBehaviorOptions";

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
}
