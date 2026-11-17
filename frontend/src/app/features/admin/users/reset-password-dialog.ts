import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { fieldErrors } from '../../../core/api-error';
import { PASSWORD_HINT } from '../../../core/password-rules';
import { fieldsMatch, mismatchErrorMatcher } from '../../../core/validators';
import { AdminService } from '../admin.service';

export interface ResetPasswordDialogData {
  userId: string;
  email: string;
}

/** Gives a person a new password. Closes with true once it is saved. */
@Component({
  selector: 'app-reset-password-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Reset password</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="fields">
        <p>
          Choose a new password for <strong>{{ data.email }}</strong
          >. They are signed out everywhere and must use the new password next time. Tell them what it is.
        </p>

        <mat-form-field appearance="outline">
          <mat-label>New password</mat-label>
          <input matInput type="password" formControlName="newPassword" autocomplete="new-password" />
          <mat-hint>{{ passwordHint }}</mat-hint>
          @if (form.controls.newPassword.hasError('required')) {
            <mat-error>A password is required.</mat-error>
          } @else if (serverMessage(); as message) {
            <mat-error>{{ message }}</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Type it again</mat-label>
          <input
            matInput
            type="password"
            formControlName="confirm"
            autocomplete="new-password"
            [errorStateMatcher]="mismatchMatcher"
          />
          @if (form.hasError('mismatch') && form.controls.confirm.touched) {
            <mat-error>The two passwords do not match.</mat-error>
          }
        </mat-form-field>

        @if (errorMessage(); as message) {
          <p class="error" role="alert">{{ message }}</p>
        }
      </mat-dialog-content>

      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button mat-flat-button type="submit" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Reset password' }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .fields { display: flex; flex-direction: column; gap: 4px; min-width: 320px; max-width: 420px; }
    .error { color: var(--mat-sys-error); margin: 0; }
  `,
})
export class ResetPasswordDialog {
  private readonly admin = inject(AdminService);
  private readonly ref = inject<MatDialogRef<ResetPasswordDialog, boolean>>(MatDialogRef);
  private readonly formBuilder = inject(NonNullableFormBuilder);
  protected readonly data = inject<ResetPasswordDialogData>(MAT_DIALOG_DATA);

  protected readonly passwordHint = PASSWORD_HINT;

  // Lets the second box show the form's "do not match" error (see mismatchErrorMatcher).
  protected readonly mismatchMatcher = mismatchErrorMatcher;

  protected readonly form = this.formBuilder.group(
    {
      newPassword: ['', Validators.required],
      confirm: ['', Validators.required],
    },
    { validators: fieldsMatch('newPassword', 'confirm') }, // a typing slip must not lock the person out
  );

  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected save(): void {
    this.errorMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);

    this.admin.resetPassword(this.data.userId, this.form.controls.newPassword.value).subscribe({
      next: () => this.ref.close(true),
      error: (error: unknown) => {
        this.saving.set(false);
        const problem = fieldErrors(error)?.['newpassword'];

        if (problem) {
          this.form.controls.newPassword.setErrors({ server: problem });
          this.form.controls.newPassword.markAsTouched();
        } else {
          this.errorMessage.set(
            error instanceof HttpErrorResponse && error.status === 0
              ? 'Cannot reach the server. The password was not changed.'
              : 'The password could not be changed. Please try again.',
          );
        }
      },
    });
  }

  protected serverMessage(): string | null {
    const message = this.form.controls.newPassword.errors?.['server'];
    return typeof message === 'string' ? message : null;
  }
}
