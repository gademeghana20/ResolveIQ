import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

import { API_URL } from '../api-config';
import { AuthService } from './auth.service';

/**
 * Runs on every HTTP call the app makes.
 *  - Outgoing: adds "Authorization: Bearer <token>", but only for our own API.
 *  - Incoming: if the API rejects a call that carried a token (401), the login is no longer valid
 *    (expired, or an Admin changed this account), so sign out and go to the login page.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  // Never send the token to anyone but our own API.
  if (!request.url.startsWith(API_URL)) {
    return next(request);
  }

  // Login and register are how you GET a token. They carry none, and a 401 from them just means
  // "wrong email or password", which the login screen shows itself. It must never sign anyone out.
  if (request.url.startsWith(`${API_URL}/auth/`)) {
    return next(request);
  }

  const token = auth.getValidToken();
  const outgoing = token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      const loginSessionIsDead = error instanceof HttpErrorResponse && error.status === 401 && token !== null;

      if (loginSessionIsDead) {
        const wherePersonWas = router.url;
        auth.logout();
        router.navigate(['/login'], { queryParams: { returnUrl: wherePersonWas } });
      }

      // Pass the error on, so the screen that made the call can still react to it.
      return throwError(() => error);
    }),
  );
};
