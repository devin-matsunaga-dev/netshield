import { useQuery } from '@tanstack/react-query';
import { useState, type SyntheticEvent } from 'react';

import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import { TextField } from '@/components/ui/TextField';
import { Timestamp } from '@/components/ui/Timestamp';
import { useToast } from '@/lib/toast';
import type { DeviceRequestError } from '@/features/devices/api/deviceMutations';
import { useCreateIgnore, useDeleteIgnore } from '@/features/discovery/api/discoveryMutations';
import { discoveryIgnoresQuery } from '@/features/discovery/api/discoveryQueries';
import { RequirePermission } from '@/features/session/components/RequirePermission';

/**
 * Addresses and blocks discovery must never offer for review again.
 *
 * Adding an entry also settles the candidates it covers: an operator who has just said "never
 * offer me anything in this block" should not then have to dismiss the eleven candidates already
 * listed from inside it.
 *
 * Removing an entry does not bring those candidates back. The next sweep that finds the address
 * answering is what puts it back on the review list, so one rule decides what is reviewable
 * rather than two.
 */
export function IgnoreList() {
  const ignores = useQuery(discoveryIgnoresQuery());
  const create = useCreateIgnore();
  const remove = useDeleteIgnore();
  const toast = useToast();

  const [cidr, setCidr] = useState('');
  const [reason, setReason] = useState('');

  const error = (create.error as DeviceRequestError | null) ?? null;

  function add(event: SyntheticEvent) {
    event.preventDefault();

    create.mutate(
      { cidr: cidr.trim(), reason: reason.trim() === '' ? null : reason.trim() },
      {
        onSuccess: () => {
          setCidr('');
          setReason('');
          toast.announce('Added to the ignore list');
        },
        onError: (failure) => {
          toast.announce(failure.message, 'danger');
        },
      },
    );
  }

  const rows = ignores.data ?? [];

  return (
    <Card title="Ignore list">
      <RequirePermission permission="InventoryWrite">
        <form onSubmit={add} className="mb-gutter flex flex-wrap items-end gap-4" noValidate>
          <div className="w-56">
            <TextField
              label="Address or range"
              required
              placeholder="192.0.2.37/32"
              value={cidr}
              error={
                error?.fieldErrors['cidr']?.[0] ??
                (error?.code === 'discovery.ignore-exists' ? error.message : undefined)
              }
              onChange={(event) => {
                setCidr(event.target.value);
              }}
            />
          </div>
          <div className="w-72">
            <TextField
              label="Reason"
              placeholder="Printer VLAN — not managed"
              value={reason}
              onChange={(event) => {
                setReason(event.target.value);
              }}
            />
          </div>
          <Button type="submit" disabled={create.isPending || cidr.trim() === ''}>
            Add to ignore list
          </Button>
        </form>
      </RequirePermission>

      {ignores.isPending ? (
        <div
          role="status"
          className="space-y-3"
          aria-busy="true"
          aria-label="Loading the ignore list"
        >
          {Array.from({ length: 3 }, (_, index) => (
            <Skeleton key={index} className="h-8 w-full" />
          ))}
        </div>
      ) : ignores.isError ? (
        <ErrorState
          title="The ignore list could not be loaded."
          action="NetShield could not reach the API. Check that it is running and try again."
          onRetry={() => void ignores.refetch()}
        />
      ) : rows.length === 0 ? (
        <EmptyState
          title="Nothing is ignored."
          action="Add an address or a block that discovery should never offer for review."
        />
      ) : (
        <div role="table" aria-label="Ignored addresses" className="flex flex-col">
          <div
            role="row"
            className="flex items-center gap-4 border-b border-subtle py-2 text-table-header text-muted"
          >
            <span role="columnheader" className="w-48 shrink-0">
              Address or range
            </span>
            <span role="columnheader" className="flex-1">
              Reason
            </span>
            <span role="columnheader" className="w-32 shrink-0">
              Added
            </span>
            <span role="columnheader" className="w-28 shrink-0">
              <span className="sr-only">Actions</span>
            </span>
          </div>

          {rows.map((entry) => (
            <div
              key={entry.id}
              role="row"
              className="flex h-row items-center gap-4 border-b border-subtle text-table-cell text-secondary hover:bg-raised"
            >
              <span role="cell" className="w-48 shrink-0 font-mono text-primary tabular-nums">
                {entry.cidr}
              </span>
              <span role="cell" className="flex-1 truncate">
                {entry.reason ?? '—'}
              </span>
              <span role="cell" className="w-32 shrink-0">
                <Timestamp value={entry.createdAt} />
              </span>
              <span role="cell" className="w-28 shrink-0 text-right">
                <RequirePermission permission="InventoryWrite">
                  <Button
                    variant="ghost"
                    disabled={remove.isPending}
                    onClick={() => {
                      remove.mutate(entry.id, {
                        onSuccess: () => {
                          toast.announce(`${entry.cidr} is no longer ignored`);
                        },
                        onError: (failure) => {
                          toast.announce(failure.message, 'danger');
                        },
                      });
                    }}
                  >
                    Remove
                  </Button>
                </RequirePermission>
              </span>
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}
