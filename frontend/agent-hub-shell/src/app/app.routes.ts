import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'agents/code-reviewer' },
  {
    path: 'agents/code-reviewer',
    loadChildren: () =>
      import('./agents/code-reviewer/code-reviewer.routes').then(
        m => m.CODE_REVIEWER_ROUTES
      ),
  },
  { path: '**', redirectTo: 'agents/code-reviewer' },
];
