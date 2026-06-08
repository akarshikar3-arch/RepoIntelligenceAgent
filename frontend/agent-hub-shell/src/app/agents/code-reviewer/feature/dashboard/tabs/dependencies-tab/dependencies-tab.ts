import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TabPlaceholder } from '../_shared/tab-placeholder';

@Component({
  selector: 'cr-dependencies-tab',
  standalone: true,
  imports: [TabPlaceholder],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<cr-tab-placeholder title="Dependencies" icon="inventory_2" />`,
})
export class DependenciesTab {}
