import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { fieldErrors } from '../../../core/api-error';
import { Category } from '../../../core/category.models';
import { notBlank } from '../../../core/validators';
import { AdminService } from '../admin.service';

/** The same limits the API enforces. */
export const MAX_NAME_LENGTH = 100;
export const MAX_DESCRIPTION_LENGTH = 500;

export interface CategoryDialogData {
  /** Leave out to create a new category; give one to rename or re-describe it. */
  category?: Category;
}

/** Creates a category, or edits one. Saves by itself, and closes with the saved category. */
@Component({
  selector: 'app-category-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ editing ? 'Edit category' : 'New category' }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="fields">
        <mat-form-field appearance="outline">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" autocomplete="off" />
          <mat-hint align="end">{{ form.controls.name.value.length }} / {{ maxName }}</mat-hint>
          @if (form.controls.name.hasError('required')) {
            <mat-error>A name is required.</mat-error>
          } @else if (form.controls.name.hasError('maxlength')) {
            <mat-error>The name is too long: the limit is {{ maxName }} characters.</mat-error>
          } @else if (serverMessage('name'); as message) {
            <mat-error>{{ message }}</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Description (optional)</mat-label>
          <textarea matInput rows="3" formControlName="description"></textarea>
          <mat-hint align="end">{{ form.controls.description.value.length }} / {{ maxDescription }}</mat-hint>
          @if (form.controls.description.hasError('maxlength')) {
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
        <button mat-flat-button type="submit" [disabled]="saving()">
          {{ saving() ? 'Saving…' : editing ? 'Save' : 'Create category' }}
        </button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .fields { display: flex; flex-direction: column; gap: 4px; min-width: 340px; padding-top: 8px; }
    .error { color: var(--mat-sys-error); margin: 0; }
  `,
})
export class CategoryDialog {
  private readonly admin = inject(AdminService);
  private readonly ref = inject<MatDialogRef<CategoryDialog, Category>>(MatDialogRef);
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly data = inject<CategoryDialogData>(MAT_DIALOG_DATA);

  protected readonly editing = !!this.data.category;
  protected readonly maxName = MAX_NAME_LENGTH;
  protected readonly maxDescription = MAX_DESCRIPTION_LENGTH;

  protected readonly form = this.formBuilder.group({
    name: [this.data.category?.name ?? '', [notBlank, Validators.maxLength(MAX_NAME_LENGTH)]],
    description: [this.data.category?.description ?? '', Validators.maxLength(MAX_DESCRIPTION_LENGTH)],
  });

  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected save(): void {
    this.errorMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const name = this.form.controls.name.value.trim();
    const description = this.form.controls.description.value.trim() || null;
    const existing = this.data.category;

    this.saving.set(true);

    const request = existing
      ? this.admin.updateCategory(existing.id, { name, description, isActive: existing.isActive }) // keeps it hidden or shown as it was
      : this.admin.createCategory(name, description);

    request.subscribe({
      next: (saved) => this.ref.close(saved),
      error: (error: unknown) => {
        this.saving.set(false);
        this.showError(error);
      },
    });
  }

  private showError(error: unknown): void {
    const problems = fieldErrors(error);
    let placed = false;

    for (const field of ['name', 'description'] as const) {
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
          : status === 404
            ? 'This category no longer exists. Close this box and refresh the list.'
            : 'The category could not be saved. Please try again.',
      );
    }
  }

  protected serverMessage(field: 'name' | 'description'): string | null {
    const message = this.form.controls[field].errors?.['server'];
    return typeof message === 'string' ? message : null;
  }
}
