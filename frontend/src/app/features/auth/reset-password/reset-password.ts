import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { allFieldMessages } from '../../../core/api-error';
import { AuthService } from '../../../core/auth/auth.service';
import { PASSWORD_HINT } from '../../../core/password-rules';
import { fieldsMatch, mismatchErrorMatcher } from '../../../core/validators';
import { ThemeToggle } from '../../../shared/theme-toggle/theme-toggle';
import { AuthBrand } from '../auth-brand/auth-brand';

/**
 * Where the emailed link lands (/reset-password?email=…&token=…). The person chooses a new password. The page never
 * signs anyone in: afterwards they go to the sign-in page and use the new password.
 */
@Component({
  selector: 'app-reset-password',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    AuthBrand,
    ThemeToggle,
  ],
  templateUrl: './reset-password.html',
  styleUrl: '../auth-page.css',
})
export class ResetPassword {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  private readonly email: string | null;
  private readonly token: string | null;

  protected readonly passwordHint = PASSWORD_HINT;
  protected readonly mismatchMatcher = mismatchErrorMatcher;

  protected readonly form = this.formBuilder.group(
    {
      newPassword: ['', Validators.required],
      confirm: ['', Validators.required],
    },
    { validators: fieldsMatch('newPassword', 'confirm') }, // a typing slip must not lock the person out
  );

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  /** True when the link did not carry what is needed, or the server said it is no longer valid. */
  protected readonly linkProblem = signal(false);

  constructor() {
    const params = inject(ActivatedRoute).snapshot.queryParamMap;
    this.email = params.get('email');
    this.token = params.get('token');
    this.linkProblem.set(!this.email || !this.token);
  }

  protected submit(): void {
    this.errorMessage.set(null);

    if (this.form.invalid || !this.email || !this.token) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);

    this.auth.resetPassword(this.email, this.token, this.form.controls.newPassword.value).subscribe({
      next: () => {
        this.snackBar.open('Your password was changed. Sign in with the new one.', 'Dismiss', { duration: 8000 });
        this.router.navigateByUrl('/login');
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.showError(error);
      },
    });
  }

  private showError(error: unknown): void {
    const messages = allFieldMessages(error);

    if (messages?.some((m) => m.field === 'token')) {
      this.linkProblem.set(true); // the link is used up or has expired: offer a fresh one
      return;
    }

    const passwordMessages = messages?.filter((m) => m.field === 'newpassword').map((m) => m.message) ?? [];
    if (passwordMessages.length > 0) {
      this.form.controls.newPassword.setErrors({ server: passwordMessages });
      this.form.controls.newPassword.markAsTouched();
      return;
    }

    this.errorMessage.set(
      error instanceof HttpErrorResponse && error.status === 0
        ? 'Cannot reach the server. Your password was not changed.'
        : 'Your password could not be changed. Please try again.',
    );
  }

  protected serverMessages(): string[] {
    const messages = this.form.controls.newPassword.errors?.['server'];
    return Array.isArray(messages) ? messages : [];
  }
}
