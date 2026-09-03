using FluentAssertions;

using NetShield.Platform.Messaging;

namespace NetShield.UnitTests.Platform;

/// <summary>
/// Covers the registry that stands between an outbox row and <see cref="Type.GetType(string)"/>.
/// A row can only ever name a type the host declared, which is what stops a stored string from
/// choosing what gets constructed.
/// </summary>
public sealed class IntegrationEventRegistryTests
{
    [Fact]
    public void ARegisteredEvent_RoundTripsThroughItsStoredName()
    {
        IntegrationEventRegistry registry = new([new IntegrationEventRegistration(typeof(ProbeEvent))]);

        string name = registry.NameOf(typeof(ProbeEvent));

        name.Should().Be(typeof(ProbeEvent).FullName);
        registry.TryResolve(name, out Type resolved).Should().BeTrue();
        resolved.Should().Be<ProbeEvent>();
    }

    [Fact]
    public void AnUnregisteredEvent_CannotBeNamed()
    {
        IntegrationEventRegistry registry = new([]);

        Action name = () => registry.NameOf(typeof(ProbeEvent));

        name.Should().Throw<InvalidOperationException>().WithMessage("*AddIntegrationEvent*");
    }

    [Fact]
    public void AnUnknownStoredName_DoesNotResolve()
    {
        IntegrationEventRegistry registry = new([new IntegrationEventRegistration(typeof(ProbeEvent))]);

        registry.TryResolve("System.IO.FileInfo", out _).Should().BeFalse(
            "a stored string must not be able to choose what gets constructed");
    }

    [Fact]
    public void ATypeThatIsNotAnIntegrationEvent_CannotBeRegistered()
    {
        Action register = () => new IntegrationEventRegistration(typeof(string));

        register.Should().Throw<ArgumentException>().WithMessage("*IIntegrationEvent*");
    }

    [Fact]
    public void IsRegistered_ReportsWhetherAnInstanceMayBePublished()
    {
        IntegrationEventRegistry registry = new([new IntegrationEventRegistration(typeof(ProbeEvent))]);

        registry.IsRegistered(new ProbeEvent("core-sw-01")).Should().BeTrue();
        registry.RegisteredEvents.Should().ContainSingle().Which.Should().Be<ProbeEvent>();
    }
}
