import { Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';

import { INCIDENT_PRIORITIES, IncidentPriority } from '../incident.models';

export interface PriorityDialogData {
  current: IncidentPriority | null;
}

/** Picks a new priority. Closes with the chosen priority, or nothing if cancelled. */
@Component({
  selector: 'app-priority-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatSelectModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Change priority</h2>
    <mat-dialog-content>
      <p>Changing the priority restarts the SLA clock, and the change is recorded in the history.</p>
      <mat-form-field appearance="outline" class="full">
        <mat-label>Priority</mat-label>
        <mat-select [formControl]="priority">
          @for (option of priorities; track option) {
            <mat-option [value]="option">{{ option }}</mat-option>
          }
        </mat-select>
        @if (priority.hasError('required')) {
          <mat-error>Choose a priority.</mat-error>
        }
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>Cancel</button>
      <button mat-flat-button type="button" [disabled]="!hasChanged()" (click)="confirm()">Change priority</button>
    </mat-dialog-actions>
  `,
  styles: '.full { width: 100%; min-width: 280px; }',
})
export class PriorityDialog {
  private readonly ref = inject<MatDialogRef<PriorityDialog, IncidentPriority>>(MatDialogRef);
  private readonly data = inject<PriorityDialogData>(MAT_DIALOG_DATA);

  protected readonly priorities = INCIDENT_PRIORITIES;
  protected readonly priority = new FormControl<IncidentPriority | null>(this.data.current, Validators.required);

  protected hasChanged(): boolean {
    return this.priority.value !== null && this.priority.value !== this.data.current;
  }

  protected confirm(): void {
    if (this.hasChanged()) {
      this.ref.close(this.priority.value!);
    }
  }
}
