import { Component, computed, inject, input, model, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { catchError, of } from 'rxjs';

import { AttachmentSettings } from '../incident.models';
import { AttachmentService } from './attachment.service';
import { fileProblem, formatBytes } from './attachment-rules';

/**
 * "Attach files": lets a person choose files and lists the ones chosen so far. It does not upload anything itself:
 * the page that owns it sends the files when its own Save, Post or Upload button is pressed.
 * It shows nothing at all while attaching is switched off on the site (or while that is still being checked).
 */
@Component({
  selector: 'app-attachment-picker',
  imports: [MatButtonModule, MatIconModule],
  template: `
    @if (settings(); as s) {
      @if (s.enabled) {
        <div class="picker">
          <button mat-stroked-button type="button" [disabled]="disabled()" (click)="chooser.click()">
            <mat-icon aria-hidden="true">attach_file</mat-icon>
            Attach files
          </button>
          <input
            #chooser
            class="chooser"
            type="file"
            multiple
            tabindex="-1"
            aria-label="Choose files to attach"
            [accept]="s.allowedExtensions.join(',')"
            (change)="onChosen($event, s)"
          />
          <span class="hint">
            Up to {{ limitFor(s) }} files, {{ formatBytes(s.maxFileBytes) }} each ({{ s.allowedExtensions.join(' ') }}).
          </span>

          @if (problem(); as message) {
            <p class="problem" role="alert">{{ message }}</p>
          }

          @if (files().length > 0) {
            <ul class="chosen" aria-label="Files that will be attached">
              @for (file of files(); track $index) {
                <li>
                  <span class="name">{{ file.name }}</span>
                  <span class="size">{{ formatBytes(file.size) }}</span>
                  <button
                    mat-icon-button
                    type="button"
                    [disabled]="disabled()"
                    [attr.aria-label]="'Remove ' + file.name + ' from the list'"
                    (click)="removeAt($index)"
                  >
                    <mat-icon aria-hidden="true">close</mat-icon>
                  </button>
                </li>
              }
            </ul>
          }
        </div>
      }
    }
  `,
  styles: `
    .picker {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 8px 12px;
      margin: 4px 0 12px;
    }

    /* Only the button is shown; it opens this. A display:none input cannot be reached by keyboard or screen reader. */
    .chooser {
      display: none;
    }

    .hint {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }

    .problem {
      flex-basis: 100%;
      margin: 0;
      color: var(--mat-sys-error);
      font: var(--mat-sys-body-medium);
      white-space: pre-line;
    }

    .chosen {
      flex-basis: 100%;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .chosen li {
      display: flex;
      align-items: center;
      gap: 8px;
      min-height: 40px;
    }

    .name {
      overflow-wrap: anywhere;
    }

    .size {
      flex: none;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
  `,
})
export class AttachmentPicker {
  private readonly attachments = inject(AttachmentService);

  /** The files chosen so far. The owner of this picker reads them, and empties the list once they are sent. */
  readonly files = model<File[]>([]);

  /** Whether the files are for a comment (a smaller limit) or for the incident itself. */
  readonly kind = input<'comment' | 'incident'>('incident');

  /** Switch the whole control off while the page is busy sending. */
  readonly disabled = input(false);

  /** The limits, or null while they are loading or could not be loaded (then nothing is offered). */
  protected readonly settings = toSignal(this.attachments.getSettings().pipe(catchError(() => of(null))), {
    initialValue: null,
  });

  protected readonly problem = signal<string | null>(null);
  protected readonly formatBytes = formatBytes;

  private readonly limit = computed(() => {
    const s = this.settings();
    return !s ? 0 : this.kind() === 'comment' ? s.maxFilesPerComment : s.maxFilesPerIncident;
  });

  protected limitFor(settings: AttachmentSettings): number {
    return this.kind() === 'comment' ? settings.maxFilesPerComment : settings.maxFilesPerIncident;
  }

  protected onChosen(event: Event, settings: AttachmentSettings): void {
    const input = event.target as HTMLInputElement;
    const chosen = Array.from(input.files ?? []);
    input.value = ''; // so choosing the same file again later still counts as a change

    const kept = [...this.files()];
    const problems: string[] = [];

    for (const file of chosen) {
      const already = kept.some((k) => k.name === file.name && k.size === file.size);
      const wrong = fileProblem(file, settings);

      if (already) {
        continue;
      }
      if (wrong) {
        problems.push(wrong);
      } else if (kept.length >= this.limit()) {
        problems.push(`${file.name}: you can attach at most ${this.limit()} files here.`);
      } else {
        kept.push(file);
      }
    }

    this.files.set(kept);
    this.problem.set(problems.length > 0 ? problems.join('\n') : null);
  }

  protected removeAt(index: number): void {
    this.files.update((list) => list.filter((_, i) => i !== index));
    this.problem.set(null);
  }
}
