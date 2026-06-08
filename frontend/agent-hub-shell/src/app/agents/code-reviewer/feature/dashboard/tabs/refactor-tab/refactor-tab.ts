import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TabPlaceholder } from '../_shared/tab-placeholder';

@Component({
  selector: 'cr-refactor-tab',
  standalone: true,
  imports: [TabPlaceholder],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<cr-tab-placeholder title="Refactoring" icon="auto_fix_high" />`,
})
export class RefactorTab {}
