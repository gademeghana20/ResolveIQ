import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { API_URL } from '../../../core/api-config';
import { ATTACHMENT_SETTINGS } from '../../../testing/attachment-settings';
import { AttachmentSettings } from '../incident.models';
import { AttachmentPicker } from './attachment-picker';

const file = (name: string, size = 100) => new File([new Uint8Array(size)], name);

describe('AttachmentPicker', () => {
  let fixture: ComponentFixture<AttachmentPicker>;
  let http: HttpTestingController;

  const page = () => fixture.nativeElement as HTMLElement;
  const chooser = () => page().querySelector('input[type="file"]') as HTMLInputElement | null;
  const problem = () => page().querySelector('[role="alert"]')?.textContent?.trim() ?? null;
  const chosenNames = () => Array.from(page().querySelectorAll('.chosen .name')).map((e) => e.textContent!.trim());

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function setUp(settings: AttachmentSettings | 'fail' | null, kind: 'comment' | 'incident' = 'incident') {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      imports: [AttachmentPicker],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AttachmentPicker);
    fixture.componentRef.setInput('kind', kind);
    await settle();

    const request = http.expectOne(`${API_URL}/attachments/settings`);
    if (settings === 'fail') {
      request.flush({}, { status: 500, statusText: 'Server Error' });
    } else if (settings) {
      request.flush(settings);
    }
    await settle();
  }

  /** Pretends the person chose these files in the file window. */
  async function choose(...files: File[]) {
    const input = chooser()!;
    Object.defineProperty(input, 'files', { value: files, configurable: true });
    input.dispatchEvent(new Event('change'));
    await settle();
  }

  it('shows nothing while the limits are still being fetched', async () => {
    await setUp(null);

    expect(page().querySelector('button')).toBeNull();
  });

  it('shows nothing when attaching is switched off on this site', async () => {
    await setUp({ ...ATTACHMENT_SETTINGS, enabled: false });

    expect(page().querySelector('button')).toBeNull();
    expect(chooser()).toBeNull();
  });

  it('shows nothing, rather than a broken control, when the limits cannot be loaded', async () => {
    await setUp('fail');

    expect(page().querySelector('button')).toBeNull();
  });

  it('offers the button, and says what is allowed', async () => {
    await setUp(ATTACHMENT_SETTINGS);

    expect(page().querySelector('button')?.textContent).toContain('Attach files');
    expect(page().textContent).toContain('Up to 20 files, 5 MB each');
    expect(page().textContent).toContain('.png');
    expect(chooser()?.accept).toContain('.pdf');
    expect(chooser()?.multiple).toBe(true);
  });

  it('uses the smaller limit for a comment', async () => {
    await setUp(ATTACHMENT_SETTINGS, 'comment');

    expect(page().textContent).toContain('Up to 5 files');
  });

  it('lists the files that were chosen, and hands them to the page that owns the picker', async () => {
    await setUp(ATTACHMENT_SETTINGS);

    await choose(file('shot.png'), file('notes.txt', 2048));

    expect(chosenNames()).toEqual(['shot.png', 'notes.txt']);
    expect(page().textContent).toContain('2 KB');
    expect(fixture.componentInstance.files().map((f) => f.name)).toEqual(['shot.png', 'notes.txt']);
    expect(problem()).toBeNull();
  });

  it('keeps adding to the list when more files are chosen later, and ignores the same file twice', async () => {
    await setUp(ATTACHMENT_SETTINGS);

    await choose(file('a.png'));
    await choose(file('a.png'), file('b.png'));

    expect(chosenNames()).toEqual(['a.png', 'b.png']);
  });

  it('refuses a file that cannot be attached, says why, and still keeps the good ones', async () => {
    await setUp(ATTACHMENT_SETTINGS);

    await choose(file('ok.png'), file('program.exe'), file('huge.png', 5 * 1024 * 1024 + 1), file('empty.txt', 0));

    expect(chosenNames()).toEqual(['ok.png']);
    expect(problem()).toContain('program.exe');
    expect(problem()).toContain('not allowed');
    expect(problem()).toContain('huge.png');
    expect(problem()).toContain('too big');
    expect(problem()).toContain('empty.txt');
  });

  it('stops at the limit for the number of files', async () => {
    await setUp({ ...ATTACHMENT_SETTINGS, maxFilesPerIncident: 2 });

    await choose(file('1.png'), file('2.png'), file('3.png'));

    expect(chosenNames()).toEqual(['1.png', '2.png']);
    expect(problem()).toContain('3.png');
    expect(problem()).toContain('at most 2');
  });

  it('lets a file be taken off the list, and clears the old complaint', async () => {
    await setUp(ATTACHMENT_SETTINGS);
    await choose(file('a.png'), file('b.png'), file('bad.exe'));
    expect(problem()).not.toBeNull();

    (page().querySelector('.chosen button') as HTMLButtonElement).click();
    await settle();

    expect(chosenNames()).toEqual(['b.png']);
    expect(problem()).toBeNull();
    expect(page().querySelector('.chosen button')?.getAttribute('aria-label')).toBe('Remove b.png from the list');
  });

  it('can be switched off while the page is busy sending', async () => {
    await setUp(ATTACHMENT_SETTINGS);
    await choose(file('a.png'));

    fixture.componentRef.setInput('disabled', true);
    await settle();

    expect(Array.from(page().querySelectorAll('button')).every((b) => b.disabled)).toBe(true);
  });
});
