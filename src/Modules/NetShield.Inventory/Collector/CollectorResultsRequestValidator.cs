using FluentValidation;

using NetShield.Inventory.Collector.Contract;

namespace NetShield.Inventory.Collector;

/// <summary>Validates a result submission at the boundary (CONVENTIONS.md §4).</summary>
/// <remarks>
/// Shape only. Whether a job exists, whether this collector still holds it, and whether it has
/// already been reported on are all facts about stored state rather than about the request, so
/// the handler answers them — and answers them per report, because one bad report in a batch
/// must not refuse the nineteen good ones alongside it.
/// </remarks>
internal sealed class CollectorResultsRequestValidator : AbstractValidator<CollectorResultsRequest>
{
    public CollectorResultsRequestValidator()
    {
        RuleFor(request => request.Collector)
            .NotEmpty()
            .MaximumLength(CollectorLimits.NameLength);

        RuleFor(request => request.Results)
            .NotNull();

        RuleForEach(request => request.Results).ChildRules(report =>
        {
            report.RuleFor(result => result.JobId).NotEmpty();

            report.RuleFor(result => result.LeaseToken)
                .NotEmpty()
                .MaximumLength(CollectorLimits.LeaseTokenLength);

            report.RuleFor(result => result.Outcome).IsInEnum();

            report.RuleFor(result => result.Detail)
                .MaximumLength(CollectorLimits.DetailLength);
        });
    }
}
