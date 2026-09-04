namespace NetShield.Inventory.Credentials;

/// <summary>What one run of the key rotation did.</summary>
/// <param name="ActiveKeyId">The key everything was moved on to.</param>
/// <param name="Examined">How many profiles were looked at, live and removed alike.</param>
/// <param name="Rewrapped">
/// How many were moved. Zero on a second run over the same estate, which is what makes the
/// command safe to repeat and is how an operator knows a retired key is free.
/// </param>
public sealed record CredentialRewrapReport(string ActiveKeyId, int Examined, int Rewrapped);
