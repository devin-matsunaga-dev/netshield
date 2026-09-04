using Microsoft.Extensions.Options;

namespace NetShield.Platform.Cryptography;

/// <summary>
/// Fails the host at startup when the key ring is missing or malformed, naming what is wrong.
/// </summary>
/// <remarks>
/// Registered with <c>ValidateOnStart</c> on purpose. The alternative is a process that starts,
/// answers health checks, and refuses the first request that needs a credential — which reads to
/// an operator as an inventory bug rather than a missing key.
/// </remarks>
internal sealed class EnvelopeEncryptionOptionsValidator : IValidateOptions<EnvelopeEncryptionOptions>
{
    public ValidateOptionsResult Validate(string? name, EnvelopeEncryptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<string> problems = KeyEncryptionKeyRing.Validate(options);

        return problems.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(problems);
    }
}
