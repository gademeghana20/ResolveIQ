import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';

import { INCIDENT_PRIORITIES, IncidentPriority, UserSummary } from '../incident.models';
import { IncidentService } from '../incident.service';

export interface AssignDialogData {
  currentAssigneeId: string | null;
  currentPriority: IncidentPriority | null;
}

export interface AssignDialogResult {
  assigneeUserId: string;
  priority: IncidentPriority;
}

/** Picks who should work on the incident, and its priority. */
@Component({
  selector: 'app-assign-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatSelectModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.currentAssigneeId ? 'Reassign incident' : 'Assign incident' }}</h2>
    <mat-dialog-content>
      @if (loadFailed()) {
        <p class="error" role="alert">The list of people could not be loaded. Close this box and try again.</p>
      } @else {
        @if (!data.currentPriority) {
          <p>This incident has no priority yet. Set one now, and the SLA clock starts when you assign it.</p>
        }
        <form [formGroup]="form" class="fields">
          <mat-form-field appearance="outline">
            <mat-label>Assign to</mat-label>
            <mat-select formControlName="assignee" [disabled]="people().length === 0">
              @for (person of people(); track person.id) {
                <mat-option [value]="person.id">
                  {{ person.email }}{{ person.id === data.currentAssigneeId ? ' (current)' : '' }}
                </mat-option>
              }
            </mat-select>
            @if (form.controls.assignee.hasError('required')) {
              <mat-error>Choose who should work on this.</mat-error>
            }
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Priority</mat-label>
            <mat-select formControlName="priority">
              @for (option of priorities; track option) {
                <mat-option [value]="option">{{ option }}</mat-option>
              }
            </mat-select>
            @if (form.controls.priority.hasError('required')) {
              <mat-error>Choose a priority.</mat-error>
            }
          </mat-form-field>
        </form>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>Cancel</button>
      <button mat-flat-button type="button" [disabled]="loadFailed()" (click)="confirm()">Assign</button>
    </mat-dialog-actions>
  `,
  styles: `
    .fields { display: flex; flex-direction: column; min-width: 300px; }
    .error { color: var(--mat-sys-error); }
  `,
})
export class AssignDialog {
  private readonly incidents = inject(IncidentService);
  private readonly ref = inject<MatDialogRef<AssignDialog, AssignDialogResult>>(MatDialogRef);
  protected readonly data = inject<AssignDialogData>(MAT_DIALOG_DATA);

  protected readonly priorities = INCIDENT_PRIORITIES;
  protected readonly people = signal<UserSummary[]>([]);
  protected readonly loadFailed = signal(false);

  protected readonly form = new FormGroup({
    assignee: new FormControl<string | null>(this.data.currentAssigneeId, Validators.required),
    priority: new FormControl<IncidentPriority | null>(this.data.currentPriority, Validators.required),
  });

  constructor() {
    this.incidents.getUsers().subscribe({
      // A deactivated person cannot log in, so the API refuses to give them work. Do not offer them.
      next: (users) => this.people.set(users.filter((user) => user.isActive)),
      error: () => this.loadFailed.set(true),
    });
  }

  protected confirm(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { assignee, priority } = this.form.getRawValue();
    this.ref.close({ assigneeUserId: assignee!, priority: priority! });
  }
}
