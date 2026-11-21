import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { API_URL } from '../../../core/api-config';
import { IncidentComment } from '../incident.models';
import { CommentForm } from './comment-form';

const URL = `${API_URL}/incidents/inc-1/comments`;

const saved: IncidentComment = {
  id: 'k1',
  text: 'Restarted the router',
  authorUserId: 'u1',
  authorEmail: 'dana@example.com',
  createdAtUtc: '2026-11-16T19:00:00Z',
};

describe('CommentForm', () => {
  let fixture: ComponentFixture<CommentForm>;
  let http: HttpTestingController;
  let added: IncidentComment[];

  const page = () => fixture.nativeElement as HTMLElement;
  const button = () => page().querySelector('button')!;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function write(text: string) {
    const box = page().querySelector('textarea')!;
    box.value = text;
    box.dispatchEvent(new Event('input'));
    await settle();
  }

  async function post() {
    page().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [CommentForm],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CommentForm);
    fixture.componentRef.setInput('incidentId', 'inc-1');
    added = [];
    fixture.componentInstance.added.subscribe((comment) => added.push(comment));
    await settle();
  });

  it('knows when a comment is typed but not posted', async () => {
    expect(fixture.componentInstance.hasDraft()).toBe(false);

    await write('   ');
    expect(fixture.componentInstance.hasDraft()).toBe(false); // spaces are nothing

    await write('Half a thought');
    expect(fixture.componentInstance.hasDraft()).toBe(true);
  });

  it('does not post an empty comment, and says why', async () => {
    await post();

    http.expectNone(URL);
    expect(page().textContent).toContain('Write something before posting.');
  });

  it('does not post a comment that is only spaces', async () => {
    await write('     ');
    await post();

    http.expectNone(URL);
    expect(page().textContent).toContain('Write something before posting.');
  });

  it('posts the comment (trimmed), tells the page, and empties the box', async () => {
    await write('  Restarted the router  ');
    await post();

    const request = http.expectOne(URL);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ text: 'Restarted the router' });
    request.flush(saved);
    await settle();

    expect(added).toEqual([saved]);
    expect(page().querySelector('textarea')!.value).toBe('');
    expect(page().textContent).not.toContain('Write something before posting.'); // no error right after success
  });

  it('cannot be pressed twice while posting', async () => {
    await write('Hello');
    await post();

    expect(button().disabled).toBe(true);
    expect(button().textContent).toContain('Posting');

    http.expectOne(URL).flush(saved);
    await settle();
    expect(button().disabled).toBe(false);
  });

  it('warns when the comment is longer than the limit, and does not post it', async () => {
    await write('x'.repeat(2001));
    await post();

    http.expectNone(URL);
    expect(page().textContent).toContain('limit is 2000');
  });

  it('counts the characters as you type', async () => {
    await write('Hello');

    expect(page().textContent).toContain('5 / 2000');
  });

  it('shows the reason the API gives, such as a closed incident', async () => {
    await write('Late comment');
    await post();

    http.expectOne(URL).flush(
      { errors: { Text: ['Comments are closed on a closed incident.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(page().textContent).toContain('Comments are closed on a closed incident.');
    expect(added).toEqual([]);
    expect(page().querySelector('textarea')!.value).toBe('Late comment'); // what they wrote is kept
  });

  it('says so when the server cannot be reached, and keeps the text', async () => {
    await write('Important note');
    await post();

    http.expectOne(URL).error(new ProgressEvent('error'));
    await settle();

    expect(page().textContent).toContain('Cannot reach the server');
    expect(page().querySelector('textarea')!.value).toBe('Important note');
    expect(button().disabled).toBe(false);
  });

  it('says so when the incident has disappeared', async () => {
    await write('Hello');
    await post();

    http.expectOne(URL).flush({}, { status: 404, statusText: 'Not Found' });
    await settle();

    expect(page().textContent).toContain('no longer available');
  });

  describe('with files', () => {
    const attachUrl = `${API_URL}/incidents/inc-1/attachments`;
    const photo = () => new File(['x'], 'shot.png');
    const chosen = () => (fixture.componentInstance as unknown as { pendingFiles: WritableSignal<File[]> }).pendingFiles;
    const attachment = { id: 'f1', commentId: 'k1', fileName: 'shot.png' };

    it('counts chosen files as an unposted draft, even with no words typed', () => {
      expect(fixture.componentInstance.hasDraft()).toBe(false);

      chosen().set([photo()]);

      expect(fixture.componentInstance.hasDraft()).toBe(true);
    });

    it('posts the comment first, then attaches the files to that comment', async () => {
      chosen().set([photo()]);
      await write('See the picture');
      await post();

      http.expectOne(URL).flush(saved);
      await settle();
      expect(added).toEqual([]); // the files are still on their way

      const upload = http.expectOne(attachUrl);
      expect((upload.request.body as FormData).get('commentId')).toBe('k1');
      upload.flush(attachment);
      await settle();

      expect(added).toEqual([saved]);
      expect(fixture.componentInstance.hasDraft()).toBe(false);
      expect(page().querySelector('textarea')!.value).toBe('');
    });

    it('keeps the comment when a file cannot be attached, and says which one', async () => {
      chosen().set([photo()]);
      await write('See the picture');
      await post();
      http.expectOne(URL).flush(saved);
      await settle();

      http
        .expectOne(attachUrl)
        .flush({ errors: { file: ['That type of file is not allowed.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle();

      expect(added).toEqual([saved]); // the comment is there regardless
      expect(page().querySelector('[role="alert"]')!.textContent).toContain('Your comment was posted');
      expect(page().querySelector('[role="alert"]')!.textContent).toContain('shot.png: That type of file is not allowed.');
    });

    it('makes no upload request when there are no files', async () => {
      await write('Just words');
      await post();
      http.expectOne(URL).flush(saved);
      await settle();

      http.expectNone(attachUrl);
      expect(added).toEqual([saved]);
    });
  });

});
