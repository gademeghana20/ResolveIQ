import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, input, output, signal, viewChild } from '@angular/core';
import { FormGroupDirective, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { notBlank } from '../../../core/validators';
import { IncidentComment } from '../incident.models';
import { IncidentService } from '../incident.service';

/** Matches the API's limit, so the screen warns before the server has to. */
export const MAX_COMMENT_LENGTH = 2000;

@Component({
  selector: 'app-comment-form',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  templateUrl: './comment-form.html',
  styleUrl: './comment-form.css',
})
export class CommentForm {
  private readonly incidents = inject(IncidentService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly incidentId = input.required<string>();

  private readonly formDirective = viewChild(FormGroupDirective);

  /** Tells the page a comment was saved, handing over the saved comment. */
  readonly added = output<IncidentComment>();

  protected readonly maxLength = MAX_COMMENT_LENGTH;
  protected readonly text = this.formBuilder.control('', [notBlank, Validators.maxLength(MAX_COMMENT_LENGTH)]);

  // A form directive is what makes (ngSubmit) work and stops the browser doing a plain page-reloading submit.
  protected readonly form = this.formBuilder.group({ text: this.text });
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  /** True while a comment has been typed but not posted. */
  hasDraft(): boolean {
    return this.text.value.trim().length > 0;
  }

  protected submit(): void {
    this.errorMessage.set(null);

    if (this.text.invalid) {
      this.text.markAsTouched();
      return;
    }

    this.submitting.set(true);

    this.incidents.addComment(this.incidentId(), this.text.value.trim()).subscribe({
      next: (comment) => {
        this.submitting.set(false);
        // Reset through the form directive, which also forgets "this form was submitted".
        // Resetting only the control would leave the empty box showing a red error.
        this.formDirective()?.resetForm({ text: '' });
        this.added.emit(comment);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(this.messageFor(error));
      },
    });
  }

  private messageFor(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) {
        return 'Cannot reach the server. Your comment was not saved.';
      }
      if (error.status === 404) {
        return 'This incident is no longer available.';
      }
      // The API explains what was wrong, e.g. "Comments are closed on a closed incident."
      const reason = error.error?.errors?.['Text']?.[0];
      if (error.status === 400 && typeof reason === 'string') {
        return reason;
      }
    }
    return 'Your comment could not be saved. Please try again.';
  }
}
