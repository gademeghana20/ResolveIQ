import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { Role } from './auth.models';
import { AuthService } from './auth.service';

/**
 * Keeps a page for certain roles only, for example roleGuard('Admin'). Logged-out visitors go to the
 * login page; signed-in people without the role are quietly sent to the incident list.
 *
 * This only improves the experience. The API refuses unauthorised calls on its own.
 */
export const roleGuard =
  (...allowed: Role[]): CanActivateFn =>
  (_route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (!auth.isLoggedIn()) {
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }

    return auth.hasRole(...allowed) ? true : router.createUrlTree(['/incidents']);
  };
