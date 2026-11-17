import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatSelect } from '@angular/material/select';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';

import { API_URL } from '../../../core/api-config';
import { fakeToken } from '../../../testing/fake-token';
import { UserSummary } from '../incident.models';
import { IncidentList } from './incident-list';

const emptyPage = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 };

const people: UserSummary[] = [
  { id: 'u-lee', email: 'lee@example.com', roles: ['Employee'], isActive: true },
  { id: 'u-dana', email: 'Dana@example.com', roles: ['Employee'], isActive: true },
  { id: 'u-gone', email: 'gone@example.com', roles: ['Employee'], isActive: false },
];

describe('IncidentList assignee filter', () => {
  let fixture: ComponentFixture<IncidentList>;
  let http: HttpTestingController;

  const page = () => fixture.nativeElement as HTMLElement;
  const isList = (r: { url: string }) => r.url === `${API_URL}/incidents`;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function open(role: 'Manager' | 'Admin' | 'Employee', usersReply: UserSummary[] | 'fail' = people) {
    localStorage.clear();
    localStorage.setItem('resolveiq.session', JSON.stringify({ token: fakeToken(), email: 'me@example.com', roles: [role] }));

    await TestBed.configureTestingModule({
      imports: [IncidentList],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(IncidentList);
    await settle();

    http.expectOne(isList).flush(emptyPage);
    http.expectOne(`${API_URL}/categories`).flush([]);

    if (role !== 'Employee') {
      const users = http.expectOne(`${API_URL}/users`);
      if (usersReply === 'fail') {
        users.flush({}, { status: 500, statusText: 'Server Error' });
      } else {
        users.flush(usersReply);
      }
    }
    await settle();
  }

  const assigneeSelect = () =>
    fixture.debugElement
      .queryAll(By.directive(MatSelect))
      .find((d) => (d.nativeElement as HTMLElement).closest('mat-form-field')?.textContent?.includes('Assignee'));

  async function choose(userId: string) {
    assigneeSelect()!.componentInstance.selectionChange.emit({ value: userId });
    await settle();
  }

  async function pressToggle(name: string) {
    const toggle = Array.from(page().querySelectorAll('mat-button-toggle')).find((t) => t.textContent!.includes(name))!;
    (toggle.querySelector('button') as HTMLButtonElement).click();
    await settle();
  }

  describe('who gets it', () => {
    it.each(['Manager', 'Admin'] as const)('is offered to a %s', async (role) => {
      await open(role);

      expect(assigneeSelect()).toBeDefined();
    });

    it('is not offered to an Employee, who may not look people up', async () => {
      await open('Employee');

      expect(assigneeSelect()).toBeUndefined();
      http.expectNone(`${API_URL}/users`); // not even asked for
    });

    it('is not offered, and the list still works, when the people cannot be loaded', async () => {
      await open('Manager', 'fail');

      expect(assigneeSelect()).toBeUndefined();
      expect(page().textContent).toContain('There are no incidents yet.');
    });
  });

  describe('the choices', () => {
    async function optionTexts() {
      (assigneeSelect()!.nativeElement as HTMLElement).click();
      await settle();
      return Array.from(document.querySelectorAll('mat-option')).map((o) => o.textContent!.trim());
    }

    it('lists everyone alphabetically, after "All"', async () => {
      await open('Manager');

      expect(await optionTexts()).toEqual(['All', 'Dana@example.com', 'gone@example.com (inactive)', 'lee@example.com']);
    });

    it('keeps deactivated people, marked as inactive, because incidents they handled are still worth finding', async () => {
      await open('Manager');

      expect((await optionTexts()).some((text) => text.includes('gone@example.com (inactive)'))).toBe(true);
    });
  });

  describe('using it', () => {
    it('asks the API only for that person\'s incidents', async () => {
      await open('Manager');

      await choose('u-dana');

      const request = http.expectOne(isList);
      expect(request.request.params.get('assignedToUserId')).toBe('u-dana');
      request.flush(emptyPage);
    });

    it('goes back to page 1', async () => {
      await open('Manager');
      fixture.debugElement.query(By.css('mat-paginator')).triggerEventHandler('page', { pageIndex: 2, pageSize: 20, length: 60 });
      await settle();
      http.expectOne(isList).flush({ ...emptyPage, totalCount: 60, totalPages: 3 });
      await settle();

      await choose('u-dana');

      const request = http.expectOne(isList);
      expect(request.request.params.get('page')).toBe('1');
      request.flush(emptyPage);
    });

    it('shows everything that person has handled, including closed incidents', async () => {
      await open('Manager');

      await choose('u-dana');

      const request = http.expectOne(isList);
      expect(request.request.params.has('status')).toBe(false); // no status narrowing: closed ones are included
      request.flush(emptyPage);
    });

    it('is removed by choosing "All"', async () => {
      await open('Manager');
      await choose('u-dana');
      http.expectOne(isList).flush(emptyPage);
      await settle();

      await choose('');

      const request = http.expectOne(isList);
      expect(request.request.params.has('assignedToUserId')).toBe(false);
      request.flush(emptyPage);
    });

    it('turns on "Clear filters", which removes the person again', async () => {
      await open('Manager');
      await choose('u-dana');
      http.expectOne(isList).flush(emptyPage);
      await settle();

      const clear = Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.includes('Clear filters'))!;
      clear.click();
      await settle();

      const request = http.expectOne(isList);
      expect(request.request.params.has('assignedToUserId')).toBe(false);
      request.flush(emptyPage);
    });
  });

  describe('with the Assigned to me / Unassigned buttons', () => {
    it('choosing a person replaces "Unassigned", which could never match', async () => {
      await open('Manager');
      await pressToggle('Unassigned');
      http.expectOne(isList).flush(emptyPage);
      await settle();

      await choose('u-dana');

      const request = http.expectOne(isList);
      expect(request.request.params.get('assignedToUserId')).toBe('u-dana');
      expect(request.request.params.has('unassigned')).toBe(false);
      request.flush(emptyPage);
    });

    it('choosing a person replaces "Assigned to me"', async () => {
      await open('Manager');
      await pressToggle('Assigned to me');
      http.expectOne(isList).flush(emptyPage);
      await settle();

      await choose('u-lee');

      const request = http.expectOne(isList);
      expect(request.request.params.has('assignedToMe')).toBe(false);
      expect(request.request.params.get('assignedToUserId')).toBe('u-lee');
      request.flush(emptyPage);
    });

    it('pressing "Unassigned" afterwards clears the chosen person', async () => {
      await open('Manager');
      await choose('u-dana');
      http.expectOne(isList).flush(emptyPage);
      await settle();

      await pressToggle('Unassigned');

      const request = http.expectOne(isList);
      expect(request.request.params.has('assignedToUserId')).toBe(false);
      expect(request.request.params.get('unassigned')).toBe('true');
      request.flush(emptyPage);
    });
  });
});
