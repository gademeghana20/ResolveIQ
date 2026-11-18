import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialogRef } from '@angular/material/dialog';

import { MAX_RESOLUTION_LENGTH, ResolveNotesDialog } from './resolve-notes-dialog';

describe('ResolveNotesDialog', () => {
  let fixture: ComponentFixture<ResolveNotesDialog>;
  let close: ReturnType<typeof vi.fn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const box = () => page().querySelector('textarea') as HTMLTextAreaElement;
  const confirmButton = () =>
    Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.trim() === 'Mark resolved') as HTMLButtonElement;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function typeAndConfirm(value: string) {
    box().value = value;
    box().dispatchEvent(new Event('input'));
    confirmButton().click();
    await settle();
  }

  beforeEach(async () => {
    close = vi.fn();
    await TestBed.configureTestingModule({
      imports: [ResolveNotesDialog],
      providers: [
        { provide: MatDialogRef, useValue: { close } },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(ResolveNotesDialog);
    await settle();
  });

  it('explains that the reporter reads the answer before confirming', () => {
    expect(page().textContent).toContain('Say how it was fixed');
    expect(page().textContent).toContain('reads this before confirming');
  });

  it('will not accept an empty answer, and says why', async () => {
    await typeAndConfirm('');

    expect(close).not.toHaveBeenCalled();
    expect(page().textContent).toContain('Say how it was fixed.');
  });

  it('will not accept an answer that is only spaces', async () => {
    await typeAndConfirm('     ');

    expect(close).not.toHaveBeenCalled();
  });

  it('closes with the answer, trimmed', async () => {
    await typeAndConfirm('  Replaced the roller.  ');

    expect(close).toHaveBeenCalledWith('Replaced the roller.');
  });

  it('counts the characters and refuses more than the limit', async () => {
    box().value = 'abc';
    box().dispatchEvent(new Event('input'));
    await settle();
    expect(page().textContent).toContain(`3 / ${MAX_RESOLUTION_LENGTH}`);

    await typeAndConfirm('x'.repeat(MAX_RESOLUTION_LENGTH + 1));

    expect(close).not.toHaveBeenCalled();
    expect(page().textContent).toContain(`The limit is ${MAX_RESOLUTION_LENGTH} characters.`);
  });
});
