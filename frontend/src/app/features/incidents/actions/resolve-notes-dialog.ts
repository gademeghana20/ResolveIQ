import { Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { notBlank } from '../../../core/validators';

export const MAX_RESOLUTION_LENGTH = 2000;

/** Asks how the problem was fixed. Closes with the notes text, or nothing if cancelled. */
@Component({
  selector: 'app-resolve-notes-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Mark as resolved</h2>
    <mat-dialog-content>
      <p>Say how it was fixed. The person who reported it reads this before confirming the fix.</p>
      <mat-form-field appearance="outline" class="full">
        <mat-label>How was it fixed?</mat-label>
        <textarea matInput rows="4" [formControl]="notes"></textarea>
        <mat-hint align="end">{{ notes.value.length }} / {{ maxLength }}</mat-hint>
        @if (notes.hasError('required')) {
          <mat-error>Say how it was fixed.</mat-error>
        } @else if (notes.hasError('maxlength')) {
          <mat-error>The limit is {{ maxLength }} characters.</mat-error>
        }
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>Cancel</button>
      <button mat-flat-button type="button" (click)="confirm()">Mark resolved</button>
    </mat-dialog-actions>
  `,
  styles: '.full { width: 100%; min-width: 320px; }',
})
export class ResolveNotesDialog {
  private readonly ref = inject<MatDialogRef<ResolveNotesDialog, string>>(MatDialogRef);

  protected readonly maxLength = MAX_RESOLUTION_LENGTH;
  protected readonly notes = new FormControl('', {
    nonNullable: true,
    validators: [notBlank, Validators.maxLength(MAX_RESOLUTION_LENGTH)],
  });

  protected confirm(): void {
    if (this.notes.invalid) {
      this.notes.markAsTouched();
      return;
    }
    this.ref.close(this.notes.value.trim());
  }
}
