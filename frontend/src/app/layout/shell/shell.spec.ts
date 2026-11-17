import { provideHttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { fakeToken } from '../../testing/fake-token';
import { Shell } from './shell';

describe('Shell', () => {
  let fixture: ComponentFixture<Shell>;

  const page = () => fixture.nativeElement as HTMLElement;

  async function setUp() {
    localStorage.clear();
    localStorage.setItem(
      'resolveiq.session',
      JSON.stringify({ token: fakeToken(), email: 'manager@example.com', roles: ['Manager'] }),
    );

    await TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([]), provideHttpClient()],
    }).compileComponents();

    fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('links to the dashboard and to the incidents', async () => {
    await setUp();

    const links = Array.from(page().querySelectorAll('nav a')).map((a) => [a.textContent!.trim(), a.getAttribute('href')]);
    expect(links).toEqual([
      ['Dashboard', '/dashboard'],
      ['Incidents', '/incidents'],
    ]);
  });

  it('says who is signed in and with which role', async () => {
    await setUp();

    expect(page().textContent).toContain('manager@example.com');
    expect(page().textContent).toContain('Manager');
  });

  it('signs out and goes to the login page', async () => {
    await setUp();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    const button = Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.includes('Sign out'))!;
    button.click();

    expect(TestBed.inject(AuthService).isLoggedIn()).toBe(false);
    expect(navigate).toHaveBeenCalledWith('/login');
  });
});
