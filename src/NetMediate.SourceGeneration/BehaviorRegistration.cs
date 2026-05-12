namespace NetMediate.SourceGeneration;

internal readonly record struct BehaviorRegistration(
    string? InterfaceName,
    string? MessageFqn,
    string? ResponseFqn,
    bool HasDiagnostics,
    bool HasResilience,
    Dictionary<string, bool> DiagnosticsBehaviors,
    Dictionary<string, bool> ResilienceBehaviors
);
