using System.Security.Cryptography;
using System.Text;

using Konscious.Security.Cryptography;

using Microsoft.Extensions.Options;

namespace NetShield.Identity.Passwords;

/// <summary>
/// Argon2id, as WP-0.4 requires, with the work factors from <see cref="PasswordHashingOptions"/>
/// and a per-password 128-bit salt.
/// </summary>
/// <remarks>
/// Argon2id rather than Argon2i or Argon2d because it is the variant the RFC recommends for
/// password storage: it takes the data-independent first pass that resists side-channel
/// observation and the data-dependent later passes that resist a time-memory trade-off.
/// </remarks>
public sealed class Argon2idPasswordHasher(IOptions<PasswordHashingOptions> options) : IPasswordHasher
{
    private PasswordHashingOptions Options => options.Value;

    /// <inheritdoc />
    public async Task<string> HashAsync(string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);
        cancellationToken.ThrowIfCancellationRequested();

        PasswordHashingOptions settings = Options;
        byte[] salt = RandomNumberGenerator.GetBytes(settings.SaltBytes);

        byte[] digest = await DeriveAsync(
            password,
            salt,
            settings.MemoryKib,
            settings.Iterations,
            settings.Parallelism,
            settings.HashBytes);

        return new PasswordHash(
            settings.MemoryKib,
            settings.Iterations,
            settings.Parallelism,
            salt,
            digest).Format();
    }

    /// <inheritdoc />
    /// <remarks>
    /// A malformed or missing hash is answered like a wrong password, after doing the work a real
    /// verification would have done. A stored row that cannot be parsed must not be the fast path.
    /// </remarks>
    public async Task<PasswordVerification> VerifyAsync(
        string password,
        string? encodedHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);
        cancellationToken.ThrowIfCancellationRequested();

        if (!PasswordHash.TryParse(encodedHash, out PasswordHash? stored) || stored is null)
        {
            await SpendComparableTimeAsync(password);
            return PasswordVerification.Failed;
        }

        byte[] computed = await DeriveAsync(
            password,
            stored.Salt,
            stored.MemoryKib,
            stored.Iterations,
            stored.Parallelism,
            stored.Digest.Length);

        if (!CryptographicOperations.FixedTimeEquals(computed, stored.Digest))
        {
            return PasswordVerification.Failed;
        }

        return new PasswordVerification(IsMatch: true, NeedsRehash: IsWeakerThanConfigured(stored));
    }

    /// <summary>Whether <paramref name="stored"/> was made with less work than is required now.</summary>
    private bool IsWeakerThanConfigured(PasswordHash stored)
    {
        PasswordHashingOptions settings = Options;

        return stored.MemoryKib < settings.MemoryKib
            || stored.Iterations < settings.Iterations
            || stored.Salt.Length < settings.SaltBytes
            || stored.Digest.Length < settings.HashBytes;
    }

    /// <summary>
    /// Does the work of a verification and discards it, so that an unparseable stored hash costs
    /// the same wall-clock time as a real one and cannot be told apart by an observer.
    /// </summary>
    private async Task SpendComparableTimeAsync(string password)
    {
        PasswordHashingOptions settings = Options;

        byte[] discarded = await DeriveAsync(
            password,
            RandomNumberGenerator.GetBytes(settings.SaltBytes),
            settings.MemoryKib,
            settings.Iterations,
            settings.Parallelism,
            settings.HashBytes);

        CryptographicOperations.ZeroMemory(discarded);
    }

    private static async Task<byte[]> DeriveAsync(
        string password,
        byte[] salt,
        int memoryKib,
        int iterations,
        int parallelism,
        int hashBytes)
    {
        byte[] secret = Encoding.UTF8.GetBytes(password);

        try
        {
            using Argon2id argon2 = new(secret)
            {
                Salt = salt,
                MemorySize = memoryKib,
                Iterations = iterations,
                DegreeOfParallelism = parallelism
            };

            return await argon2.GetBytesAsync(hashBytes);
        }
        finally
        {
            // The plaintext lives in exactly two places for the length of a request — the request
            // body and this array — and the one this method owns is cleared on the way out.
            CryptographicOperations.ZeroMemory(secret);
        }
    }
}
