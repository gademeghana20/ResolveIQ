import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { fieldErrors } from '../../../core/api-error';
import { notBlank } from '../../../core/validators';
import { IncidentDetail, MAX_DESCRIPTION_LENGTH, MAX_TITLE_LENGTH } from '../incident.models';
import { IncidentService } from '../incident.service';

export interface EditIncidentDialogData {
  incident: IncidentDetail;
}

/** Lets the reporter reword their incident. Saves by itself, and closes with the updated incident. */
@Component({
  selector: 'app-edit-incident-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Edit {{ data.incident.incidentNumber }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="fields">
        <p class="note">Every change is recorded in the history, with what it said before.</p>

        <mat-form-field appearance="outline">
          <mat-label>Title</mat-label>
          <input matInput formControlName="title" autocomplete="off" />
          <mat-hint align="end">{{ form.controls.title.value.length }} / {{ maxTitle }}</mat-hint>
          @if (form.controls.title.hasError('required')) {
            <mat-error>A title is required.</mat-error>
          } @else if (form.controls.title.hasError('maxlength')) {
            <mat-error>The title is too long: the limit is {{ maxTitle }} characters.</mat-error>
          } @else if (serverMessage('title'); as message) {
            <mat-error>{{ message }}</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Description</mat-label>
          <textarea matInput rows="8" formControlName="description"></textarea>
          <mat-hint align="end">{{ form.controls.description.value.length }} / {{ maxDescription }}</mat-hint>
          @if (form.controls.description.hasError('required')) {
            <mat-error>A description is required.</mat-error>
          } @else if (form.controls.description.hasError('maxlength')) {
            <mat-error>The description is too long: the limit is {{ maxDescription }} characters.</mat-error>
          } @else if (serverMessage('description'); as message) {
            <mat-error>{{ message }}</mat-error>
          }
        </mat-form-field>

        @if (errorMessage(); as message) {
          <p class="error" role="alert">{{ message }}</p>
        }
      </mat-dialog-content>

      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button mat-flat-button type="submit" [disabled]="saving() || !hasChanged()">
          {{ saving() ? 'Saving…' : 'Save changes' }}
        </button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .fields { display: flex; flex-direction: column; gap: 4px; min-width: 360px; max-width: 560px; padding-top: 8px; }
    .note { margin: 0 0 8px; color: var(--mat-sys-on-surface-variant); }
    .error { color: var(--mat-sys-error); margin: 0; }
  `,
})
export class EditIncidentDialog {
  private readonly incidents = inject(IncidentService);
  private readonly ref = inject<MatDialogRef<EditIncidentDialog, IncidentDetail>>(MatDialogRef);
  private readonly formBuilder = inject(NonNullableFormBuilder);
  protected readonly data = inject<EditIncidentDialogData>(MAT_DIALOG_DATA);

  protected readonly maxTitle = MAX_TITLE_LENGTH;
  protected readonly maxDescription = MAX_DESCRIPTION_LENGTH;

  protected readonly form = this.formBuilder.group({
    title: [this.data.incident.title, [notBlank, Validators.maxLength(MAX_TITLE_LENGTH)]],
    description: [this.data.incident.description, [notBlank, Validators.maxLength(MAX_DESCRIPTION_LENGTH)]],
  });

  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  /** Nothing to save until something is actually different (spaces around the text do not count). */
  protected hasChanged(): boolean {
    const { title, description } = this.form.getRawValue();
    return title.trim() !== this.data.incident.title || description.trim() !== this.data.incident.description;
  }

  protected save(): void {
    this.errorMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { title, description } = this.form.getRawValue();
    this.saving.set(true);

    this.incidents.updateIncident(this.data.incident.id, title.trim(), description.trim()).subscribe({
      next: (updated) => this.ref.close(updated),
      error: (error: unknown) => {
        this.saving.set(false);
        this.showError(error);
      },
    });
  }

  private showError(error: unknown): void {
    const problems = fieldErrors(error);
    let placed = false;

    for (const field of ['title', 'description'] as const) {
      if (problems?.[field]) {
        this.form.controls[field].setErrors({ server: problems[field] });
        this.form.controls[field].markAsTouched();
        placed = true;
      }
    }

    if (!placed) {
      const status = error instanceof HttpErrorResponse ? error.status : undefined;
      this.errorMessage.set(
        status === 0
          ? 'Cannot reach the server. Nothing was saved.'
          : status === 409
            ? 'Someone else changed this incident while you were editing. Close this box, look at the latest version, and try again.'
            : status === 403
              ? 'Only the person who reported an incident can edit it.'
              : status === 400
                ? (problems && Object.values(problems)[0]) || 'This incident can no longer be edited.'
                : 'Your changes could not be saved. Please try again.',
      );
    }
  }

  protected serverMessage(field: 'title' | 'description'): string | null {
    const message = this.form.controls[field].errors?.['server'];
    return typeof message === 'string' ? message : null;
  }
}
