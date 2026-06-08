import { Routes } from '@angular/router';

export const CODE_REVIEWER_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./feature/landing/landing.page').then(m => m.LandingPage),
    title: 'Repo Intelligence',
  },
  {
    path: 'session/:sessionId',
    loadComponent: () =>
      import('./feature/dashboard/dashboard-shell.page').then(
        m => m.DashboardShellPage
      ),
    title: 'Repo Intelligence — Dashboard',
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'overview' },
      {
        path: 'overview',
        loadComponent: () =>
          import('./feature/dashboard/tabs/overview-tab/overview-tab').then(
            m => m.OverviewTab
          ),
      },
      {
        path: 'architecture',
        loadComponent: () =>
          import(
            './feature/dashboard/tabs/architecture-tab/architecture-tab'
          ).then(m => m.ArchitectureTab),
      },
      {
        path: 'quality',
        loadComponent: () =>
          import('./feature/dashboard/tabs/quality-tab/quality-tab').then(
            m => m.QualityTab
          ),
      },
      {
        path: 'security',
        loadComponent: () =>
          import('./feature/dashboard/tabs/security-tab/security-tab').then(
            m => m.SecurityTab
          ),
      },
      {
        path: 'performance',
        loadComponent: () =>
          import(
            './feature/dashboard/tabs/performance-tab/performance-tab'
          ).then(m => m.PerformanceTab),
      },
    ],
  },
];
