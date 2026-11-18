import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, map, of } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { ThemeToggle } from '../../../shared/theme-toggle/theme-toggle';
import { AuthBrand } from '../auth-brand/auth-brand';

@Component({
  selector: 'app-login',
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
  templateUrl: './login.html',
  styleUrl: '../auth-page.css',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  // A reactive form: the form's rules live here in code, and the template just displays them.
  protected readonly form = this.formBuilder.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  // The "Create an account" link is shown unless the server says registration is closed (or cannot be asked).
  protected readonly registrationOpen = toSignal(
    this.auth.registration().pipe(
      map((status) => status.enabled),
      catchError(() => of(true)),
    ),
    { initialValue: true },
  );

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected submit(): void {
    this.errorMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched(); // makes every field show its error message
      return;
    }

    this.submitting.set(true);

    this.auth.login(this.form.getRawValue()).subscribe({
      next: () => this.router.navigateByUrl(this.safeReturnUrl()),
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(this.messageFor(error));
      },
    });
  }

  /** Where to go after logging in: the page the person originally wanted, if it is one of ours. */
  private safeReturnUrl(): string {
    const wanted = this.route.snapshot.queryParamMap.get('returnUrl');
    const isOurOwnPath = !!wanted && wanted.startsWith('/') && !wanted.startsWith('//');
    return isOurOwnPath ? wanted : '/incidents';
  }

  private messageFor(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) {
        return 'Cannot reach the server. Please check that the API is running.';
      }
      if (error.status === 401) {
        return 'Invalid email or password.';
      }
      if (error.status === 429) {
        return 'Too many attempts. Please wait a minute and try again.';
      }
    }
    return 'Something went wrong. Please try again.';
  }
}
