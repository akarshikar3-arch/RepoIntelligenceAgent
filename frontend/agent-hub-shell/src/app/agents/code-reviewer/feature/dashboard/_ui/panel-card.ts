import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'cr-panel-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel">
      <header>
        @if (icon()) { <span class="material-symbols">{{ icon() }}</span> }
        <h3>{{ title() }}</h3>
        @if (subtitle()) { <span class="subtitle">{{ subtitle() }}</span> }
      </header>
      <div class="body">
        <ng-content />
      </div>
    </section>
  `,
  styles: `
    :host { display: block; }
    :host(.fill) { height: 100%; }
    .panel {
      background: var(--surface-glass, rgba(20,22,36,0.55));
      border: 1px solid var(--surface-border, rgba(255,255,255,0.08));
      border-radius: var(--r-xl, 22px);
      padding: 1.05rem 1.2rem 1.2rem;
      backdrop-filter: blur(20px) saturate(150%);
      -webkit-backdrop-filter: blur(20px) saturate(150%);
      display: grid;
      gap: 0.8rem;
      box-shadow: var(--shadow-1);
      transition:
        border-color var(--dur-2, 200ms) var(--ease-out, ease),
        transform var(--dur-2, 200ms) var(--ease-out, ease),
        box-shadow var(--dur-2, 200ms) var(--ease-out, ease);
    }
    :host(.fill) .panel { height: 100%; }
    .panel:hover {
      border-color: var(--surface-border-strong, rgba(255,255,255,0.16));
      transform: translateY(-1px);
      box-shadow: var(--shadow-2);
    }
    header {
      display: flex;
      align-items: center;
      min-height: 1.65rem;
      gap: 0.55rem;
      padding-bottom: 0.2rem;
      border-bottom: 1px solid color-mix(in srgb, var(--surface-border) 74%, transparent);
    }
    h3 { margin: 0; font-size: 1rem; color: var(--text-1, #fff); letter-spacing: -0.01em; font-weight: 650; }
    .subtitle {
      margin-left: auto;
      font-size: 0.72rem;
      color: var(--text-3, #8f94aa);
      text-transform: uppercase;
      letter-spacing: 0.08em;
      font-weight: 600;
    }
    .material-symbols {
      font-family: 'Material Symbols Rounded', system-ui;
      font-size: 1.05rem;
      color: var(--accent, #7c5cff);
    }
    .body { display: block; }
    :host(.body-scroll) .panel {
      display: grid;
      grid-template-rows: auto minmax(0, 1fr);
    }
    :host(.body-scroll) .body {
      min-height: 0;
      overflow: auto;
      padding-right: 0.2rem;
      scrollbar-width: thin;
      scrollbar-color: rgba(148, 156, 186, 0.45) transparent;
    }
    :host(.body-scroll) .body::-webkit-scrollbar { width: 8px; }
    :host(.body-scroll) .body::-webkit-scrollbar-thumb {
      background: rgba(148, 156, 186, 0.45);
      border-radius: 999px;
    }
  `,
})
export class PanelCard {
  readonly title = input.required<string>();
  readonly subtitle = input<string | null>(null);
  readonly icon = input<string | null>(null);
}
