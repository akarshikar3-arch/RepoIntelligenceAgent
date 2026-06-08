import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TabPlaceholder } from '../_shared/tab-placeholder';

@Component({
  selector: 'cr-docs-tab',
  standalone: true,
  imports: [TabPlaceholder],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<cr-tab-placeholder title="Documentation" icon="menu_book" />`,
})
export class DocsTab {}
