import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';

import { Role } from './auth.models';
import { AuthService } from './auth.service';
import { roleGuard } from './role.guard';

describe('roleGuard', () => {
  function run(guard: ReturnType<typeof roleGuard>, loggedIn: boolean, roles: Role[], url = '/admin/users') {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isLoggedIn: () => loggedIn, hasRole: (...allowed: Role[]) => loggedIn && roles.some((r) => allowed.includes(r)) } },
      ],
    });

    return TestBed.runInInjectionContext(() => guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot));
  }

  const serialize = (result: unknown) => TestBed.inject(Router).serializeUrl(result as UrlTree);

  it('lets in someone who has the role', () => {
    expect(run(roleGuard('Admin'), true, ['Admin'])).toBe(true);
  });

  it('lets in anyone who has one of several allowed roles', () => {
    expect(run(roleGuard('Admin', 'Manager'), true, ['Manager'])).toBe(true);
  });

  it('sends a signed-in person without the role to the incident list', () => {
    const result = run(roleGuard('Admin'), true, ['Employee']);

    expect(result).toBeInstanceOf(UrlTree);
    expect(serialize(result)).toBe('/incidents');
  });

  it('keeps a Manager out of an Admin-only page', () => {
    expect(serialize(run(roleGuard('Admin'), true, ['Manager']))).toBe('/incidents');
  });

  it('sends a logged-out visitor to login, remembering the page they wanted', () => {
    const result = run(roleGuard('Admin'), false, [], '/admin/users');

    expect(serialize(result)).toBe('/login?returnUrl=%2Fadmin%2Fusers');
  });
});
