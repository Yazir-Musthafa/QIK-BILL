namespace Nexora.Domain.Common;

/// <summary>
/// Raised when a business rule is violated. Messages are written for end users and must not
/// contain secrets or internal identifiers.
/// </summary>
public sealed class DomainException(string message) : Exception(message);
