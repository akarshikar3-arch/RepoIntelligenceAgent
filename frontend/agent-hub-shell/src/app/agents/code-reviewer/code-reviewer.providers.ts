import { EnvironmentProviders, InjectionToken, makeEnvironmentProviders } from '@angular/core';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { AGENT_METADATA } from '../../core/agent-registry';
import { CODE_REVIEWER_AGENT } from './code-reviewer.agent';

export interface RepoIntelAgentConfig {
  /** Backend base URL, e.g. https://localhost:5001 */
  apiBaseUrl: string;
}

export const REPO_INTEL_CONFIG = new InjectionToken<RepoIntelAgentConfig>('REPO_INTEL_CONFIG');

/**
 * Plug into the host Agent Hub:
 *
 *   bootstrapApplication(AppShell, {
 *     providers: [
 *       provideRepoIntelAgent({ apiBaseUrl: 'https://localhost:5001' }),
 *     ],
 *   });
 */
export function provideRepoIntelAgent(
  config: RepoIntelAgentConfig
): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideHttpClient(withFetch()),
    { provide: AGENT_METADATA, multi: true, useValue: CODE_REVIEWER_AGENT },
    { provide: REPO_INTEL_CONFIG, useValue: config },
  ]);
}
