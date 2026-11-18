import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_URL } from '../api-config';
import { AppNotification, NotificationList } from './notification.models';
import { NotificationService, POLL_EVERY_MS } from './notification.service';

const URL = `${API_URL}/notifications`;

function note(id: string, isRead = false): AppNotification {
  return { id, kind: 'Assigned', message: `Message ${id}`, incidentId: 'inc-1', createdAtUtc: '2026-11-18T09:00:00Z', isRead };
}

function list(...items: AppNotification[]): NotificationList {
  return { unreadCount: items.filter((i) => !i.isRead).length, items };
}

function pretendTheTabIs(state: 'visible' | 'hidden') {
  Object.defineProperty(document, 'visibilityState', { value: state, configurable: true });
}

describe('NotificationService', () => {
  let http: HttpTestingController;
  let service: NotificationService;

  beforeEach(() => {
    vi.useFakeTimers();
    pretendTheTabIs('visible');
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(NotificationService);
  });

  afterEach(() => {
    service.stop();
    vi.useRealTimers();
    pretendTheTabIs('visible');
  });

  it('starts empty', () => {
    expect(service.unreadCount()).toBe(0);
    expect(service.items()).toEqual([]);
  });

  it('looks straight away when started, and shows what the API says', () => {
    service.start();

    http.expectOne(URL).flush(list(note('a'), note('b', true)));

    expect(service.unreadCount()).toBe(1);
    expect(service.items().map((i) => i.id)).toEqual(['a', 'b']);
  });

  it('looks again every minute while the page is on screen', () => {
    service.start();
    http.expectOne(URL).flush(list());

    vi.advanceTimersByTime(POLL_EVERY_MS);
    http.expectOne(URL).flush(list(note('new')));
    vi.advanceTimersByTime(POLL_EVERY_MS);
    http.expectOne(URL).flush(list(note('new'), note('newer')));

    expect(service.unreadCount()).toBe(2);
  });

  it('does not ask while the tab is hidden, and asks as soon as the person comes back', () => {
    service.start();
    http.expectOne(URL).flush(list());

    pretendTheTabIs('hidden');
    vi.advanceTimersByTime(POLL_EVERY_MS * 3);
    http.expectNone(URL);

    pretendTheTabIs('visible');
    document.dispatchEvent(new Event('visibilitychange'));
    http.expectOne(URL).flush(list(note('while-away')));

    expect(service.unreadCount()).toBe(1);
  });

  it('starting twice does not double the polling', () => {
    service.start();
    http.expectOne(URL).flush(list());
    service.start();
    http.expectOne(URL).flush(list());

    vi.advanceTimersByTime(POLL_EVERY_MS);

    http.expectOne(URL).flush(list()); // exactly one, not two
  });

  it('keeps what it had when a look fails', () => {
    service.start();
    http.expectOne(URL).flush(list(note('a')));

    vi.advanceTimersByTime(POLL_EVERY_MS);
    http.expectOne(URL).flush({}, { status: 500, statusText: 'Server Error' });

    expect(service.unreadCount()).toBe(1);
    expect(service.items().length).toBe(1);
  });

  it('stops looking, and forgets everything, when stopped', () => {
    service.start();
    http.expectOne(URL).flush(list(note('a')));

    service.stop();
    vi.advanceTimersByTime(POLL_EVERY_MS * 2);

    http.expectNone(URL);
    expect(service.unreadCount()).toBe(0);
    expect(service.items()).toEqual([]);
  });

  describe('marking read', () => {
    beforeEach(() => {
      service.start();
      http.expectOne(URL).flush(list(note('a'), note('b'), note('c', true)));
    });

    it('updates the bell at once, and tells the API', () => {
      service.markRead(note('a')).subscribe();

      expect(service.unreadCount()).toBe(1);
      expect(service.items().find((i) => i.id === 'a')!.isRead).toBe(true);
      expect(service.items().find((i) => i.id === 'b')!.isRead).toBe(false);

      const request = http.expectOne(`${URL}/a/read`);
      expect(request.request.method).toBe('PUT');
      request.flush(null, { status: 204, statusText: 'No Content' });
    });

    it('does not lower the count again for one that was already read', () => {
      service.markRead(note('c', true)).subscribe();

      expect(service.unreadCount()).toBe(2);
      http.expectOne(`${URL}/c/read`).flush(null, { status: 204, statusText: 'No Content' });
    });

    it('marks everything read in one go', () => {
      service.markAllRead().subscribe();

      expect(service.unreadCount()).toBe(0);
      expect(service.items().every((i) => i.isRead)).toBe(true);
      const request = http.expectOne(`${URL}/read-all`);
      expect(request.request.method).toBe('PUT');
      request.flush(null, { status: 204, statusText: 'No Content' });
    });

    it('looks again if marking everything read fails, so the bell tells the truth', () => {
      service.markAllRead().subscribe({ error: () => undefined });

      http.expectOne(`${URL}/read-all`).flush({}, { status: 500, statusText: 'Server Error' });

      http.expectOne(URL).flush(list(note('a'), note('b')));
      expect(service.unreadCount()).toBe(2);
    });
  });
});
