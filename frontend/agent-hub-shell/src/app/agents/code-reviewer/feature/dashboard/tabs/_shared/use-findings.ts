import { computed, inject, Signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of, switchMap } from 'rxjs';
import { DashboardApi } from '../../../../data-access/api/dashboard.api';
import { Finding, Severity, SeverityCount } from '../../../../data-access/models/dashboard.models';
import { PanelCard } from '../../_ui/panel-card';
import { FindingList } from '../../_ui/finding-list';
import { SeverityBar } from '../../_ui/severity-bar';

const SEVERITY_ORDER: Severity[] = ['critical', 'high', 'medium', 'low', 'info'];

export interface FindingsTabState {
  status: 'loading' | 'ready' | 'error';
  findings: Finding[];
  error: string | null;
}

export function summarizeFindings(findings: Finding[]): SeverityCount[] {
  const map = new Map<Severity, number>();
  for (const f of findings) map.set(f.severity, (map.get(f.severity) ?? 0) + 1);
  return SEVERITY_ORDER.map(severity => ({ severity, count: map.get(severity) ?? 0 }));
}

/**
 * Loads findings for the current session, optionally filtered by category, and
 * returns Angular signals for use by category-specific tabs (Quality, Security).
 */
export function useFindings(category?: string): {
  state: Signal<FindingsTabState>;
  counts: Signal<SeverityCount[]>;
} {
  const api = inject(DashboardApi);
  const route = inject(ActivatedRoute);

  const initial: FindingsTabState = { status: 'loading', findings: [], error: null };
  const state: Signal<FindingsTabState> = toSignal(
    route.parent!.paramMap.pipe(
      switchMap(p => {
        const id = p.get('sessionId');
        if (!id) return of<FindingsTabState>({ status: 'ready', findings: [], error: null });
        return api.getFindings(id, category ? { category } : {}).pipe(
          map(findings => ({ status: 'ready' as const, findings, error: null }) as FindingsTabState),
          catchError(err =>
            of<FindingsTabState>({
              status: 'error',
              findings: [],
              error: err?.message ?? 'Failed to load findings',
            })
          )
        );
      })
    ),
    { initialValue: initial }
  );

  return {
    state,
    counts: computed(() => summarizeFindings(state().findings)),
  };
}

export const FINDING_TAB_IMPORTS = [PanelCard, FindingList, SeverityBar] as const;
