using FluentAssertions;

using FluentValidation;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Identity;

using NetShield.Identity;
using NetShield.Identity.Passwords;
using NetShield.Identity.Persistence;

using NetShield.Platform;

namespace NetShield.UnitTests.Identity;

/// <summary>
/// One call registers the module. A host that resolves half of it starts and then fails on the
/// first sign-in, which is the worst moment to discover a missing registration.
/// </summary>
public sealed class IdentityRegistrationTests
{
    [Fact]
    public void AddNetShieldIdentity_RegistersThePasswordPrimitives()
    {
        using IHost host = BuildHost();

        host.Services.GetRequiredService<IPasswordHasher>().Should().BeOfType<Argon2idPasswordHasher>();
        host.Services.GetRequiredService<PasswordPolicy>().Should().NotBeNull();
        host.Services.GetRequiredService<DecoyPasswordHash>().Should().NotBeNull();
    }

    [Fact]
    public void AddNetShieldIdentity_RegistersAValidatorForEveryRequestShape()
    {
        using IHost host = BuildHost();
        using IServiceScope scope = host.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IValidator<LoginRequest>>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IValidator<ChangePasswordRequest>>().Should().NotBeNull();
    }

    [Fact]
    public void PasswordHashingOptions_DefaultToTheOwaspArgon2idParameters()
    {
        using IHost host = BuildHost();

        PasswordHashingOptions options = host.Services
            .GetRequiredService<IOptions<PasswordHashingOptions>>().Value;

        options.MemoryKib.Should().Be(19 * 1024);
        options.Iterations.Should().Be(2);
        options.Parallelism.Should().Be(1);
    }

    [Fact]
    public void SessionOptions_LockTheAccountAfterFiveFailures()
    {
        using IHost host = BuildHost();

        NetShield.Identity.Authentication.SessionOptions options = host.Services
            .GetRequiredService<IOptions<NetShield.Identity.Authentication.SessionOptions>>().Value;

        options.MaxFailedLoginAttempts.Should().Be(5, "WP-0.4 fixes the threshold at five");
    }

    [Fact]
    public void OptionsBoundFromConfiguration_AreValidatedOnStart()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:PasswordPolicy:MinimumLength"] = "4"
        });

        builder.Services.AddDbContext<IdentityDbContext>(options => options.UseIdentityConventions());
        builder.AddNetShieldPlatform();
        builder.AddNetShieldIdentity();

        using IHost host = builder.Build();

        Action start = () => host.Services
            .GetRequiredService<IOptions<PasswordPolicyOptions>>().Value.ToString();

        start.Should().Throw<OptionsValidationException>(
            "a minimum length below the annotated floor is a misconfiguration, not a preference");
    }

    private static IHost BuildHost()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        builder.Services.AddDbContext<IdentityDbContext>(options => options.UseIdentityConventions());

        builder.AddNetShieldPlatform();
        builder.AddNetShieldIdentity();

        return builder.Build();
    }
}
