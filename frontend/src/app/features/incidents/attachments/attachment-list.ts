import { DatePipe } from '@angular/common';
import { Component, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';

import { ConfirmDialog, ConfirmDialogData } from '../../../shared/confirm-dialog/confirm-dialog';
import { Attachment } from '../incident.models';
import { AttachmentService } from './attachment.service';
import { formatBytes } from './attachment-rules';

/** Hands a downloaded file to the browser as a download, under its own name. */
export function saveFile(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
}

/** The attached files of an incident or comment: download each, and remove it if allowed. */
@Component({
  selector: 'app-attachment-list',
  imports: [DatePipe, MatButtonModule, MatIconModule],
  template: `
    @if (attachments().length > 0) {
      <ul class="files" [attr.aria-label]="label()">
        @for (file of attachments(); track file.id) {
          <li>
            <mat-icon class="kind" aria-hidden="true">{{ iconFor(file) }}</mat-icon>
            <div class="info">
              <button
                mat-button
                type="button"
                class="download"
                [disabled]="busyId() === file.id"
                [attr.aria-label]="'Download ' + file.fileName"
                (click)="download(file)"
              >
                {{ file.fileName }}
              </button>
              <div class="meta">
                {{ formatBytes(file.sizeBytes) }} · {{ file.uploadedByEmail ?? 'Unknown user' }} ·
                {{ file.createdAtUtc | date: 'medium' }}
              </div>
            </div>
            @if (file.canRemove) {
              <button
                mat-icon-button
                type="button"
                [disabled]="busyId() === file.id"
                [attr.aria-label]="'Remove ' + file.fileName"
                (click)="confirmRemove(file)"
              >
                <mat-icon aria-hidden="true">delete</mat-icon>
              </button>
            }
          </li>
        }
      </ul>
    }
  `,
  styles: `
    .files {
      margin: 8px 0 0;
      padding: 0;
      list-style: none;
    }

    .files li {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 4px 0;
    }

    .kind {
      flex: none;
      color: var(--mat-sys-on-surface-variant);
    }

    .info {
      flex: 1;
      min-width: 0;
    }

    .download {
      max-width: 100%;
      text-align: left;
      white-space: normal;
      overflow-wrap: anywhere;
    }

    .meta {
      padding-left: 12px;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
  `,
})
export class AttachmentList {
  private readonly attachmentService = inject(AttachmentService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly attachments = input.required<Attachment[]>();

  /** What a screen reader calls this list, for example "Files on this comment". */
  readonly label = input('Attached files');

  /** Tells the page a file was removed, so it can fetch the list again. */
  readonly removed = output<Attachment>();

  protected readonly busyId = signal<string | null>(null);
  protected readonly formatBytes = formatBytes;

  protected iconFor(file: Attachment): string {
    if (file.contentType.startsWith('image/')) {
      return 'image';
    }
    return file.contentType === 'application/pdf' ? 'picture_as_pdf' : 'description';
  }

  protected download(file: Attachment): void {
    this.busyId.set(file.id);
    this.attachmentService.download(file.id).subscribe({
      next: (blob) => {
        this.busyId.set(null);
        saveFile(blob, file.fileName);
      },
      error: () => {
        this.busyId.set(null);
        this.snackBar.open(`${file.fileName} could not be downloaded. Please try again.`, 'Dismiss', { duration: 6000 });
      },
    });
  }

  protected confirmRemove(file: Attachment): void {
    const data: ConfirmDialogData = {
      title: 'Remove this file?',
      message: `${file.fileName} will be removed from the incident. The history will show that it was.`,
      confirmLabel: 'Remove',
    };

    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data })
      .afterClosed()
      .subscribe((confirmed) => {
        if (confirmed) {
          this.remove(file);
        }
      });
  }

  private remove(file: Attachment): void {
    this.busyId.set(file.id);
    this.attachmentService.remove(file.id).subscribe({
      next: () => {
        this.busyId.set(null);
        this.removed.emit(file);
      },
      error: () => {
        this.busyId.set(null);
        this.snackBar.open(`${file.fileName} could not be removed. Please try again.`, 'Dismiss', { duration: 6000 });
      },
    });
  }
}
