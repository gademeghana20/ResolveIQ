import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { API_URL } from '../../../core/api-config';
import { SlaPolicyDialog } from './sla-policy-dialog';
import { SlaPolicy } from './sla.models';

const high: SlaPolicy = { priority: 'High', resolutionTargetHours: 8, isActive: true };

describe('SlaPolicyDialog', () => {
  let fixture: ComponentFixture<SlaPolicyDialog>;
  let http: HttpTestingController;
  let close: ReturnType<typeof vi.fn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const text = () => page().textContent ?? '';
  const hoursBox = () => page().querySelector('input[type="number"]') as HTMLInputElement;
  const submitButton = () => page().querySelector('button[type="submit"]') as HTMLButtonElement;
  const form = () => (fixture.componentInstance as unknown as { form: SlaPolicyDialog['form'] }).form;
  const url = `${API_URL}/sla/policies/High`;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function setUp(policy: SlaPolicy = high) {
    close = vi.fn();
    await TestBed.configureTestingModule({
      imports: [SlaPolicyDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
        { provide: MAT_DIALOG_DATA, useValue: { policy } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SlaPolicyDialog);
    await settle();
  }

  async function typeHours(value: string) {
    hoursBox().value = value;
    hoursBox().dispatchEvent(new Event('input'));
    await settle();
  }

  async function submit() {
    page().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  beforeEach(() => setUp());

  it('names the priority being changed', () => {
    expect(text()).toContain('High deadline');
  });

  it('says the new deadline only applies from now on, and that running and finished incidents are left alone', () => {
    expect(text()).toContain('from now on');
    expect(text()).toContain('already running keep the deadline they were given');
    expect(text()).toContain('finished ones keep their result');
    expect(text()).not.toContain('every High incident');
  });

  it('starts with the current number of hours, and the deadline switched on', () => {
    expect(hoursBox().value).toBe('8');
    expect(form().controls.isActive.value).toBe(true);
  });

  it('helps people think in days by showing the hours as days and hours', async () => {
    await typeHours('30');

    expect(text()).toContain('That is 1 d 6 h');
  });

  describe('checks the number before saving', () => {
    it('needs a number', async () => {
      await typeHours('');
      await submit();

      http.expectNone(url);
      expect(text()).toContain('Enter a number of hours.');
    });

    it.each(['0', '-5', '721', '5000'])('refuses %s hours', async (value) => {
      await typeHours(value);
      await submit();

      http.expectNone(url);
      expect(text()).toContain('between 1 and 720 hours');
    });

    it('refuses a fraction of an hour', async () => {
      await typeHours('8.5');
      await submit();

      http.expectNone(url);
      expect(text()).toContain('whole number of hours');
    });

    it.each(['1', '24', '720'])('accepts %s hours', async (value) => {
      await typeHours(value);
      await submit();

      expect(http.expectOne(url).request.body).toMatchObject({ resolutionTargetHours: Number(value) });
    });
  });

  it('saves the new hours for that priority, then closes with the saved policy', async () => {
    await typeHours('12');

    await submit();

    const request = http.expectOne(url);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ resolutionTargetHours: 12, isActive: true });
    const saved = { ...high, resolutionTargetHours: 12 };
    request.flush(saved);

    expect(close).toHaveBeenCalledWith(saved);
  });

  it('can switch the deadline off, and explains what that means', async () => {
    form().controls.isActive.setValue(false);
    await settle();

    expect(text()).toContain('show "No SLA" and can never be late');
    expect(text()).toContain('Ones already running keep their deadline');

    await submit();
    expect(http.expectOne(url).request.body).toMatchObject({ isActive: false });
  });

  it('cannot be submitted twice while saving', async () => {
    await submit();

    expect(submitButton().disabled).toBe(true);
    expect(submitButton().textContent).toContain('Saving');
    http.expectOne(url).flush(high);
  });

  it('shows the API\'s complaint next to the hours and stays open', async () => {
    await typeHours('100');
    await submit();

    http.expectOne(url).flush(
      { errors: { ResolutionTargetHours: ['The field ResolutionTargetHours must be between 1 and 720.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(text()).toContain('must be between 1 and 720');
    expect(close).not.toHaveBeenCalled();
    expect(submitButton().disabled).toBe(false);
  });

  it('says so when the server cannot be reached', async () => {
    await submit();

    http.expectOne(url).error(new ProgressEvent('error'));
    await settle();

    expect(text()).toContain('Cannot reach the server');
    expect(close).not.toHaveBeenCalled();
  });

  it('says so when the policy has gone', async () => {
    await submit();

    http.expectOne(url).flush({}, { status: 404, statusText: 'Not Found' });
    await settle();

    expect(text()).toContain('no longer exists');
  });
});
