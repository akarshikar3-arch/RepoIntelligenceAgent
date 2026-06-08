import { AgentMetadata } from '../../core/agent-registry';

export const CODE_REVIEWER_AGENT: AgentMetadata = {
  id: 'code-reviewer',
  title: 'Repo Intelligence',
  description:
    'Analyze a GitHub repository or pasted snippet. Get a premium dashboard, ' +
    'architecture insights, risk hotspots, and a context-grounded code assistant.',
  icon: 'graph_3',
  route: '/agents/code-reviewer',
  accent: '#c1121f',
  capabilities: [
    'repo-input',
    'snippet-input',
    'dashboard',
    'architecture-graph',
    'static-analysis',
    'chat',
  ],
  loadChildren: () =>
    import('./code-reviewer.routes').then(m => m.CODE_REVIEWER_ROUTES),
};
