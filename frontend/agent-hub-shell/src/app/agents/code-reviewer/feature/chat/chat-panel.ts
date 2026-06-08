import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  output,
  signal,
  ViewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { DashboardApi } from '../../data-access/api/dashboard.api';
import {
  ChatCitation,
  ChatMessageDto,
  ChatTurn,
} from '../../data-access/models/chat.models';

@Component({
  selector: 'cr-chat-panel',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './chat-panel.html',
  styleUrl: './chat-panel.scss',
})
export class ChatPanel {
  private readonly api = inject(DashboardApi);
  private readonly destroyRef = inject(DestroyRef);
  private cooldownTimer: ReturnType<typeof setTimeout> | null = null;

  private static readonly TAB_PROMPTS: Record<string, string[]> = {
    overview: [
      'What does this project do?',
      'What tech stack is used?',
      'What should I improve before production?',
    ],
    architecture: [
      'What is the main entry point?',
      'How are major modules connected?',
      'Which components are most coupled?',
    ],
    quality: [
      'What are the top code quality issues?',
      'Which files have most TS-ANY usage?',
      'What should be fixed first for maintainability?',
    ],
    security: [
      'List all security findings.',
      'Which findings affect production code?',
      'What are the highest risk files?',
    ],
    performance: [
      'What are the biggest performance hotspots?',
      'Which files are largest and why risky?',
      'What can improve performance before production?',
    ],
  };

  readonly sessionId = input.required<string>();
  readonly activeTab = input<string>('overview');
  readonly disabled = input<boolean>(false);
  readonly citationClicked = output<ChatCitation>();

  protected readonly turns = signal<ChatTurn[]>([]);
  protected readonly draft = signal('');
  protected readonly busy = signal(false);
  protected readonly cooldownActive = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly quickPrompts = computed(() => {
    const key = (this.activeTab() || 'overview').toLowerCase();
    return ChatPanel.TAB_PROMPTS[key] ?? ChatPanel.TAB_PROMPTS['overview'];
  });
  protected readonly hintQuestions = computed(() => this.quickPrompts().slice(0, 3));

  protected readonly canSend = computed(
    () => !this.busy() && !this.cooldownActive() && !this.disabled() && this.draft().trim().length > 0
  );
  protected readonly composerHint = computed(() =>
    this.cooldownActive()
      ? 'Please wait a moment before sending the next question.'
      : 'Ask about stack, APIs, findings, entrypoints, or file responsibilities.'
  );

  @ViewChild('scrollHost') private scrollHost?: ElementRef<HTMLElement>;

  constructor() {
    this.destroyRef.onDestroy(() => {
      if (this.cooldownTimer) clearTimeout(this.cooldownTimer);
    });

    effect(() => {
      const id = this.sessionId();
      if (!id) return;
      this.loadHistory(id);
    });

    effect(() => {
      // Scroll on new turn / busy flip.
      this.turns();
      this.busy();
      queueMicrotask(() => {
        const el = this.scrollHost?.nativeElement;
        if (el) el.scrollTop = el.scrollHeight;
      });
    });
  }

  protected onCitation(c: ChatCitation): void {
    this.citationClicked.emit(c);
  }

  protected async send(): Promise<void> {
    if (!this.canSend()) return;
    const id = this.sessionId();
    const question = this.draft().trim();

    this.draft.set('');
    this.error.set(null);
    this.startCooldown();

    const history: ChatMessageDto[] = this.turns().map(t => ({
      role: t.role,
      content: t.content,
    }));

    this.turns.update(prev => [...prev, { role: 'user', content: question }]);
    this.busy.set(true);

    try {
      const resp = await firstValueFrom(
        this.api.ask(id, { question, history })
      );
      this.turns.update(prev => [
        ...prev,
        {
          role: 'assistant',
          content: resp.content,
          citations: resp.citations ?? null,
        },
      ]);
    } catch (err: unknown) {
      const msg =
        (err as { error?: { detail?: string }; message?: string })?.error
          ?.detail ??
        (err as { message?: string })?.message ??
        'Chat request failed.';
      this.error.set(msg);
      this.turns.update(prev => [
        ...prev,
        { role: 'assistant', content: `[error] ${msg}` },
      ]);
    } finally {
      this.busy.set(false);
    }
  }

  protected async clear(): Promise<void> {
    const id = this.sessionId();
    try {
      await firstValueFrom(this.api.clearChatHistory(id));
    } catch {
      /* non-fatal */
    }
    this.turns.set([]);
    this.error.set(null);
  }

  protected onComposerKey(ev: KeyboardEvent): void {
    if (ev.key === 'Enter' && !ev.shiftKey) {
      ev.preventDefault();
      void this.send();
    }
  }

  protected usePrompt(prompt: string): void {
    if (this.busy() || this.cooldownActive() || this.disabled()) return;
    this.draft.set(prompt);
    void this.send();
  }

  private async loadHistory(id: string): Promise<void> {
    try {
      const history = await firstValueFrom(this.api.getChatHistory(id));
      this.turns.set(history ?? []);
    } catch {
      this.turns.set([]);
    }
  }

  private startCooldown(): void {
    if (this.cooldownTimer) clearTimeout(this.cooldownTimer);
    this.cooldownActive.set(true);
    this.cooldownTimer = setTimeout(() => {
      this.cooldownActive.set(false);
      this.cooldownTimer = null;
    }, 1200);
  }
}
