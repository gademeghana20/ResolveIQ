import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';

import { API_URL } from '../api-config';
import { AuthService } from './auth.service';

// The auth calls that happen before anyone has a login, or that carry their own proof (the refresh token).
// They never get the token, and a 401 from them must never sign anyone out ("wrong password" is just that).
const OPEN_AUTH_CALLS = ['login', 'register', 'forgot-password', 'reset-password', 'refresh-token', 'logout'].map(
  (name) => `${API_URL}/auth/${name}`,
);

function withToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
}

/**
 * Runs on every HTTP call the app makes.
 *  - Outgoing: adds "Authorization: Bearer <token>", but only for our own API.
 *  - Incoming: if the API rejects a call (401) because the short-lived token ran out, the refresh token is used to get
 *    a new one and the call is repeated once, so the person never notices. Only when that fails too (the refresh
 *    ticket is used up, expired or cancelled, or an Admin changed this account) is the person signed out and sent to
 *    the login page.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  // Never send the token to anyone but our own API.
  if (!request.url.startsWith(API_URL)) {
    return next(request);
  }

  if (OPEN_AUTH_CALLS.some((open) => request.url.startsWith(open))) {
    return next(request);
  }

  const token = auth.getValidToken();

  const signOut = () => {
    const wherePersonWas = router.url;
    auth.logout();
    router.navigate(['/login'], { queryParams: { returnUrl: wherePersonWas } });
  };

  const isRejected = (error: unknown) => error instanceof HttpErrorResponse && error.status === 401;

  return next(withToken(request, token)).pipe(
    catchError((error: unknown) => {
      if (isRejected(error) && auth.canRefresh()) {
        return auth.refresh().pipe(
          // The ticket did not work: the login really is over. (If the server was merely unreachable, stay signed in.)
          catchError((refreshError: unknown) => {
            if (isRejected(refreshError)) {
              signOut();
            }
            return throwError(() => error);
          }),
          // Repeat the call with the new token. If even that is refused, the login is over.
          switchMap((fresh) =>
            next(withToken(request, fresh.token)).pipe(
              catchError((again: unknown) => {
                if (isRejected(again)) {
                  signOut();
                }
                return throwError(() => again);
              }),
            ),
          ),
        );
      }

      if (isRejected(error) && token !== null) {
        signOut();
      }

      // Pass the error on, so the screen that made the call can still react to it.
      return throwError(() => error);
    }),
  );
};
