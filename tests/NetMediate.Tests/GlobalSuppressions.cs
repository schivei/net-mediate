using System.Diagnostics.CodeAnalysis;

[assembly: GenDICoveration(false)]
[assembly: ExcludeFromCodeCoverage]

[assembly: SuppressMessage(
    "CodeQuality",
    "IDE0076:Invalid global 'SuppressMessageAttribute'",
    Justification = "Suppressing IDE0076 to keep the suppression file structure intact for future use."
)]
