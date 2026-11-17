import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { API_URL } from '../../../core/api-config';
import { IncidentDetail } from '../incident.models';
import { EditIncidentDialog } from './edit-incident-dialog';

const incident = {
  id: 'inc-1',
  incidentNumber: 'INC-000007',
  title: 'Printer broken',
  description: 'Paper jam on floor 2',
} as IncidentDetail;

const URL = `${API_URL}/incidents/inc-1`;

describe('EditIncidentDialog', () => {
  let fixture: ComponentFixture<EditIncidentDialog>;
  let http: HttpTestingController;
  let close: ReturnType<typeof vi.fn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const text = () => page().textContent ?? '';
  const titleBox = () => page().querySelector('input') as HTMLInputElement;
  const descriptionBox = () => page().querySelector('textarea') as HTMLTextAreaElement;
  const saveButton = () => page().querySelector('button[type="submit"]') as HTMLButtonElement;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function type(box: HTMLInputElement | HTMLTextAreaElement, value: string) {
    box.value = value;
    box.dispatchEvent(new Event('input'));
    await settle();
  }

  async function submit() {
    page().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  beforeEach(async () => {
    close = vi.fn();
    await TestBed.configureTestingModule({
      imports: [EditIncidentDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
        { provide: MAT_DIALOG_DATA, useValue: { incident } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EditIncidentDialog);
    await settle();
  });

  it('says which incident is being edited', () => {
    expect(text()).toContain('Edit INC-000007');
  });

  it('starts with the current title and description', () => {
    expect(titleBox().value).toBe('Printer broken');
    expect(descriptionBox().value).toBe('Paper jam on floor 2');
  });

  it('tells the person that every change is recorded, with the old wording', () => {
    expect(text()).toContain('recorded in the history');
  });

  describe('Save', () => {
    it('is switched off until something is different', () => {
      expect(saveButton().disabled).toBe(true);
    });

    it('stays off when only spaces around the text are added', async () => {
      await type(titleBox(), '  Printer broken  ');

      expect(saveButton().disabled).toBe(true);
    });

    it('switches on when the title changes', async () => {
      await type(titleBox(), 'Printer broken again');

      expect(saveButton().disabled).toBe(false);
    });

    it('switches on when the description changes', async () => {
      await type(descriptionBox(), 'Paper jam on floor 3');

      expect(saveButton().disabled).toBe(false);
    });

    it('switches off again if the person types the original back', async () => {
      await type(titleBox(), 'Something else');
      await type(titleBox(), 'Printer broken');

      expect(saveButton().disabled).toBe(true);
    });
  });

  describe('checks before sending', () => {
    it('needs a title', async () => {
      await type(titleBox(), '');
      await submit();

      http.expectNone(URL);
      expect(text()).toContain('A title is required.');
    });

    it('does not accept a title of only spaces', async () => {
      await type(titleBox(), '     ');
      await submit();

      http.expectNone(URL);
      expect(text()).toContain('A title is required.');
    });

    it('needs a description', async () => {
      await type(descriptionBox(), '');
      await submit();

      http.expectNone(URL);
      expect(text()).toContain('A description is required.');
    });

    it('warns when the title is over 200 characters', async () => {
      await type(titleBox(), 'x'.repeat(201));
      await submit();

      http.expectNone(URL);
      expect(text()).toContain('limit is 200');
    });

    it('warns when the description is over 4000 characters', async () => {
      await type(descriptionBox(), 'x'.repeat(4001));
      await submit();

      http.expectNone(URL);
      expect(text()).toContain('limit is 4000');
    });

    it('counts characters as you type', async () => {
      await type(titleBox(), 'Printer');

      expect(text()).toContain('7 / 200');
    });
  });

  describe('saving', () => {
    it('sends the trimmed text for that incident, then closes with the updated incident', async () => {
      await type(titleBox(), '  Printer on fire  ');
      await type(descriptionBox(), '  Smoke from the tray  ');

      await submit();

      const request = http.expectOne(URL);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ title: 'Printer on fire', description: 'Smoke from the tray' });
      const updated = { ...incident, title: 'Printer on fire' };
      request.flush(updated);

      expect(close).toHaveBeenCalledWith(updated);
    });

    it('cannot be submitted twice while saving', async () => {
      await type(titleBox(), 'Printer on fire');
      await submit();

      expect(saveButton().disabled).toBe(true);
      expect(saveButton().textContent).toContain('Saving');
      http.expectOne(URL).flush(incident);
    });
  });

  describe('when the API refuses', () => {
    async function refuse(body: object, status: number) {
      await type(titleBox(), 'Printer on fire');
      await submit();
      http.expectOne(URL).flush(body, { status, statusText: String(status) });
      await settle();
    }

    it('shows a complaint about the title next to the title, and keeps what was typed', async () => {
      await refuse({ errors: { Title: ['The field Title must be a string with a maximum length of 200.'] } }, 400);

      expect(text()).toContain('maximum length of 200');
      expect(titleBox().value).toBe('Printer on fire');
      expect(close).not.toHaveBeenCalled();
      expect(saveButton().disabled).toBe(false);
    });

    it('explains that a closed incident cannot be edited', async () => {
      await refuse({ errors: { Status: ['A closed incident cannot be edited.'] } }, 400);

      expect(text()).toContain('A closed incident cannot be edited.');
      expect(close).not.toHaveBeenCalled();
    });

    it('says someone else changed it, when two people edit at once (409)', async () => {
      await refuse({ title: 'The incident was changed by someone else.' }, 409);

      expect(text()).toContain('Someone else changed this incident');
    });

    it('says only the reporter can edit (403), for example if their role changed', async () => {
      await refuse({}, 403);

      expect(text()).toContain('Only the person who reported an incident can edit it.');
    });

    it('says so when the server cannot be reached', async () => {
      await type(titleBox(), 'Printer on fire');
      await submit();

      http.expectOne(URL).error(new ProgressEvent('error'));
      await settle();

      expect(text()).toContain('Cannot reach the server');
      expect(titleBox().value).toBe('Printer on fire');
    });
  });
});
