import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TabPlaceholder } from '../_shared/tab-placeholder';

@Component({
  selector: 'cr-testing-tab',
  standalone: true,
  imports: [TabPlaceholder],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<cr-tab-placeholder title="Testing" icon="science" />`,
})
export class TestingTab {}
