import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map, startWith } from 'rxjs';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../../state/session.store';
import { ChatPanel } from '../chat/chat-panel';
import { ChatCitation } from '../../data-access/models/chat.models';
import { DashboardApi } from '../../data-access/api/dashboard.api';

interface TabDef {
  path: string;
  label: string;
  icon: string;
}

const TABS: TabDef[] = [
  { path: 'overview', label: 'Overview', icon: 'dashboard' },
  { path: 'architecture', label: 'Architecture', icon: 'hub' },
  { path: 'quality', label: 'Code Quality', icon: 'rule' },
  { path: 'security', label: 'Security', icon: 'shield' },
  { path: 'performance', label: 'Performance', icon: 'speed' },
];

@Component({
  selector: 'cr-dashboard-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ChatPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard-shell.page.html',
  styleUrl: './dashboard-shell.page.scss',
})
export class DashboardShellPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(DashboardApi);
  protected readonly store = inject(SessionStore);

  protected readonly tabs = TABS;
  protected readonly chatOpen = signal(true);
  protected readonly sessionMissing = signal(false);

  private readonly params = toSignal(this.route.paramMap, {
    initialValue: this.route.snapshot.paramMap,
  });
  private readonly activeTabSignal = toSignal(
    this.router.events.pipe(
      startWith(null),
      map(() => this.resolveActiveTab())
    ),
    { initialValue: this.resolveActiveTab() }
  );
  protected readonly sessionId = computed(() => this.params().get('sessionId') ?? '');
  protected readonly activeTab = computed(() => this.activeTabSignal());
  protected readonly isChatReady = computed(() => this.store.summary()?.status === 'ready');
  protected readonly isSummaryReady = computed(() => this.store.summary()?.status === 'ready');
  private readonly inferredFramework = computed(() => {
    const summary = this.store.summary();
    const framework = (summary?.framework ?? '').trim();
    const lang = (summary?.primaryLanguage ?? '').toLowerCase();

    if (framework && framework.toLowerCase() !== 'unknown') return framework;
    if (lang === 'javascript' || lang === 'typescript') return 'Node.js (inferred)';
    if (lang === 'c#' || lang === 'f#') return '.NET (inferred)';
    if (lang === 'go') return 'Go services (inferred)';
    if (lang === 'python') return 'Python app (inferred)';
    return 'Unknown';
  });
  protected readonly frameworkLabel = computed(() =>
    this.isSummaryReady()
      ? this.inferredFramework()
      : 'Detecting…'
  );
  protected readonly languageLabel = computed(() =>
    this.isSummaryReady()
      ? (this.store.summary()?.primaryLanguage ?? '—')
      : 'Detecting…'
  );
  protected readonly healthLabel = computed(() =>
    this.isSummaryReady()
      ? (this.store.summary()?.healthScore ?? '—')
      : '…'
  );
  protected readonly confidenceLabel = computed(() =>
    this.isSummaryReady()
      ? (this.store.summary()?.confidenceScore ?? '—')
      : '…'
  );

  constructor() {
    effect(() => {
      const id = this.sessionId();
      if (!id) {
        this.sessionMissing.set(true);
        return;
      }

      void this.checkSession(id);
    });
  }

  protected toggleChat(): void {
    this.chatOpen.update(v => !v);
  }

  protected startAnotherReview(): void {
    this.store.reset();
    void this.router.navigate(['/agents/code-reviewer']);
  }

  protected onCitationClicked(citation: ChatCitation): void {
    // Future: deep-link into the file explorer / open in viewer.
    // For now, surface the citation in the URL hash so it survives navigation.
    if (typeof window !== 'undefined') {
      const url = new URL(window.location.href);
      url.hash = `#file=${encodeURIComponent(citation.file)}&L${citation.startLine}-${citation.endLine}`;
      window.history.replaceState({}, '', url.toString());
    }
  }

  private resolveActiveTab(): string {
    // During initial route activation, firstChild/snapshot can be transiently undefined.
    // Parse from current URL first, then fall back to activated child snapshot.
    const cleanUrl = this.router.url.split('?')[0].split('#')[0];
    const segments = cleanUrl.split('/').filter(Boolean);
    const sessionIdx = segments.indexOf('session');
    if (sessionIdx >= 0 && segments.length > sessionIdx + 2) {
      return segments[sessionIdx + 2] || 'overview';
    }

    return this.route.firstChild?.snapshot?.url?.[0]?.path ?? 'overview';
  }

  private async checkSession(sessionId: string): Promise<void> {
    try {
      await firstValueFrom(this.api.getSession(sessionId));
      this.sessionMissing.set(false);
    } catch (err: unknown) {
      const status = (err as { status?: number })?.status;
      this.sessionMissing.set(status === 404);
    }
  }
}
