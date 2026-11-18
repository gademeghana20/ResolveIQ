import { Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';

export interface DeactivateDialogData {
  email: string;
  /** How many unfinished incidents are assigned to this person. */
  count: number;
  /** Active people the incidents could go to. */
  candidates: { id: string; email: string }[];
}

/** What the Admin chose: the person to hand the incidents to, or null to leave them where they are. */
export interface DeactivateResult {
  reassignToUserId: string | null;
}

/** The warning for deactivating someone who still has unfinished incidents, with the choice of handing them over. */
@Component({
  selector: 'app-deactivate-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatRadioModule, MatFormFieldModule, MatSelectModule],
  template: `
    <h2 mat-dialog-title>Deactivate {{ data.email }}?</h2>
    <mat-dialog-content>
      <p class="warning" role="alert">
        <strong>{{ data.count === 1 ? '1 unfinished incident is' : data.count + ' unfinished incidents are' }}</strong>
        still assigned to them. Nobody can work on {{ data.count === 1 ? 'it' : 'them' }} while it stays that way.
      </p>

      <mat-radio-group aria-label="What to do with their incidents" [formControl]="choice" class="choices">
        <mat-radio-button value="move">Hand them to someone else</mat-radio-button>
        <mat-radio-button value="keep">Leave them assigned to {{ data.email }}</mat-radio-button>
      </mat-radio-group>

      @if (choice.value === 'move') {
        <mat-form-field appearance="outline" class="full">
          <mat-label>Hand them to</mat-label>
          <mat-select [formControl]="target" panelWidth="360px">
            @for (person of data.candidates; track person.id) {
              <mat-option [value]="person.id">{{ person.email }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      }

      <p class="note">They are signed out at once and cannot sign in until you reactivate them.</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>Cancel</button>
      <button mat-flat-button type="button" [disabled]="!canConfirm()" (click)="confirm()">Deactivate</button>
    </mat-dialog-actions>
  `,
  styles: `
    .warning {
      margin-top: 0;
    }
    .choices {
      display: flex;
      flex-direction: column;
      margin-bottom: 8px;
    }
    .full {
      width: 100%;
      min-width: 300px;
    }
    .note {
      color: var(--mat-sys-on-surface-variant);
      margin-bottom: 0;
    }
  `,
})
export class DeactivateDialog {
  private readonly ref = inject<MatDialogRef<DeactivateDialog, DeactivateResult>>(MatDialogRef);
  protected readonly data = inject<DeactivateDialogData>(MAT_DIALOG_DATA);

  // Handing them over is the suggested choice, as long as there is someone to hand them to.
  protected readonly choice = new FormControl<'move' | 'keep'>(this.data.candidates.length > 0 ? 'move' : 'keep', { nonNullable: true });
  protected readonly target = new FormControl<string | null>(null);

  protected canConfirm(): boolean {
    return this.choice.value === 'keep' || !!this.target.value;
  }

  protected confirm(): void {
    if (this.canConfirm()) {
      this.ref.close({ reassignToUserId: this.choice.value === 'move' ? this.target.value : null });
    }
  }
}
