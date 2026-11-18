import { HttpErrorResponse } from '@angular/common/http';
import { Component, HostListener, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';

import { allFieldMessages } from '../../../core/api-error';
import { AuthService } from '../../../core/auth/auth.service';
import { PASSWORD_HINT } from '../../../core/password-rules';
import { HasUnsavedChanges, warnBeforeClosingTab } from '../../../core/unsaved-changes';
import { fieldsMatch, mismatchErrorMatcher } from '../../../core/validators';

/** Lets a signed-in person change their own password. They must type the current one first. */
@Component({
  selector: 'app-change-password',
  imports: [ReactiveFormsModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  templateUrl: './change-password.html',
  styleUrl: './change-password.css',
})
export class ChangePassword implements HasUnsavedChanges {
  private readonly auth = inject(AuthService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly passwordHint = PASSWORD_HINT;
  protected readonly mismatchMatcher = mismatchErrorMatcher;

  protected readonly form = this.formBuilder.group(
    {
      currentPassword: ['', Validators.required],
      newPassword: ['', Validators.required],
      confirm: ['', Validators.required],
    },
    { validators: fieldsMatch('newPassword', 'confirm') }, // a typing slip must not lock the person out
  );

  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  /** True while something has been typed and not yet saved. */
  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  @HostListener('window:beforeunload', ['$event'])
  protected onBeforeUnload(event: BeforeUnloadEvent): void {
    warnBeforeClosingTab(event, this);
  }

  protected save(): void {
    this.errorMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { currentPassword, newPassword } = this.form.getRawValue();
    this.saving.set(true);

    this.auth.changePassword(currentPassword, newPassword).subscribe({
      next: () => {
        this.saving.set(false);
        this.form.reset();
        this.snackBar.open('Your password was changed. You were signed out on any other device.', 'Dismiss', {
          duration: 8000,
        });
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.showError(error);
      },
    });
  }

  private showError(error: unknown): void {
    const messages = allFieldMessages(error) ?? [];
    const forField = (name: string) => messages.filter((m) => m.field === name).map((m) => m.message);

    const current = forField('currentpassword');
    const next = forField('newpassword');

    if (current.length > 0) {
      this.form.controls.currentPassword.setErrors({ server: current });
      this.form.controls.currentPassword.markAsTouched();
    }
    if (next.length > 0) {
      this.form.controls.newPassword.setErrors({ server: next });
      this.form.controls.newPassword.markAsTouched();
    }

    if (current.length === 0 && next.length === 0) {
      this.errorMessage.set(
        error instanceof HttpErrorResponse && error.status === 0
          ? 'Cannot reach the server. Your password was not changed.'
          : 'Your password could not be changed. Please try again.',
      );
    }
  }

  protected serverMessages(name: 'currentPassword' | 'newPassword'): string[] {
    const messages = this.form.controls[name].errors?.['server'];
    return Array.isArray(messages) ? messages : [];
  }
}
