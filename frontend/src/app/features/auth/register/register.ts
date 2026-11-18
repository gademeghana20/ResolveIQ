import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { catchError, of } from 'rxjs';

import { allFieldMessages } from '../../../core/api-error';
import { RegistrationStatus } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { PASSWORD_HINT } from '../../../core/password-rules';
import { emailWithDomain, fieldsMatch, mismatchErrorMatcher } from '../../../core/validators';
import { ThemeToggle } from '../../../shared/theme-toggle/theme-toggle';
import { AuthBrand } from '../auth-brand/auth-brand';

type Problems = { email: string[]; password: string[]; other: string[] };

@Component({
  selector: 'app-register',
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
  templateUrl: './register.html',
  styleUrl: '../auth-page.css',
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly passwordHint = PASSWORD_HINT;
  protected readonly mismatchMatcher = mismatchErrorMatcher;

  protected readonly form = this.formBuilder.group(
    {
      email: ['', [Validators.required, emailWithDomain]],
      password: ['', Validators.required],
      confirm: ['', Validators.required],
    },
    { validators: fieldsMatch('password', 'confirm') }, // a typing slip must not lock the person out
  );

  // Until the server answers (or if it cannot be asked), assume registration is open: the API checks again on submit.
  protected readonly registration = toSignal(
    this.auth.registration().pipe(catchError(() => of<RegistrationStatus>({ enabled: true, allowedEmailDomains: [] }))),
    { initialValue: { enabled: true, allowedEmailDomains: [] } as RegistrationStatus },
  );

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected submit(): void {
    this.errorMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched(); // every field shows what is wrong
      return;
    }

    const { email, password } = this.form.getRawValue();
    this.submitting.set(true);

    // Registering also signs the new person in, so they can start straight away.
    this.auth.register({ email: email.trim(), password }).subscribe({
      next: () => {
        this.snackBar.open('Welcome! Your account was created and you are signed in.', 'Dismiss', { duration: 8000 });
        this.router.navigateByUrl('/incidents');
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.showError(error);
      },
    });
  }

  /**
   * The API names each broken rule by itself ("PasswordRequiresUpper", "DuplicateUserName"…) instead of by field.
   * Group them here, so every message appears next to the box it is about, and all of them are shown at once.
   */
  private showError(error: unknown): void {
    const messages = allFieldMessages(error);

    if (messages) {
      const problems = this.groupProblems(messages);

      if (problems.email.length) {
        this.form.controls.email.setErrors({ server: problems.email });
        this.form.controls.email.markAsTouched();
      }
      if (problems.password.length) {
        this.form.controls.password.setErrors({ server: problems.password });
        this.form.controls.password.markAsTouched();
      }
      if (problems.other.length) {
        this.errorMessage.set(problems.other.join(' '));
      }
      if (problems.email.length || problems.password.length || problems.other.length) {
        return;
      }
    }

    this.errorMessage.set(
      error instanceof HttpErrorResponse && error.status === 0
        ? 'Cannot reach the server. Your account was not created.'
        : error instanceof HttpErrorResponse && error.status === 403
          ? 'Registration is closed. Ask an administrator to add you.'
          : error instanceof HttpErrorResponse && error.status === 429
            ? 'Too many attempts. Please wait a minute and try again.'
            : 'Your account could not be created. Please try again.',
    );
  }

  private groupProblems(messages: { field: string; message: string }[]): Problems {
    const problems: Problems = { email: [], password: [], other: [] };

    for (const { field, message } of messages) {
      if (field.startsWith('password')) {
        problems.password.push(message);
      } else if (field === 'email' || field.startsWith('duplicate') || field.startsWith('invalid')) {
        problems.email.push(message);
      } else {
        problems.other.push(message);
      }
    }
    return problems;
  }

  protected serverMessages(field: 'email' | 'password'): string[] {
    const messages = this.form.controls[field].errors?.['server'];
    return Array.isArray(messages) ? messages : [];
  }
}
