import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import { fieldErrors } from '../../../core/api-error';
import { Role } from '../../../core/auth/auth.models';
import { PASSWORD_HINT } from '../../../core/password-rules';
import { emailWithDomain } from '../../../core/validators';
import { UserSummary } from '../../../core/user.models';
import { AdminService } from '../admin.service';

export const ROLES: Role[] = ['Employee', 'Manager', 'Admin'];

/** Creates a person with a chosen role. Closes with the new user, or nothing if cancelled. */
@Component({
  selector: 'app-create-user-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>New user</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="fields">
        <mat-form-field appearance="outline">
          <mat-label>Email</mat-label>
          <input matInput type="email" formControlName="email" autocomplete="off" />
          @if (form.controls.email.hasError('required')) {
            <mat-error>An email is required.</mat-error>
          } @else if (form.controls.email.hasError('email')) {
            <mat-error>Enter a full email address, like name@example.com.</mat-error>
          } @else if (serverMessage('email'); as message) {
            <mat-error>{{ message }}</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Password</mat-label>
          <input matInput type="password" formControlName="password" autocomplete="new-password" />
          <mat-hint>{{ passwordHint }}</mat-hint>
          @if (form.controls.password.hasError('required')) {
            <mat-error>A password is required.</mat-error>
          } @else if (serverMessage('password'); as message) {
            <mat-error>{{ message }}</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Role</mat-label>
          <mat-select formControlName="role">
            @for (role of roles; track role) {
              <mat-option [value]="role">{{ role }}</mat-option>
            }
          </mat-select>
          @if (serverMessage('role'); as message) {
            <mat-error>{{ message }}</mat-error>
          }
        </mat-form-field>

        @if (errorMessage(); as message) {
          <p class="error" role="alert">{{ message }}</p>
        }
      </mat-dialog-content>

      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button mat-flat-button type="submit" [disabled]="saving()">{{ saving() ? 'Creating…' : 'Create user' }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .fields { display: flex; flex-direction: column; gap: 4px; min-width: 320px; padding-top: 8px; }
    .error { color: var(--mat-sys-error); margin: 0; }
  `,
})
export class CreateUserDialog {
  private readonly admin = inject(AdminService);
  private readonly ref = inject<MatDialogRef<CreateUserDialog, UserSummary>>(MatDialogRef);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly roles = ROLES;
  protected readonly passwordHint = PASSWORD_HINT;

  protected readonly form = this.formBuilder.group({
    email: ['', [Validators.required, emailWithDomain]],
    password: ['', Validators.required],
    role: ['Employee' as Role, Validators.required],
  });

  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected save(): void {
    this.errorMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);

    this.admin.createUser(this.form.getRawValue()).subscribe({
      next: (user) => this.ref.close(user),
      error: (error: unknown) => {
        this.saving.set(false);
        this.showError(error);
      },
    });
  }

  /** Puts each complaint beside the field it is about. Anything else goes in a general message. */
  private showError(error: unknown): void {
    const problems = fieldErrors(error);
    let placed = false;

    for (const name of ['email', 'password', 'role'] as const) {
      if (problems?.[name]) {
        this.form.controls[name].setErrors({ server: problems[name] });
        this.form.controls[name].markAsTouched();
        placed = true;
      }
    }

    if (!placed) {
      this.errorMessage.set(
        error instanceof HttpErrorResponse && error.status === 0
          ? 'Cannot reach the server. The user was not created.'
          : 'The user could not be created. Please try again.',
      );
    }
  }

  protected serverMessage(name: 'email' | 'password' | 'role'): string | null {
    const message = this.form.controls[name].errors?.['server'];
    return typeof message === 'string' ? message : null;
  }
}
