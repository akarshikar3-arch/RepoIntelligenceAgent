import { InjectionToken } from '@angular/core';
import { AgentMetadata } from './agent-metadata.model';

/**
 * Multi-provider token. Each agent contributes its `AgentMetadata` here and
 * the hub composes the sidenav / dashboard cards from the collected entries.
 */
export const AGENT_METADATA = new InjectionToken<readonly AgentMetadata[]>(
  'AGENT_METADATA',
  { providedIn: 'root', factory: () => [] }
);
