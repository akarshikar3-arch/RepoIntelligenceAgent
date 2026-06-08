/** Public surface for hosts that want to embed the agent. */
export { CODE_REVIEWER_AGENT } from './code-reviewer.agent';
export {
  provideRepoIntelAgent,
  REPO_INTEL_CONFIG,
  type RepoIntelAgentConfig,
} from './code-reviewer.providers';
export { CODE_REVIEWER_ROUTES } from './code-reviewer.routes';
export { SessionStore } from './state/session.store';
export { DashboardApi } from './data-access/api/dashboard.api';
export * from './data-access/models/session.models';
export * from './data-access/models/dashboard.models';
