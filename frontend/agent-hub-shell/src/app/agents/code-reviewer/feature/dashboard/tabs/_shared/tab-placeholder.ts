import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'cr-tab-placeholder',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="placeholder">
      <span class="material-symbols">{{ icon() }}</span>
      <h3>{{ title() }}</h3>
      <p>{{ message() }}</p>
    </section>
  `,
  styles: `
    :host { display: block; }
    .placeholder {
      display: grid;
      place-items: center;
      gap: 0.5rem;
      padding: 3rem 1rem;
      border-radius: 18px;
      background: var(--surface-glass, rgba(20,22,36,0.45));
      border: 1px dashed var(--surface-border, rgba(255,255,255,0.1));
      color: var(--text-2, #b8bdd1);
      text-align: center;
    }
    .material-symbols {
      font-family: 'Material Symbols Rounded', system-ui;
      font-size: 2.4rem;
      color: var(--accent, #7c5cff);
    }
    h3 { margin: 0; color: var(--text-1, #fff); }
    p { margin: 0; max-width: 48ch; font-size: 0.9rem; }
  `,
})
export class TabPlaceholder {
  readonly title = input.required<string>();
  readonly message = input<string>('Populated in a later implementation phase.');
  readonly icon = input<string>('hourglass_top');
}
