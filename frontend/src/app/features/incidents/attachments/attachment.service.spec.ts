import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_URL } from '../../../core/api-config';
import { ATTACHMENT_SETTINGS as SETTINGS } from '../../../testing/attachment-settings';
import { Attachment } from '../incident.models';
import { AttachmentService, UploadSummary, uploadProblem } from './attachment.service';

const saved: Attachment = {
  id: 'f1',
  incidentId: 'inc-1',
  commentId: null,
  fileName: 'shot.png',
  contentType: 'image/png',
  sizeBytes: 1234,
  uploadedByUserId: 'u1',
  uploadedByEmail: 'dana@example.com',
  createdAtUtc: '2026-11-21T10:00:00Z',
  canRemove: true,
};

const pick = (name: string) => new File(['x'], name);

describe('AttachmentService', () => {
  let service: AttachmentService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AttachmentService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks for the settings once and remembers the answer', () => {
    const answers: unknown[] = [];
    service.getSettings().subscribe((s) => answers.push(s));
    service.getSettings().subscribe((s) => answers.push(s));

    http.expectOne(`${API_URL}/attachments/settings`).flush(SETTINGS);

    // A later screen gets the remembered answer without another request.
    service.getSettings().subscribe((s) => answers.push(s));
    expect(answers).toEqual([SETTINGS, SETTINGS, SETTINGS]);
  });

  it('asks again after a failed question', () => {
    service.getSettings().subscribe({ error: () => undefined });
    http.expectOne(`${API_URL}/attachments/settings`).flush({}, { status: 500, statusText: 'Server Error' });

    service.getSettings().subscribe();
    http.expectOne(`${API_URL}/attachments/settings`).flush(SETTINGS);
  });

  it('lists the files of an incident', () => {
    let result: Attachment[] = [];
    service.list('inc-1').subscribe((r) => (result = r));

    http.expectOne(`${API_URL}/incidents/inc-1/attachments`).flush([saved]);

    expect(result).toEqual([saved]);
  });

  it('sends a file as a form, with the comment it belongs to when there is one', () => {
    service.upload('inc-1', pick('shot.png'), 'k1').subscribe();

    const request = http.expectOne(`${API_URL}/incidents/inc-1/attachments`);
    expect(request.request.method).toBe('POST');
    const form = request.request.body as FormData;
    expect((form.get('file') as File).name).toBe('shot.png');
    expect(form.get('commentId')).toBe('k1');
    request.flush(saved);
  });

  it('leaves out the comment when the file is for the incident itself', () => {
    service.upload('inc-1', pick('shot.png')).subscribe();

    const request = http.expectOne(`${API_URL}/incidents/inc-1/attachments`);
    expect((request.request.body as FormData).has('commentId')).toBe(false);
    request.flush(saved);
  });

  it('downloads a file as a blob', () => {
    let blob: Blob | undefined;
    service.download('f1').subscribe((b) => (blob = b));

    const request = http.expectOne(`${API_URL}/attachments/f1/download`);
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['bytes']));

    expect(blob?.size).toBe(5);
  });

  it('removes a file', () => {
    service.remove('f1').subscribe();

    const request = http.expectOne(`${API_URL}/attachments/f1`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
  });

  describe('uploadAll', () => {
    it('sends the files one after another, and a failure does not stop the others', () => {
      let summary: UploadSummary | undefined;
      service.uploadAll('inc-1', [pick('a.png'), pick('b.exe'), pick('c.txt')], 'k1').subscribe((s) => (summary = s));

      // Only the first is sent at first; the next goes when it is done.
      http.expectOne(`${API_URL}/incidents/inc-1/attachments`).flush(saved);
      http
        .expectOne(`${API_URL}/incidents/inc-1/attachments`)
        .flush({ errors: { file: ['That type of file is not allowed.'] } }, { status: 400, statusText: 'Bad Request' });
      http.expectOne(`${API_URL}/incidents/inc-1/attachments`).flush(saved);

      expect(summary).toEqual({ uploaded: 2, failures: ['b.exe: That type of file is not allowed.'] });
    });

    it('answers at once when there is nothing to send', () => {
      let summary: UploadSummary | undefined;
      service.uploadAll('inc-1', []).subscribe((s) => (summary = s));

      expect(summary).toEqual({ uploaded: 0, failures: [] });
    });

    it('never fails: even an unreachable server comes back as a sentence', () => {
      let summary: UploadSummary | undefined;
      service.uploadAll('inc-1', [pick('a.png')]).subscribe((s) => (summary = s));

      http.expectOne(`${API_URL}/incidents/inc-1/attachments`).error(new ProgressEvent('error'));

      expect(summary?.uploaded).toBe(0);
      expect(summary?.failures[0]).toBe('a.png: the server could not be reached.');
    });
  });
});

describe('uploadProblem', () => {
  const failure = (status: number, body: unknown = {}) => new HttpErrorResponse({ status, error: body });

  it('says what the server said about the file', () => {
    expect(uploadProblem(failure(400, { errors: { file: ['That file is too big. The limit is 5 MB.'] } }))).toBe(
      'That file is too big. The limit is 5 MB.',
    );
  });

  it('uses the explanation of a refusal that has no field', () => {
    expect(uploadProblem(failure(403, { detail: 'Only the person who wrote a comment can attach files to it.' }))).toBe(
      'Only the person who wrote a comment can attach files to it.',
    );
  });

  it('has plain words for the usual failures', () => {
    expect(uploadProblem(failure(0))).toContain('could not be reached');
    expect(uploadProblem(failure(404))).toContain('no longer available');
    expect(uploadProblem(failure(413))).toContain('too big');
    expect(uploadProblem(failure(429))).toContain('wait a minute');
    expect(uploadProblem(failure(500))).toContain('try again');
    expect(uploadProblem(new Error('anything'))).toContain('try again');
  });
});
