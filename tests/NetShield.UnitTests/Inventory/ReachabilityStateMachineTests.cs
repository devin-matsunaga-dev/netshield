using FluentAssertions;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Reachability;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The device state machine, exercised as the pure function it is.
/// </summary>
/// <remarks>
/// Every claim WP-1.4 makes about state lives here — a device goes offline after the configured
/// number of failures and comes back after the configured number of successes, a flapping device
/// produces no transition at all, and a probe that observed nothing changes nothing. None of it
/// needs a database, a queue or a collector to be true, so none of it is tested through one.
/// </remarks>
public sealed class ReachabilityStateMachineTests
{
    /// <summary>Three failures to go down, two successes to come back.</summary>
    private static readonly ReachabilityOptions Options = new()
    {
        FailureThreshold = 3,
        SuccessThreshold = 2
    };

    [Fact]
    public void Classify_EveryReplyReceived_IsOnline() =>
        ReachabilityStateMachine.Classify(4, 4).Should().Be(DeviceState.Online);

    [Fact]
    public void Classify_SomeRepliesReceived_IsWarning() =>
        ReachabilityStateMachine.Classify(4, 1).Should().Be(DeviceState.Warning);

    [Fact]
    public void Classify_NoRepliesReceived_IsOffline() =>
        ReachabilityStateMachine.Classify(4, 0).Should().Be(DeviceState.Offline);

    [Fact]
    public void Classify_NothingSent_IsUnknown() =>
        // A probe that sent nothing observed nothing, and Unknown is how the caller is told that
        // this is not evidence in either direction.
        ReachabilityStateMachine.Classify(0, 0).Should().Be(DeviceState.Unknown);

    [Fact]
    public void Apply_FewerConsecutiveFailuresThanTheThreshold_DoesNotChangeTheState()
    {
        ReachabilityTransition first = Apply(DeviceState.Online, DeviceState.Online, 5, DeviceState.Offline);
        ReachabilityTransition second = Apply(DeviceState.Online, first, DeviceState.Offline);

        first.Changed.Should().BeFalse();
        second.Changed.Should().BeFalse();
        second.State.Should().Be(DeviceState.Online);
        second.PendingObservations.Should().Be(2);
    }

    [Fact]
    public void Apply_TheFailureThresholdIsReached_MovesToOffline()
    {
        ReachabilityTransition third = Run(DeviceState.Online, DeviceState.Offline, times: 3);

        third.Changed.Should().BeTrue();
        third.State.Should().Be(DeviceState.Offline);
    }

    [Fact]
    public void Apply_AlreadyOfflineAndStillFailing_RaisesNoFurtherTransition()
    {
        // The property that keeps a week-long outage to one event rather than ten thousand.
        ReachabilityTransition further = Run(DeviceState.Offline, DeviceState.Offline, times: 10);

        further.Changed.Should().BeFalse();
        further.State.Should().Be(DeviceState.Offline);
    }

    [Fact]
    public void Apply_TheSuccessThresholdIsReachedAfterAnOutage_MovesBackToOnline()
    {
        ReachabilityTransition recovered = Run(DeviceState.Offline, DeviceState.Online, times: 2);

        recovered.Changed.Should().BeTrue();
        recovered.State.Should().Be(DeviceState.Online);
    }

    [Fact]
    public void Apply_OneSuccessAfterAnOutage_IsNotYetARecovery()
    {
        ReachabilityTransition first = Run(DeviceState.Offline, DeviceState.Online, times: 1);

        first.Changed.Should().BeFalse();
        first.State.Should().Be(DeviceState.Offline);
    }

    [Fact]
    public void Apply_SustainedPartialLoss_MovesToWarning()
    {
        ReachabilityTransition degraded = Run(DeviceState.Online, DeviceState.Warning, times: 2);

        degraded.Changed.Should().BeTrue();
        degraded.State.Should().Be(DeviceState.Warning);
    }

    [Fact]
    public void Apply_OneLostPacketOnAnOtherwiseHealthyDevice_DoesNotMoveIt()
    {
        // A single stray loss is why the classification needs no configurable loss percentage:
        // the success threshold is what decides whether one bad probe means anything.
        ReachabilityTransition stray = Apply(DeviceState.Online, DeviceState.Online, 9, DeviceState.Warning);

        stray.Changed.Should().BeFalse();
        stray.State.Should().Be(DeviceState.Online);
    }

    [Fact]
    public void Apply_AFlappingDevice_EmitsNoTransitionAtAll()
    {
        // The WP-1.4 criterion, stated exactly: a device alternating between answering and not
        // never accumulates a run long enough to be adopted, because each observation resets the
        // other's count to one.
        DeviceState state = DeviceState.Online;
        DeviceState pending = DeviceState.Online;
        int observations = 1;
        int transitions = 0;

        foreach (int probe in Enumerable.Range(0, 40))
        {
            DeviceState observed = probe % 2 == 0 ? DeviceState.Offline : DeviceState.Online;

            ReachabilityTransition next = ReachabilityStateMachine.Apply(
                state, pending, observations, observed, Options);

            if (next.Changed)
            {
                transitions++;
            }

            state = next.State;
            pending = next.PendingState;
            observations = next.PendingObservations;
        }

        transitions.Should().Be(0);
        state.Should().Be(DeviceState.Online);
    }

    [Fact]
    public void Apply_AFailureRunInterruptedByOneSuccess_StartsTheRunAgain()
    {
        ReachabilityTransition twoFailures = Run(DeviceState.Online, DeviceState.Offline, times: 2);
        ReachabilityTransition interrupted = Apply(DeviceState.Online, twoFailures, DeviceState.Online);
        ReachabilityTransition failingAgain = Apply(DeviceState.Online, interrupted, DeviceState.Offline);

        failingAgain.Changed.Should().BeFalse();
        failingAgain.PendingObservations.Should().Be(1);
    }

    [Fact]
    public void Apply_AnUnknownObservation_LeavesBothTheStateAndTheRunUntouched()
    {
        // A probe that observed nothing is not a contradiction of the ones that did, so it does
        // not break a run that is partway to a threshold.
        ReachabilityTransition partway = Run(DeviceState.Online, DeviceState.Offline, times: 2);
        ReachabilityTransition nothing = Apply(DeviceState.Online, partway, DeviceState.Unknown);

        nothing.Changed.Should().BeFalse();
        nothing.PendingState.Should().Be(DeviceState.Offline);
        nothing.PendingObservations.Should().Be(2);
    }

    [Fact]
    public void Apply_AFirstSuccessfulProbeOfANewDevice_NeedsTheSuccessThresholdToLeaveUnknown()
    {
        ReachabilityTransition first = Apply(DeviceState.Unknown, DeviceState.Unknown, 0, DeviceState.Online);
        ReachabilityTransition second = Apply(DeviceState.Unknown, first, DeviceState.Online);

        first.Changed.Should().BeFalse();
        second.Changed.Should().BeTrue();
        second.State.Should().Be(DeviceState.Online);
    }

    [Fact]
    public void Apply_AThresholdOfOne_AdoptsTheFirstObservation()
    {
        ReachabilityOptions immediate = new() { FailureThreshold = 1, SuccessThreshold = 1 };

        ReachabilityTransition transition = ReachabilityStateMachine.Apply(
            DeviceState.Online, DeviceState.Online, 3, DeviceState.Offline, immediate);

        transition.Changed.Should().BeTrue();
        transition.State.Should().Be(DeviceState.Offline);
    }

    /// <summary>Observes <paramref name="observed"/> <paramref name="times"/> times in a row.</summary>
    private static ReachabilityTransition Run(DeviceState current, DeviceState observed, int times)
    {
        ReachabilityTransition transition = new(current, 0, current, Changed: false);

        foreach (int _ in Enumerable.Range(0, times))
        {
            transition = ReachabilityStateMachine.Apply(
                transition.State,
                transition.PendingState,
                transition.PendingObservations,
                observed,
                Options);
        }

        return transition;
    }

    private static ReachabilityTransition Apply(
        DeviceState current,
        DeviceState pending,
        int observations,
        DeviceState observed) =>
        ReachabilityStateMachine.Apply(current, pending, observations, observed, Options);

    private static ReachabilityTransition Apply(
        DeviceState current,
        ReachabilityTransition previous,
        DeviceState observed) =>
        ReachabilityStateMachine.Apply(
            previous.Changed ? previous.State : current,
            previous.PendingState,
            previous.PendingObservations,
            observed,
            Options);
}
