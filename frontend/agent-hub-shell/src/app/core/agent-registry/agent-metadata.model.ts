import { Routes } from '@angular/router';

export type AgentCapability =
  | 'repo-input'
  | 'snippet-input'
  | 'chat'
  | 'dashboard'
  | 'architecture-graph'
  | 'static-analysis';

export interface AgentMetadata {
  /** Stable id used for routing and registration. */
  readonly id: string;
  readonly title: string;
  readonly description: string;
  /** Material symbol name or svg id resolved by the hub. */
  readonly icon: string;
  /** Absolute route under the hub, e.g. `/agents/code-reviewer`. */
  readonly route: string;
  /** Optional accent color override; falls back to hub theme. */
  readonly accent?: string;
  readonly capabilities: readonly AgentCapability[];
  /** Lazy route loader for the agent feature. */
  readonly loadChildren: () => Promise<Routes>;
}
