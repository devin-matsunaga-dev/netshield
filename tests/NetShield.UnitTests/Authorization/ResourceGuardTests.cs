using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NetShield.Contracts.Identity;
using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

using NSubstitute;

namespace NetShield.UnitTests.Authorization;

/// <summary>
/// Covers the module-level check of ARCHITECTURE.md §8 — the one a handler makes for itself,
/// after the endpoint has already made one.
/// </summary>
public sealed class ResourceGuardTests
{
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IAuditContext _audit = Substitute.For<IAuditContext>();

    [Fact]
    public void Require_ACallerHoldingThePermission_Succeeds()
    {
        _user.IsAuthenticated.Returns(true);
        _user.Has(Permission.InventoryWrite).Returns(true);

        Guard().Require(Permission.InventoryWrite, "device", "d-1").IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Require_ACallerWithoutThePermission_IsForbidden()
    {
        _user.IsAuthenticated.Returns(true);
        _user.Has(Permission.InventoryWrite).Returns(false);
        _user.Role.Returns(UserRole.Analyst);

        Result result = Guard().Require(Permission.InventoryWrite, "device", "d-1");

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(ErrorKind.Forbidden);
    }

    [Fact]
    public void Require_AnAnonymousCaller_IsUnauthenticated()
    {
        _user.IsAuthenticated.Returns(false);

        Result result = Guard().Require(Permission.InventoryWrite, "device");

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(ErrorKind.Unauthenticated);
    }

    [Fact]
    public void Require_NamesTheResourceOnTheAuditRow_WhateverTheAnswer()
    {
        _user.IsAuthenticated.Returns(true);
        _user.Has(Permission.InventoryWrite).Returns(false);

        Guard().Require(Permission.InventoryWrite, "device", "d-1");

        // A refusal that does not say what was refused is a row nobody can act on.
        _audit.Received(1).Target("device", "d-1");
    }

    [Fact]
    public void Require_WithNoResourceType_Throws()
    {
        Action act = () => Guard().Require(Permission.InventoryWrite, string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    private ResourceGuard Guard() => new(_user, _audit, NullLogger<ResourceGuard>.Instance);
}
