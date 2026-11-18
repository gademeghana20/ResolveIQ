import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { API_URL } from '../../../core/api-config';
import { ForgotPassword } from './forgot-password';

describe('ForgotPassword', () => {
  let fixture: ComponentFixture<ForgotPassword>;
  let http: HttpTestingController;

  const page = () => fixture.nativeElement as HTMLElement;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function submit(email: string) {
    const input = page().querySelector('input')!;
    input.value = email;
    input.dispatchEvent(new Event('input'));
    page().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [ForgotPassword],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ForgotPassword);
    await settle();
  });

  it('asks only for an email, and offers a way back to sign in', () => {
    expect(page().querySelectorAll('input').length).toBe(1);
    const back = Array.from(page().querySelectorAll('a')).find((a) => a.textContent!.includes('Back to sign in'))!;
    expect(back.getAttribute('href')).toBe('/login');
  });

  it('does not call the API without a valid email, and says what is wrong', async () => {
    await submit('');
    expect(page().textContent).toContain('Email is required.');

    await submit('not-an-email');
    expect(page().textContent).toContain('Enter a valid email address.');
    http.expectNone(`${API_URL}/auth/forgot-password`);
  });

  it('asks the API for the email, then says it is on its way without confirming the address exists', async () => {
    await submit('  dana@example.com ');

    const request = http.expectOne(`${API_URL}/auth/forgot-password`);
    expect(request.request.body).toEqual({ email: 'dana@example.com' });
    request.flush({ message: 'If that address has an account, an email is on its way.' });
    await settle();

    expect(page().textContent).toContain('If that address has an account');
    expect(page().querySelector('form')).toBeNull(); // the form is replaced by the message
  });

  it('cannot be sent twice while waiting', async () => {
    await submit('dana@example.com');

    const button = page().querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    http.expectOne(`${API_URL}/auth/forgot-password`).flush({ message: 'ok' });
  });

  it('says to wait a minute when requests come too fast, and keeps the form', async () => {
    await submit('dana@example.com');

    http.expectOne(`${API_URL}/auth/forgot-password`).flush({}, { status: 429, statusText: 'Too Many Requests' });
    await settle();

    expect(page().textContent).toContain('Too many requests. Please wait a minute');
    expect(page().querySelector('form')).not.toBeNull();
  });

  it('says so when the server cannot be reached', async () => {
    await submit('dana@example.com');

    http.expectOne(`${API_URL}/auth/forgot-password`).error(new ProgressEvent('error'));
    await settle();

    expect(page().textContent).toContain('Cannot reach the server');
    expect(page().querySelector('form')).not.toBeNull(); // they can try again
  });
});
