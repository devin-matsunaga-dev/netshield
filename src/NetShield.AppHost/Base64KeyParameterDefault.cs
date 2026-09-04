using System.Security.Cryptography;

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;

namespace NetShield.AppHost;

/// <summary>
/// Generates a cryptographic key of exactly <paramref name="lengthInBytes"/> random bytes,
/// base64-encoded — the shape <c>Security:CredentialEncryption:Keys</c> requires.
/// </summary>
/// <remarks>
/// <para>
/// Aspire's own <see cref="GenerateParameterDefault"/> produces a password: a string of random
/// characters at a requested length and character-class mix. That is right for a first-run
/// administrator password and wrong for a key-encryption key, which is 256 bits of entropy and
/// not a string somebody types. Deriving a key from such a string with a KDF would be
/// cryptographically respectable and would still not add entropy the string never had — so the
/// configuration contract stays "this value <em>is</em> a 256-bit key", and this generates one.
/// </para>
/// <para>
/// Used with <c>persist: true</c>, so the value is written once to the AppHost's user-secrets
/// store and survives a restart. That matters more here than anywhere else in the file: a KEK
/// that changed between runs would leave every credential in the development database
/// permanently unreadable.
/// </para>
/// </remarks>
/// <param name="lengthInBytes">How many random bytes to generate before encoding.</param>
internal sealed class Base64KeyParameterDefault(int lengthInBytes) : ParameterDefault
{
    public override string GetDefaultValue() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(lengthInBytes));

    /// <summary>
    /// Writes no default into a deployment manifest, deliberately.
    /// </summary>
    /// <remarks>
    /// <see cref="GenerateParameterDefault"/> writes a <c>generate</c> block, which tells whatever
    /// reads the manifest to make the value itself. That is the wrong promise for this parameter:
    /// a key-encryption key must not be produced by the thing starting the containers and left
    /// beside the database it opens. Emitting nothing says the deployment has to supply it — from
    /// a secret store, a mounted file, or a KMS — which is the truth, and is the gap recorded in
    /// STATUS.md rather than one papered over here.
    /// </remarks>
    public override void WriteToManifest(ManifestPublishingContext context)
    {
        // Intentionally empty. See the remarks above.
    }
}
