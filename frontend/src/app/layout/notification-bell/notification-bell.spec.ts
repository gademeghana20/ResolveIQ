import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { Router, provideRouter } from '@angular/router';

import { API_URL } from '../../core/api-config';
import { AppNotification, NotificationList } from '../../core/notifications/notification.models';
import { NotificationService } from '../../core/notifications/notification.service';
import { NotificationBell } from './notification-bell';

const URL = `${API_URL}/notifications`;

function note(id: string, overrides: Partial<AppNotification> = {}): AppNotification {
  return {
    id,
    kind: 'Assigned',
    message: `INC-000001 was assigned to you (${id}).`,
    incidentId: 'inc-1',
    createdAtUtc: new Date().toISOString(),
    isRead: false,
    ...overrides,
  };
}

describe('NotificationBell', () => {
  let fixture: ComponentFixture<NotificationBell>;
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const trigger = () => page().querySelector('button.bell') as HTMLButtonElement;
  const inPanel = (selector: string) => Array.from(document.querySelectorAll<HTMLElement>(`.bell-panel ${selector}`));

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  /** Gives the bell its news (as the shell's polling would), then draws it. */
  async function setUp(news: NotificationList) {
    await TestBed.configureTestingModule({
      imports: [NotificationBell],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    const service = TestBed.inject(NotificationService);
    service.refresh();
    http.expectOne(URL).flush(news);

    fixture = TestBed.createComponent(NotificationBell);
    await settle();
  }

  async function openPanel() {
    trigger().click();
    await settle();
    http.match(URL).forEach((request) => request.flush({ unreadCount: 0, items: [] })); // opening looks for newer news
  }

  afterEach(() => {
    document.querySelectorAll('.cdk-overlay-container').forEach((overlay) => (overlay.innerHTML = ''));
  });

  it('says in the button\'s name how many are unread, so the number never depends on seeing the badge', async () => {
    await setUp({ unreadCount: 3, items: [note('a'), note('b'), note('c')] });

    expect(trigger().getAttribute('aria-label')).toBe('Notifications, 3 unread');
  });

  it('says none are unread when there is nothing new', async () => {
    await setUp({ unreadCount: 0, items: [] });

    expect(trigger().getAttribute('aria-label')).toBe('Notifications, none unread');
  });

  it('shows the count on the bell, and hides the badge at zero', async () => {
    await setUp({ unreadCount: 3, items: [note('a')] });
    expect(page().querySelector('.mat-badge-content')!.textContent!.trim()).toBe('3');
    expect(page().querySelector('.mat-badge-hidden')).toBeNull();
  });

  it('hides the badge when nothing is unread', async () => {
    await setUp({ unreadCount: 0, items: [] });

    expect(page().querySelector('.mat-badge-hidden')).not.toBeNull();
  });

  it('writes 99+ when there are very many', async () => {
    await setUp({ unreadCount: 250, items: [note('a')] });

    expect(page().querySelector('.mat-badge-content')!.textContent!.trim()).toBe('99+');
  });

  it('lists the notifications when opened, newest first as the API sent them', async () => {
    await setUp({ unreadCount: 2, items: [note('a'), note('b', { message: 'Second one' })] });
    await openPanel();

    const messages = inPanel('.bell-message').map((m) => m.textContent!.replace(/\s+/g, ' ').trim());
    expect(messages.length).toBe(2);
    expect(messages[0]).toContain('(a)');
    expect(messages[1]).toContain('Second one');
  });

  it('marks an unread one in words as well as with a dot, and a read one not at all', async () => {
    await setUp({ unreadCount: 1, items: [note('a'), note('b', { isRead: true })] });
    await openPanel();

    const items = inPanel('.bell-item');
    expect(items[0].textContent).toContain('Unread:');
    expect(items[0].classList).toContain('unread');
    expect(items[1].textContent).not.toContain('Unread:');
  });

  it('shows how long ago each happened', async () => {
    await setUp({ unreadCount: 1, items: [note('a', { createdAtUtc: new Date(Date.now() - 5 * 60_000).toISOString() })] });
    await openPanel();

    expect(inPanel('.bell-when')[0].textContent).toBe('5 min ago');
  });

  it('says so when there is nothing yet', async () => {
    await setUp({ unreadCount: 0, items: [] });
    await openPanel();

    expect(inPanel('.bell-empty')[0].textContent).toContain('No notifications yet.');
  });

  it('opens the incident and marks the notification read when it is chosen', async () => {
    await setUp({ unreadCount: 1, items: [note('a', { incidentId: 'inc-42' })] });
    await openPanel();

    inPanel('.bell-item')[0].click();
    await settle();

    expect(navigate).toHaveBeenCalledWith(['/incidents', 'inc-42']);
    const request = http.expectOne(`${URL}/a/read`);
    expect(request.request.method).toBe('PUT');
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(trigger().getAttribute('aria-label')).toBe('Notifications, none unread');
  });

  it('only marks it read when the notification is not about one incident', async () => {
    await setUp({ unreadCount: 1, items: [note('a', { incidentId: null })] });
    await openPanel();

    inPanel('.bell-item')[0].click();
    await settle();

    expect(navigate).not.toHaveBeenCalled();
    http.expectOne(`${URL}/a/read`).flush(null, { status: 204, statusText: 'No Content' });
  });

  it('marks everything read with one button, which is switched off when nothing is unread', async () => {
    await setUp({ unreadCount: 2, items: [note('a'), note('b')] });
    await openPanel();

    const button = inPanel('.bell-head button')[0] as HTMLButtonElement;
    expect(button.disabled).toBe(false);
    button.click();
    await settle();

    http.expectOne(`${URL}/read-all`).flush(null, { status: 204, statusText: 'No Content' });
    expect(trigger().getAttribute('aria-label')).toBe('Notifications, none unread');
    expect((inPanel('.bell-head button')[0] as HTMLButtonElement).disabled).toBe(true);
  });

  it('looks for newer news each time it is opened', async () => {
    await setUp({ unreadCount: 0, items: [] });

    trigger().click();
    await settle();

    expect(http.match(URL).length).toBe(1);
  });
});
