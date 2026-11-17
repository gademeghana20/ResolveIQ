import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { fieldErrors } from '../../../core/api-error';
import { formatHours } from '../../../shared/format';
import { AdminService } from '../admin.service';
import { MAX_TARGET_HOURS, MIN_TARGET_HOURS, SlaPolicy } from './sla.models';

export interface SlaPolicyDialogData {
  policy: SlaPolicy;
}

/** Changes the deadline for one priority. Saves by itself, and closes with the saved policy. */
@Component({
  selector: 'app-sla-policy-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatCheckboxModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.policy.priority }} deadline</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="fields">
        <p class="warning">
          The new deadline applies to {{ data.policy.priority }} incidents <strong>from now on</strong>: ones assigned for
          the first time, or whose priority is changed. Incidents already running keep the deadline they were given, and
          finished ones keep their result.
        </p>

        <mat-form-field appearance="outline">
          <mat-label>Hours to resolve</mat-label>
          <input matInput type="number" inputmode="numeric" formControlName="hours" min="1" max="720" step="1" />
          <mat-hint>{{ hoursHint() }}</mat-hint>
          @if (form.controls.hours.hasError('required')) {
            <mat-error>Enter a number of hours.</mat-error>
          } @else if (form.controls.hours.hasError('min') || form.controls.hours.hasError('max')) {
            <mat-error>Choose between {{ min }} and {{ max }} hours.</mat-error>
          } @else if (form.controls.hours.hasError('pattern')) {
            <mat-error>Use a whole number of hours.</mat-error>
          } @else if (serverMessage(); as message) {
            <mat-error>{{ message }}</mat-error>
          }
        </mat-form-field>

        <mat-checkbox formControlName="isActive">Give {{ data.policy.priority }} incidents a deadline</mat-checkbox>
        @if (!form.controls.isActive.value) {
          <p class="hint">
            With this off, {{ data.policy.priority }} incidents that start their clock from now on show "No SLA" and can
            never be late. Ones already running keep their deadline.
          </p>
        }

        @if (errorMessage(); as message) {
          <p class="error" role="alert">{{ message }}</p>
        }
      </mat-dialog-content>

      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button mat-flat-button type="submit" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save deadline' }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .fields { display: flex; flex-direction: column; gap: 4px; min-width: 340px; max-width: 440px; }
    .warning { margin: 0 0 12px; color: var(--mat-sys-on-surface-variant); }
    .hint { margin: 4px 0 0; color: var(--mat-sys-on-surface-variant); }
    .error { color: var(--mat-sys-error); margin: 0; }
  `,
})
export class SlaPolicyDialog {
  private readonly admin = inject(AdminService);
  private readonly ref = inject<MatDialogRef<SlaPolicyDialog, SlaPolicy>>(MatDialogRef);
  private readonly formBuilder = inject(NonNullableFormBuilder);
  protected readonly data = inject<SlaPolicyDialogData>(MAT_DIALOG_DATA);

  protected readonly min = MIN_TARGET_HOURS;
  protected readonly max = MAX_TARGET_HOURS;

  protected readonly form = this.formBuilder.group({
    hours: [
      this.data.policy.resolutionTargetHours as number | null,
      [Validators.required, Validators.min(MIN_TARGET_HOURS), Validators.max(MAX_TARGET_HOURS), Validators.pattern(/^\d+$/)],
    ],
    isActive: [this.data.policy.isActive],
  });

  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  /** "That is 1 d 6 h" helps people think in days, not just hours. */
  protected hoursHint(): string {
    const hours = this.form.controls.hours.value;
    const valid = hours !== null && Number.isInteger(hours) && hours >= MIN_TARGET_HOURS && hours <= MAX_TARGET_HOURS;
    return valid ? `That is ${formatHours(hours)}. The limit is ${MAX_TARGET_HOURS} hours (30 days).` : `Between ${MIN_TARGET_HOURS} and ${MAX_TARGET_HOURS} hours.`;
  }

  protected save(): void {
    this.errorMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);

    this.admin
      .updateSlaPolicy(this.data.policy.priority, {
        resolutionTargetHours: Number(this.form.controls.hours.value),
        isActive: this.form.controls.isActive.value,
      })
      .subscribe({
        next: (saved) => this.ref.close(saved),
        error: (error: unknown) => {
          this.saving.set(false);
          const problem = fieldErrors(error)?.['resolutiontargethours'];

          if (problem) {
            this.form.controls.hours.setErrors({ server: problem });
            this.form.controls.hours.markAsTouched();
          } else {
            const status = error instanceof HttpErrorResponse ? error.status : undefined;
            this.errorMessage.set(
              status === 0
                ? 'Cannot reach the server. Nothing was saved.'
                : status === 404
                  ? 'This policy no longer exists. Close this box and refresh the page.'
                  : 'The deadline could not be saved. Please try again.',
            );
          }
        },
      });
  }

  protected serverMessage(): string | null {
    const message = this.form.controls.hours.errors?.['server'];
    return typeof message === 'string' ? message : null;
  }
}
