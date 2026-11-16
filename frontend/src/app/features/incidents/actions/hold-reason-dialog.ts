import { Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { notBlank } from '../../../core/validators';

export const MAX_REASON_LENGTH = 500;

/** Asks why work is paused. Closes with the reason text, or nothing if cancelled. */
@Component({
  selector: 'app-hold-reason-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Put on hold</h2>
    <mat-dialog-content>
      <p>The SLA clock pauses while an incident is on hold. Say why work is paused.</p>
      <mat-form-field appearance="outline" class="full">
        <mat-label>Reason</mat-label>
        <textarea matInput rows="3" [formControl]="reason"></textarea>
        <mat-hint align="end">{{ reason.value.length }} / {{ maxLength }}</mat-hint>
        @if (reason.hasError('required')) {
          <mat-error>A reason is required.</mat-error>
        } @else if (reason.hasError('maxlength')) {
          <mat-error>The limit is {{ maxLength }} characters.</mat-error>
        }
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>Cancel</button>
      <button mat-flat-button type="button" (click)="confirm()">Put on hold</button>
    </mat-dialog-actions>
  `,
  styles: '.full { width: 100%; min-width: 280px; }',
})
export class HoldReasonDialog {
  private readonly ref = inject<MatDialogRef<HoldReasonDialog, string>>(MatDialogRef);

  protected readonly maxLength = MAX_REASON_LENGTH;
  protected readonly reason = new FormControl('', {
    nonNullable: true,
    validators: [notBlank, Validators.maxLength(MAX_REASON_LENGTH)],
  });

  protected confirm(): void {
    if (this.reason.invalid) {
      this.reason.markAsTouched();
      return;
    }
    this.ref.close(this.reason.value.trim());
  }
}
