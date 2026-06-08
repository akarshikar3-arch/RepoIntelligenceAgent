import { Injectable, computed, inject, signal } from '@angular/core';
import { Route } from '@angular/router';
import { AgentMetadata } from './agent-metadata.model';
import { AGENT_METADATA } from './agent-registry.tokens';

@Injectable({ providedIn: 'root' })
export class AgentRegistryService {
  private readonly registered = inject(AGENT_METADATA, { optional: true }) ?? [];
  private readonly extra = signal<AgentMetadata[]>([]);

  readonly agents = computed<readonly AgentMetadata[]>(() => [
    ...this.registered,
    ...this.extra(),
  ]);

  /** Programmatic registration (used when an agent is added at runtime). */
  register(meta: AgentMetadata): void {
    if (this.agents().some(a => a.id === meta.id)) return;
    this.extra.update(list => [...list, meta]);
  }

  byId(id: string): AgentMetadata | undefined {
    return this.agents().find(a => a.id === id);
  }

  /** Convenience: emit Angular `Route[]` for all registered agents. */
  toRoutes(): Route[] {
    return this.agents().map(meta => ({
      path: meta.route.replace(/^\//, '').replace(/^agents\//, ''),
      loadChildren: () => meta.loadChildren(),
      data: { agent: meta },
    }));
  }
}
