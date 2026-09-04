using FluentValidation;

using NetShield.Inventory.Collector.Contract;

namespace NetShield.Inventory.Collector;

/// <summary>Validates a heartbeat at the boundary (CONVENTIONS.md §4).</summary>
/// <remarks>
/// The bounds are about what the row can hold and what a number can sensibly be, not about
/// whether the collector is telling the truth — it is reporting on itself and nothing depends on
/// it being right. Refusing a nonsense value is still worth doing, because a negative capacity
/// in the health page is a bug nobody can explain later.
/// </remarks>
internal sealed class CollectorHeartbeatRequestValidator : AbstractValidator<CollectorHeartbeatRequest>
{
    /// <summary>The most jobs a single collector may claim to run at once.</summary>
    private const int MaxCapacity = 10_000;

    public CollectorHeartbeatRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .MaximumLength(CollectorLimits.NameLength);

        RuleFor(request => request.Version)
            .MaximumLength(CollectorLimits.VersionLength);

        RuleFor(request => request.Capacity)
            .InclusiveBetween(0, MaxCapacity);

        RuleFor(request => request.Running)
            .InclusiveBetween(0, MaxCapacity);
    }
}
