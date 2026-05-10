namespace NetMediate.SourceGeneration;

internal static class Constants
{
    public const string PackName = "NetMediate";
    public const string CoverageTpl = "[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]\n";
    public const char TypedExtKeySeparator = '\u001F';
    public const string CoverageToken = "{{Coverage}}";
    public const string NotifierToken = "{{Notifier}}";
    public const string RegistrationsToken = "{{Registrations}}";
    public const string FrameworkBehaviorsToken = "{{FrameworkBehaviors}}";
    public const string AssemblyNamespaceToken = "{{AssemblyNamespace}}";
    public const string TypedExtensionsToken = "{{TypedExtensions}}";
    public const string BehaviorRegistrationsToken = "{{BehaviorRegistrations}}";
    public const string PipelineRegistrationsToken = "{{PipelineRegistrations}}";
    public const string ExecutorTypesToken = "{{ExecutorTypes}}";
    public const string GlobalNamespace = "global::";
    public const string RequestHandlerIfce = "IRequestHandler";
    public const string StreamHandlerIfce = "IStreamHandler";
    public const string CommandHandlerIfce = "ICommandHandler";
    public const string NotificationHandlerIfce = "INotificationHandler";
    public static readonly string TemplateResourceName =
        $"{typeof(Constants).Namespace}.NetMediateGeneratedDI.template";
    public static readonly string TypedExtensionsTemplateResourceName =
        $"{typeof(Constants).Namespace}.NetMediateTypedExtensions.template";
    public static readonly string PipelineExecutorsTemplateResourceName =
        $"{typeof(Constants).Namespace}.NetMediatePipelineExecutors.template";
}
