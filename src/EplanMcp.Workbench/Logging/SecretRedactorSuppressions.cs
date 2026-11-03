using System.Diagnostics.CodeAnalysis;

// The linked implementation must also compile for Contracts' netstandard2.0 target, which lacks these newer APIs.
[assembly: SuppressMessage("Maintainability", "CA1510:Use ArgumentNullException throw helper", Scope = "type", Target = "~T:EplanMcp.Contracts.Security.SecretRedactor", Justification = "Keep the shared implementation compatible with netstandard2.0.")]
[assembly: SuppressMessage("Usage", "CA2249:Consider using String.Contains instead of String.IndexOf", Scope = "type", Target = "~T:EplanMcp.Contracts.Security.SecretRedactor", Justification = "Keep the shared implementation compatible with netstandard2.0.")]
