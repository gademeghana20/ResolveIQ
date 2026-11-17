import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, provideRouter } from '@angular/router';

import { API_URL } from '../../../core/api-config';
import { IncidentDetail } from '../incident.models';
import { IncidentCreate } from './incident-create';

const categories = [
  { id: 'c1', name: 'Hardware', description: null, isActive: true },
  { id: 'c2', name: 'Retired', description: null, isActive: false },
];

describe('IncidentCreate', () => {
  let fixture: ComponentFixture<IncidentCreate>;
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;
  let snack: ReturnType<typeof vi.spyOn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const text = () => page().textContent ?? '';
  const submitButton = () => page().querySelector('button[type="submit"]') as HTMLButtonElement;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function type(selector: string, value: string) {
    const field = page().querySelector(selector) as HTMLInputElement | HTMLTextAreaElement;
    field.value = value;
    field.dispatchEvent(new Event('input'));
  }

  async function submit() {
    page().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  async function setUp(categoryResponse: 'ok' | 'fail' = 'ok') {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [IncidentCreate],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
    fixture = TestBed.createComponent(IncidentCreate);
    await settle();

    const request = http.expectOne(`${API_URL}/categories`);
    if (categoryResponse === 'ok') {
      request.flush(categories);
    } else {
      request.flush({}, { status: 500, statusText: 'Server Error' });
    }
    await settle();
  }

  const created = { id: 'new-id', incidentNumber: 'INC-000042' } as IncidentDetail;

  it('has no priority field: a manager sets that later', async () => {
    await setUp();

    expect(text()).toContain('Title');
    expect(text()).toContain('Description');
    expect(text()).toContain('Category');
    expect(text()).not.toContain('Priority');
  });

  it('does not submit an empty form, and says what is missing', async () => {
    await setUp();

    await submit();

    http.expectNone(`${API_URL}/incidents`);
    expect(text()).toContain('A title is required.');
    expect(text()).toContain('A description is required.');
  });

  it('does not accept a title or description that is only spaces', async () => {
    await setUp();
    type('input[formControlName="title"]', '    ');
    type('textarea', '    ');

    await submit();

    http.expectNone(`${API_URL}/incidents`);
    expect(text()).toContain('A title is required.');
  });

  it('warns when the title is over 200 characters, and does not submit', async () => {
    await setUp();
    type('input[formControlName="title"]', 'x'.repeat(201));
    type('textarea', 'Something is wrong');

    await submit();

    http.expectNone(`${API_URL}/incidents`);
    expect(text()).toContain('limit is 200');
  });

  it('counts characters as you type', async () => {
    await setUp();
    type('input[formControlName="title"]', 'Printer');
    await settle();

    expect(text()).toContain('7 / 200');
  });

  it('only offers active categories', async () => {
    await setUp();

    const select = page().querySelector('mat-select') as HTMLElement;
    select.click();
    await settle();

    const options = Array.from(document.querySelectorAll('mat-option')).map((o) => o.textContent!.trim());
    expect(options).toContain('Hardware');
    expect(options).toContain('None');
    expect(options).not.toContain('Retired');
  });

  it('sends the trimmed text, and with no category when none was chosen', async () => {
    await setUp();
    type('input[formControlName="title"]', '  Printer broken  ');
    type('textarea', '  Paper jam on floor 2  ');

    await submit();

    const request = http.expectOne(`${API_URL}/incidents`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ title: 'Printer broken', description: 'Paper jam on floor 2', categoryId: undefined });
    request.flush(created);
  });

  it('after saving, tells the person and opens the new incident', async () => {
    await setUp();
    type('input[formControlName="title"]', 'Printer broken');
    type('textarea', 'Paper jam');

    await submit();
    http.expectOne(`${API_URL}/incidents`).flush(created);
    await settle();

    expect(snack.mock.calls[0][0]).toContain('INC-000042');
    expect(navigate).toHaveBeenCalledWith(['/incidents', 'new-id']);
  });

  it('cannot be submitted twice while saving', async () => {
    await setUp();
    type('input[formControlName="title"]', 'Printer broken');
    type('textarea', 'Paper jam');

    await submit();

    expect(submitButton().disabled).toBe(true);
    expect(submitButton().textContent).toContain('Submitting');
    http.expectOne(`${API_URL}/incidents`).flush(created);
  });

  it('shows the API\'s complaint next to the field it is about, and keeps what was typed', async () => {
    await setUp();
    type('input[formControlName="title"]', 'Printer broken');
    type('textarea', 'Paper jam');

    await submit();
    http.expectOne(`${API_URL}/incidents`).flush(
      { errors: { CategoryId: ['The category does not exist or is not active.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(text()).toContain('The category does not exist or is not active.');
    expect((page().querySelector('input[formControlName="title"]') as HTMLInputElement).value).toBe('Printer broken');
    expect(submitButton().disabled).toBe(false); // can try again
    expect(navigate).not.toHaveBeenCalled();
  });

  it('says so when the server cannot be reached, and keeps what was typed', async () => {
    await setUp();
    type('input[formControlName="title"]', 'Printer broken');
    type('textarea', 'Paper jam');

    await submit();
    http.expectOne(`${API_URL}/incidents`).error(new ProgressEvent('error'));
    await settle();

    expect(text()).toContain('Cannot reach the server');
    expect((page().querySelector('textarea') as HTMLTextAreaElement).value).toBe('Paper jam');
  });

  it('still works when the categories cannot be loaded', async () => {
    await setUp('fail');
    type('input[formControlName="title"]', 'Printer broken');
    type('textarea', 'Paper jam');

    expect(text()).toContain('categories could not be loaded');

    await submit();
    http.expectOne(`${API_URL}/incidents`).flush(created);
  });
});
