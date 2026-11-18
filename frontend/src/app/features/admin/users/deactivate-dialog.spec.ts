import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { DeactivateDialog, DeactivateDialogData } from './deactivate-dialog';

describe('DeactivateDialog', () => {
  let fixture: ComponentFixture<DeactivateDialog>;
  let close: ReturnType<typeof vi.fn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const confirmButton = () =>
    Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.trim() === 'Deactivate') as HTMLButtonElement;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function open(data: Partial<DeactivateDialogData> = {}) {
    close = vi.fn();
    await TestBed.configureTestingModule({
      imports: [DeactivateDialog],
      providers: [
        { provide: MatDialogRef, useValue: { close } },
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            email: 'dana@example.com',
            count: 3,
            candidates: [{ id: 'u2', email: 'sam@example.com' }],
            ...data,
          },
        },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(DeactivateDialog);
    await settle();
  }

  it('warns with the number of unfinished incidents', async () => {
    await open();

    expect(page().textContent).toContain('3 unfinished incidents are');
    expect(page().textContent).toContain('still assigned to them');
  });

  it('says "1 unfinished incident is" for a single one', async () => {
    await open({ count: 1 });

    expect(page().textContent).toContain('1 unfinished incident is');
  });

  it('suggests handing them over, and will not go ahead until someone is chosen', async () => {
    await open();

    expect(page().textContent).toContain('Hand them to');
    expect(confirmButton().disabled).toBe(true);
  });

  it('closes with the chosen person', async () => {
    await open();
    (fixture.componentInstance as unknown as { target: { setValue: (v: string) => void } }).target.setValue('u2');
    await settle();

    confirmButton().click();

    expect(close).toHaveBeenCalledWith({ reassignToUserId: 'u2' });
  });

  it('closes with no one when the incidents are left where they are', async () => {
    await open();
    (fixture.componentInstance as unknown as { choice: { setValue: (v: string) => void } }).choice.setValue('keep');
    await settle();

    expect(confirmButton().disabled).toBe(false);
    confirmButton().click();

    expect(close).toHaveBeenCalledWith({ reassignToUserId: null });
  });

  it('only offers leaving them when there is nobody to hand them to', async () => {
    await open({ candidates: [] });

    expect(page().textContent).not.toContain('Hand them to\n');
    expect(confirmButton().disabled).toBe(false);
  });
});
