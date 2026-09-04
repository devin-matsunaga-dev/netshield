using NetShield.Platform.Results;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// What a seed actually asks for: its ranges, minus its exclusions, cut into jobs.
/// </summary>
/// <remarks>
/// <para>
/// One place where a seed's text becomes arithmetic, so that the number a validator reports, the
/// number a run records, and the addresses the collector is asked to probe are all derived from
/// the same reading. Three separate readings of "10.0.0.0/24 except 10.0.0.128/25" is how a run
/// comes to say it swept 254 addresses and probe 126.
/// </para>
/// <para>
/// Ranges within a seed do not overlap — the validator refuses a seed whose do — which is what
/// lets the address count be a sum rather than a union. Exclusions may overlap each other and
/// may sit outside every range; both are harmless and both are handled by merging them before
/// anything is subtracted.
/// </para>
/// </remarks>
internal sealed class SweepPlan
{
    private readonly IReadOnlyList<AddressSpan> exclusionSpans;

    private SweepPlan(
        IReadOnlyList<AddressRange> ranges,
        IReadOnlyList<AddressRange> exclusions,
        IReadOnlyList<AddressSpan> exclusionSpans)
    {
        Ranges = ranges;
        Exclusions = exclusions;
        this.exclusionSpans = exclusionSpans;
    }

    /// <summary>The blocks to sweep, normalised.</summary>
    internal IReadOnlyList<AddressRange> Ranges { get; }

    /// <summary>The blocks inside them that must never be probed, normalised.</summary>
    internal IReadOnlyList<AddressRange> Exclusions { get; }

    /// <summary>
    /// How many addresses one run would probe, saturating at <see cref="long.MaxValue"/>.
    /// </summary>
    internal long AddressCount
    {
        get
        {
            UInt128 total = UInt128.Zero;

            foreach (AddressRange range in Ranges)
            {
                AddressSpan span = new(range.Family, range.FirstHost, range.LastHost);

                total += Length(span) - Covered(span);
            }

            return total > (UInt128)long.MaxValue ? long.MaxValue : (long)total;
        }
    }

    /// <summary>Reads a seed's ranges and exclusions, refusing anything that is not an address.</summary>
    internal static Result<SweepPlan> Create(
        IReadOnlyList<string> ranges,
        IReadOnlyList<string>? exclusions)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        Result<List<AddressRange>> parsedRanges = ParseAll(ranges);

        if (!parsedRanges.IsSuccess)
        {
            return Result<SweepPlan>.Failure(parsedRanges.Error);
        }

        Result<List<AddressRange>> parsedExclusions = ParseAll(exclusions ?? []);

        if (!parsedExclusions.IsSuccess)
        {
            return Result<SweepPlan>.Failure(parsedExclusions.Error);
        }

        return new SweepPlan(
            parsedRanges.Value,
            parsedExclusions.Value,
            Merge(parsedExclusions.Value));
    }

    /// <summary>The first pair of ranges that overlap, or nothing if none do.</summary>
    /// <remarks>
    /// Overlapping ranges in one seed are refused rather than merged. Merging would make the
    /// stored seed differ from what the operator typed, and the address count depends on the
    /// ranges being disjoint — two ranges that overlap would have the shared addresses counted
    /// twice and swept twice.
    /// </remarks>
    internal (AddressRange First, AddressRange Second)? FirstOverlap()
    {
        for (int outer = 0; outer < Ranges.Count; outer++)
        {
            for (int inner = outer + 1; inner < Ranges.Count; inner++)
            {
                if (Ranges[outer].Overlaps(Ranges[inner]))
                {
                    return (Ranges[outer], Ranges[inner]);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The work, as spans of at most <paramref name="maxAddressesPerSpan"/> addresses each.
    /// </summary>
    /// <remarks>
    /// A span every one of whose addresses is excluded is not returned at all: queueing a job
    /// whose whole answer is "everything here was excluded" spends a lease and a round trip to
    /// learn nothing. A span that is only partly excluded is returned whole, and the collector
    /// applies the exclusions itself — which is why they travel in the job's parameters.
    /// </remarks>
    internal IEnumerable<AddressSpan> Spans(int maxAddressesPerSpan)
    {
        foreach (AddressRange range in Ranges)
        {
            foreach (AddressSpan span in range.Spans(maxAddressesPerSpan))
            {
                if (Covered(span) < Length(span))
                {
                    yield return span;
                }
            }
        }
    }

    /// <summary>
    /// How many of a span's addresses would actually be probed, once exclusions are applied.
    /// </summary>
    /// <remarks>
    /// What a run records as its address count, summed over the spans it actually queued. Taking
    /// it from the plan as a whole would over-report a run that hit
    /// <c>DiscoveryOptions.MaxJobsPerRun</c> and queued only part of the seed.
    /// </remarks>
    internal long Probeable(AddressSpan span)
    {
        UInt128 probeable = Length(span) - Covered(span);

        return probeable > (UInt128)long.MaxValue ? long.MaxValue : (long)probeable;
    }

    private static Result<List<AddressRange>> ParseAll(IReadOnlyList<string> values)
    {
        List<AddressRange> parsed = new(values.Count);

        foreach (string value in values)
        {
            Result<AddressRange> range = AddressRange.Parse(value);

            if (!range.IsSuccess)
            {
                return Result<List<AddressRange>>.Failure(range.Error);
            }

            parsed.Add(range.Value);
        }

        return parsed;
    }

    /// <summary>The exclusions as non-overlapping spans, sorted, one sequence per family.</summary>
    private static List<AddressSpan> Merge(IReadOnlyList<AddressRange> exclusions)
    {
        List<AddressSpan> spans =
        [
            .. exclusions
                .Select(exclusion => new AddressSpan(exclusion.Family, exclusion.Network, exclusion.Last))
                .OrderBy(span => span.Family)
                .ThenBy(span => span.First)
        ];

        List<AddressSpan> merged = [];

        foreach (AddressSpan span in spans)
        {
            if (merged.Count > 0
                && merged[^1].Family == span.Family
                && span.First <= merged[^1].Last)
            {
                merged[^1] = merged[^1] with { Last = UInt128.Max(merged[^1].Last, span.Last) };

                continue;
            }

            merged.Add(span);
        }

        return merged;
    }

    /// <summary>How many of a span's addresses an exclusion covers.</summary>
    private UInt128 Covered(AddressSpan span)
    {
        UInt128 covered = UInt128.Zero;

        foreach (AddressSpan exclusion in exclusionSpans)
        {
            if (exclusion.Family != span.Family)
            {
                continue;
            }

            if (span.Intersect(exclusion) is { } shared)
            {
                covered += Length(shared);
            }
        }

        return covered;
    }

    private static UInt128 Length(AddressSpan span) => span.Last - span.First + UInt128.One;
}
