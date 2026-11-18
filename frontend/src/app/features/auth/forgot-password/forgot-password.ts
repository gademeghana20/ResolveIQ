import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { ThemeToggle } from '../../../shared/theme-toggle/theme-toggle';
import { AuthBrand } from '../auth-brand/auth-brand';

/**
 * "Forgot your password?": the person types their email and, if an account exists, is emailed a link to choose a
 * new one. The page says the same thing either way, so it cannot be used to find out who has an account.
 */
@Component({
  selector: 'app-forgot-password',
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
  templateUrl: './forgot-password.html',
  styleUrl: '../auth-page.css',
})
export class ForgotPassword {
  private readonly auth = inject(AuthService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly form = this.formBuilder.group({
    email: ['', [Validators.required, Validators.email]],
  });

  protected readonly submitting = signal(false);
  protected readonly sent = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected submit(): void {
    this.errorMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);

    this.auth.forgotPassword(this.form.controls.email.value.trim()).subscribe({
      next: () => {
        this.submitting.set(false);
        this.sent.set(true);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(
          error instanceof HttpErrorResponse && error.status === 0
            ? 'Cannot reach the server. Please check that the API is running.'
            : error instanceof HttpErrorResponse && error.status === 429
              ? 'Too many requests. Please wait a minute and try again.'
              : 'Something went wrong. Please try again.',
        );
      },
    });
  }
}
