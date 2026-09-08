import type { Schemas } from '@/api/types';
import type { BadgeTone } from '@/components/ui/Badge';

/**
 * What each discovery status is called and how it renders.
 *
 * `PartiallyFailed` is the one worth reading twice: a run that swept nine spans and failed the
 * tenth found something real and also missed a tenth of the range, so it is neither a success
 * nor a failure and DESIGN.md's warning hue is exactly what it means.
 */
export const runStatusLabels: Record<Schemas['DiscoveryRunStatus'], string> = {
  Pending: 'Pending',
  Running: 'Running',
  Completed: 'Completed',
  PartiallyFailed: 'Partially failed',
  Failed: 'Failed',
};

export const runStatusTones: Record<Schemas['DiscoveryRunStatus'], BadgeTone> = {
  Pending: 'muted',
  Running: 'accent',
  Completed: 'success',
  PartiallyFailed: 'warning',
  Failed: 'danger',
};

export const runTriggerLabels: Record<Schemas['DiscoveryRunTrigger'], string> = {
  Scheduled: 'Scheduled',
  OnDemand: 'On demand',
};

export const candidateStatusLabels: Record<Schemas['DiscoveryCandidateStatus'], string> = {
  New: 'Awaiting review',
  Promoted: 'Promoted',
  Ignored: 'Ignored',
};

export const candidateStatusTones: Record<Schemas['DiscoveryCandidateStatus'], BadgeTone> = {
  New: 'accent',
  Promoted: 'success',
  Ignored: 'muted',
};

export const hostOutcomeLabels: Record<Schemas['DiscoveryHostOutcome'], string> = {
  NewCandidate: 'New candidate',
  KnownCandidate: 'Already a candidate',
  ExistingDevice: 'Already a device',
  Ignored: 'Ignored',
};

export const hostOutcomeTones: Record<Schemas['DiscoveryHostOutcome'], BadgeTone> = {
  NewCandidate: 'accent',
  KnownCandidate: 'info',
  ExistingDevice: 'success',
  Ignored: 'muted',
};
