import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';

import { API_URL } from '../../../core/api-config';
import { Attachment } from '../incident.models';
import { AttachmentList } from './attachment-list';

function file(overrides: Partial<Attachment> = {}): Attachment {
  return {
    id: 'f1',
    incidentId: 'inc-1',
    commentId: null,
    fileName: 'shot.png',
    contentType: 'image/png',
    sizeBytes: 2048,
    uploadedByUserId: 'u1',
    uploadedByEmail: 'dana@example.com',
    createdAtUtc: '2026-11-21T10:00:00Z',
    canRemove: true,
    ...overrides,
  };
}

describe('AttachmentList', () => {
  let fixture: ComponentFixture<AttachmentList>;
  let http: HttpTestingController;
  let removed: Attachment[];
  let snack: ReturnType<typeof vi.spyOn>;
  let click: ReturnType<typeof vi.spyOn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const buttonLabelled = (label: string) =>
    page().querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement | null;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function setUp(files: Attachment[], label?: string) {
    await TestBed.configureTestingModule({
      imports: [AttachmentList],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
    // The browser's own "save" is stood in for: a download is a click on a link with a "download" name.
    (URL as unknown as { createObjectURL: () => string }).createObjectURL = () => 'blob:fake';
    (URL as unknown as { revokeObjectURL: () => void }).revokeObjectURL = () => undefined;
    click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);

    fixture = TestBed.createComponent(AttachmentList);
    fixture.componentRef.setInput('attachments', files);
    if (label) {
      fixture.componentRef.setInput('label', label);
    }
    removed = [];
    fixture.componentInstance.removed.subscribe((f) => removed.push(f));
    await settle();
  }

  afterEach(() => {
    click?.mockRestore();
    TestBed.inject(MatDialog).closeAll();
  });

  /** Pretends the "Remove this file?" box was answered. */
  const confirmation = (answer: boolean | undefined) =>
    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(answer) } as never);

  it('shows nothing at all when there are no files', async () => {
    await setUp([]);

    expect(page().querySelector('ul')).toBeNull();
  });

  it('lists each file with its size, who attached it, and when', async () => {
    await setUp([file(), file({ id: 'f2', fileName: 'report.pdf', contentType: 'application/pdf', sizeBytes: 300000, uploadedByEmail: null })], 'Files on this comment');

    const text = page().textContent!;
    expect(text).toContain('shot.png');
    expect(text).toContain('2 KB');
    expect(text).toContain('dana@example.com');
    expect(text).toContain('Nov 21, 2026');
    expect(text).toContain('report.pdf');
    expect(text).toContain('Unknown user');
    expect(page().querySelector('ul')?.getAttribute('aria-label')).toBe('Files on this comment');
  });

  it('picks an icon for each kind of file', async () => {
    await setUp([
      file({ id: 'a', contentType: 'image/png' }),
      file({ id: 'b', contentType: 'application/pdf' }),
      file({ id: 'c', contentType: 'text/plain' }),
    ]);

    const icons = Array.from(page().querySelectorAll('.kind')).map((i) => i.textContent!.trim());
    expect(icons).toEqual(['image', 'picture_as_pdf', 'description']);
  });

  it('downloads a file under its own name', async () => {
    await setUp([file()]);

    buttonLabelled('Download shot.png')!.click();
    await settle();
    expect(buttonLabelled('Download shot.png')!.disabled).toBe(true); // busy while it is fetched

    http.expectOne(`${API_URL}/attachments/f1/download`).flush(new Blob(['png-bytes']));
    await settle();

    expect(click).toHaveBeenCalledTimes(1);
    expect(buttonLabelled('Download shot.png')!.disabled).toBe(false);
  });

  it('says so when a download fails, and saves nothing', async () => {
    await setUp([file()]);

    buttonLabelled('Download shot.png')!.click();
    await settle();
    http.expectOne(`${API_URL}/attachments/f1/download`).flush(new Blob(['boom']), { status: 500, statusText: 'Server Error' });
    await settle();

    expect(click).not.toHaveBeenCalled();
    expect(snack).toHaveBeenCalledWith(expect.stringContaining('shot.png could not be downloaded'), 'Dismiss', expect.anything());
  });

  it('offers "Remove" only on files the person may remove', async () => {
    await setUp([file(), file({ id: 'f2', fileName: 'locked.pdf', canRemove: false })]);

    expect(buttonLabelled('Remove shot.png')).not.toBeNull();
    expect(buttonLabelled('Remove locked.pdf')).toBeNull();
  });

  it('asks first, then removes the file and tells the page', async () => {
    await setUp([file()]);
    const ask = confirmation(true);

    buttonLabelled('Remove shot.png')!.click();
    await settle();

    expect(ask).toHaveBeenCalledTimes(1);
    expect(JSON.stringify(ask.mock.calls[0][1])).toContain('shot.png will be removed');
    const request = http.expectOne(`${API_URL}/attachments/f1`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    expect(removed.map((f) => f.id)).toEqual(['f1']);
  });

  it('does nothing when the question is cancelled', async () => {
    await setUp([file()]);
    confirmation(undefined);

    buttonLabelled('Remove shot.png')!.click();
    await settle();

    http.expectNone(`${API_URL}/attachments/f1`);
    expect(removed).toEqual([]);
  });

  it('says so when removing fails, and tells the page nothing', async () => {
    await setUp([file()]);
    confirmation(true);

    buttonLabelled('Remove shot.png')!.click();
    await settle();
    http.expectOne(`${API_URL}/attachments/f1`).flush({}, { status: 403, statusText: 'Forbidden' });
    await settle();

    expect(removed).toEqual([]);
    expect(snack).toHaveBeenCalledWith(expect.stringContaining('could not be removed'), 'Dismiss', expect.anything());
    expect(buttonLabelled('Remove shot.png')!.disabled).toBe(false);
  });
});
