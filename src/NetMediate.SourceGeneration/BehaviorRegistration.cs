namespace NetMediate.SourceGeneration;

internal readonly record struct BehaviorRegistration(
    string ResilienceTemplate,
    string DiagnosticTemplate,
    string AssemblyName,
    string InterfaceName,
    string MessageFqn,
    string ResponseFqn,
    bool HasDiagnostics,
    bool HasResilience
);
