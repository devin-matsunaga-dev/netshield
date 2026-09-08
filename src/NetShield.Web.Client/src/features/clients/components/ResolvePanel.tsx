import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState, type SyntheticEvent } from 'react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Skeleton } from '@/components/ui/Skeleton';
import { TextField } from '@/components/ui/TextField';
import { Timestamp } from '@/components/ui/Timestamp';
import { assetResolutionQuery, type AssetResolution } from '@/features/clients/api/clientQueries';
import { assetKindLabels } from '@/features/clients/components/clientLabels';

/** What the reader asked: an address, and optionally a moment other than now. */
interface Asked {
  readonly ipAddress: string;
  readonly at: string | undefined;
}

/**
 * `ResolveAssetAt` with a box round it: what held an address at an instant.
 *
 * This is the answer to a question somebody asks the first time a Phase 4 report looks wrong —
 * "this flow was blamed on that host; was it?" — and it is the only way a person can check the
 * handover behaviour by hand rather than by reading a test. Leaving the instant blank asks about
 * now, which is what somebody typing an address into the box usually wants.
 *
 * A refusal is an ordinary answer here. Typing something that is not an address, or a moment that
 * has not happened, gets a sentence rather than an error state — the reader can act on both.
 */
export function ResolvePanel() {
  const [ipAddress, setIpAddress] = useState('');
  const [at, setAt] = useState('');
  const [asked, setAsked] = useState<Asked | null>(null);

  const resolution = useQuery({
    ...assetResolutionQuery(asked?.ipAddress ?? '', asked?.at),
    enabled: asked !== null,
  });

  function ask(event: SyntheticEvent) {
    event.preventDefault();

    if (ipAddress.trim() === '') {
      return;
    }

    setAsked({
      ipAddress: ipAddress.trim(),
      // A datetime-local value carries no zone. Reading it as local time and sending the instant
      // is what makes "14:03" mean the reader's 14:03 rather than UTC's.
      at: at === '' ? undefined : new Date(at).toISOString(),
    });
  }

  const answer = resolution.data;

  return (
    <Card title="Resolve an address">
      <form onSubmit={ask} className="flex flex-wrap items-end gap-4">
        <div className="w-56">
          <TextField
            label="IP address"
            placeholder="10.0.0.5"
            value={ipAddress}
            onChange={(event) => {
              setIpAddress(event.target.value);
            }}
          />
        </div>
        <div className="w-64">
          <TextField
            label="At"
            type="datetime-local"
            hint="Leave blank to ask about now."
            value={at}
            onChange={(event) => {
              setAt(event.target.value);
            }}
          />
        </div>
        <Button type="submit" disabled={ipAddress.trim() === ''}>
          Resolve
        </Button>
      </form>

      {asked !== null && (
        <div className="mt-gutter border-t border-subtle pt-gutter">
          {resolution.isPending ? (
            <div role="status" aria-busy="true" aria-label="Resolving the address">
              <Skeleton className="h-8 w-80" />
            </div>
          ) : resolution.isError ? (
            <p className="text-body text-danger">
              NetShield could not resolve the address. Check that the API is running and try again.
            </p>
          ) : answer === null || answer === undefined ? (
            <p className="text-body text-secondary">
              That is not an address NetShield can resolve, or the moment asked about has not
              happened yet. Enter an IPv4 or IPv6 address and a time no later than now.
            </p>
          ) : (
            <Answer answer={answer} />
          )}
        </div>
      )}
    </Card>
  );
}

function Answer({ answer }: { readonly answer: AssetResolution }) {
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-3">
        <Badge
          tone={answer.kind === 'Device' ? 'accent' : answer.kind === 'Client' ? 'violet' : 'muted'}
        >
          {assetKindLabels[answer.kind]}
        </Badge>
        <span className="font-mono text-body text-primary">{answer.ipAddress}</span>
        <span className="text-body text-muted">at</span>
        <span className="text-body text-secondary">
          <Timestamp value={answer.at} />
        </span>
      </div>

      {answer.kind === 'Unresolved' ? (
        <p className="text-body text-secondary">
          Nothing in the inventory held this address then. No device is reached on it and no
          observation recorded a client holding it at that moment.
        </p>
      ) : (
        <dl className="grid grid-cols-[10rem_1fr] gap-x-4 gap-y-2 text-body">
          {answer.deviceId !== null && (
            <>
              <dt className="text-secondary">Device name</dt>
              <dd className="text-primary">
                <Link
                  to="/devices/$deviceId"
                  params={{ deviceId: answer.deviceId }}
                  className="rounded-control text-accent focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
                >
                  {answer.deviceHostname ?? answer.deviceId}
                </Link>
              </dd>
            </>
          )}

          {answer.clientId !== null && (
            <>
              <dt className="text-secondary">Hardware address</dt>
              <dd className="text-primary">
                <Link
                  to="/clients/$clientId"
                  params={{ clientId: answer.clientId }}
                  className="rounded-control font-mono text-accent focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
                >
                  {answer.macAddress}
                </Link>
              </dd>
            </>
          )}

          {/*
            The interval the answer came out of. There is deliberately no "which MIB table said
            so" row: a nullable enum member contaminates the shared schema in the generated
            client (see AssetResolution), and the source is on the client's own history where it
            is always present.
          */}
          {answer.observedFrom !== null && (
            <>
              <dt className="text-secondary">Held from</dt>
              <dd className="text-primary">
                <Timestamp value={answer.observedFrom} />
              </dd>
              <dt className="text-secondary">Held until</dt>
              <dd className="text-primary">
                {answer.observedTo === null ? (
                  'Still held'
                ) : (
                  <Timestamp value={answer.observedTo} />
                )}
              </dd>
            </>
          )}
        </dl>
      )}
    </div>
  );
}
