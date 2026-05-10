namespace NetMediate.SourceGeneration;

internal readonly record struct BehaviorRegistration(
    string? InterfaceName,
    string? MessageFqn,
    string? ResponseFqn,
    bool HasDiagnostics,
    bool HasResilience,
    System.Collections.Generic.Dictionary<string, bool> DiagnosticsBehaviors,
    System.Collections.Generic.Dictionary<string, bool> ResilienceBehaviors
);
