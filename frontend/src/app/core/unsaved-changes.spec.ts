import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { Observable, of } from 'rxjs';

import { AuthService } from './auth/auth.service';
import { HasUnsavedChanges, unsavedChangesGuard, warnBeforeClosingTab } from './unsaved-changes';

describe('unsavedChangesGuard', () => {
  let open: ReturnType<typeof vi.fn>;

  function run(unsaved: boolean, loggedIn: boolean, answer: unknown = true) {
    open = vi.fn().mockReturnValue({ afterClosed: () => of(answer) });
    TestBed.configureTestingModule({
      providers: [
        { provide: MatDialog, useValue: { open } },
        { provide: AuthService, useValue: { isLoggedIn: () => loggedIn } },
      ],
    });
    const page: HasUnsavedChanges = { hasUnsavedChanges: () => unsaved };

    return TestBed.runInInjectionContext(() =>
      unsavedChangesGuard(page, {} as ActivatedRouteSnapshot, {} as RouterStateSnapshot, {} as RouterStateSnapshot),
    );
  }

  it('lets the person go without asking when nothing is unsaved', () => {
    expect(run(false, true)).toBe(true);
    expect(open).not.toHaveBeenCalled();
  });

  it('asks first when something is unsaved, and lets them go on "Leave page"', async () => {
    const result = run(true, true, true) as Observable<boolean>;

    let leave: boolean | undefined;
    result.subscribe((value) => (leave = value));
    expect(leave).toBe(true);
    expect(JSON.stringify(open.mock.calls[0][1])).toContain('Leave this page?');
  });

  it('keeps them on the page when they cancel', () => {
    let leave: boolean | undefined;
    (run(true, true, false) as Observable<boolean>).subscribe((value) => (leave = value));

    expect(leave).toBe(false);
  });

  it('does not ask once the session is over, since there is nowhere left to save to', () => {
    expect(run(true, false)).toBe(true);
    expect(open).not.toHaveBeenCalled();
  });
});

describe('warnBeforeClosingTab', () => {
  it('only makes the browser ask when there is something unsaved', () => {
    const quiet = new Event('beforeunload', { cancelable: true }) as BeforeUnloadEvent;
    warnBeforeClosingTab(quiet, { hasUnsavedChanges: () => false });
    expect(quiet.defaultPrevented).toBe(false);

    const asking = new Event('beforeunload', { cancelable: true }) as BeforeUnloadEvent;
    warnBeforeClosingTab(asking, { hasUnsavedChanges: () => true });
    expect(asking.defaultPrevented).toBe(true);
  });
});
