import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { DashboardApi } from '../../data-access/api/dashboard.api';

type Mode = 'repo' | 'snippet';
type ReviewProfile = 'strict' | 'balanced' | 'relaxed';

interface SnippetDraft {
  name: string;
  language: string;
  content: string;
}

@Component({
  selector: 'cr-landing',
  standalone: true,
  imports: [FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './landing.page.html',
  styleUrl: './landing.page.scss',
})
export class LandingPage implements AfterViewInit {
  private readonly api = inject(DashboardApi);
  private readonly reviewProfile: ReviewProfile = 'balanced';
  @ViewChild('repoUrlInput') private readonly repoUrlInput?: ElementRef<HTMLInputElement>;

  protected readonly mode = signal<Mode>('repo');
  protected readonly repoUrl = signal('');
  protected readonly branch = signal('');
  protected readonly snippets = signal<SnippetDraft[]>([
    { name: 'example.ts', language: 'typescript', content: '' },
  ]);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor(private readonly router: Router) {}

  ngAfterViewInit(): void {
    // Some browsers autofill URL fields without emitting ngModel/input events.
    // Sync from DOM after paint so Analyze enables correctly on first click.
    queueMicrotask(() => this.syncRepoUrlFromDom());
    setTimeout(() => this.syncRepoUrlFromDom(), 200);
  }

  protected setMode(mode: Mode): void {
    this.mode.set(mode);
  }

  protected addSnippet(): void {
    this.snippets.update(list => [
      ...list,
      { name: `file-${list.length + 1}.ts`, language: 'typescript', content: '' },
    ]);
  }

  protected removeSnippet(idx: number): void {
    this.snippets.update(list => list.filter((_, i) => i !== idx));
  }

  protected updateSnippet(idx: number, patch: Partial<SnippetDraft>): void {
    this.snippets.update(list =>
      list.map((s, i) => (i === idx ? { ...s, ...patch } : s))
    );
  }

  protected canSubmit(): boolean {
    if (this.mode() === 'repo') {
      return /^https?:\/\/(www\.)?github\.com\/[^/]+\/[^/]+/i.test(this.repoUrl());
    }
    return this.snippets().some(s => s.content.trim().length > 0);
  }

  protected async submit(): Promise<void> {
    if (!this.canSubmit() || this.submitting()) return;
    this.submitting.set(true);
    this.error.set(null);
    try {
      let sessionId: string;
      if (this.mode() === 'repo') {
        const res = await firstValueFrom(
          this.api.ingestRepo(
            this.repoUrl().trim(),
            this.branch().trim() || undefined,
            this.reviewProfile
          )
        );
        sessionId = res.sessionId;
      } else {
        const files = this.snippets()
          .filter(s => s.content.trim().length > 0)
          .map(s => ({ name: s.name, language: s.language || null, content: s.content }));
        const res = await firstValueFrom(this.api.ingestSnippet(files, this.reviewProfile));
        sessionId = res.sessionId;
      }
      await this.router.navigate([
        '/agents/code-reviewer/session',
        sessionId,
        'overview',
      ]);
    } catch (err: unknown) {
      const msg =
        (err as { error?: { detail?: string }; message?: string })?.error?.detail ??
        (err as { message?: string })?.message ??
        'Ingest failed';
      this.error.set(msg);
    } finally {
      this.submitting.set(false);
    }
  }

  private syncRepoUrlFromDom(): void {
    const domValue = this.repoUrlInput?.nativeElement.value?.trim() ?? '';
    if (domValue && domValue !== this.repoUrl()) {
      this.repoUrl.set(domValue);
    }
  }
}
