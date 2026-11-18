import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { catchError, of } from 'rxjs';

import { fieldErrors } from '../../../core/api-error';
import { notBlank } from '../../../core/validators';
import { Category, IncidentPriority, MAX_DESCRIPTION_LENGTH, MAX_TITLE_LENGTH } from '../incident.models';
import { IncidentService } from '../incident.service';

@Component({
  selector: 'app-incident-create',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
  ],
  templateUrl: './incident-create.html',
  styleUrl: './incident-create.css',
})
export class IncidentCreate {
  private readonly incidents = inject(IncidentService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly maxTitle = MAX_TITLE_LENGTH;
  protected readonly maxDescription = MAX_DESCRIPTION_LENGTH;

  protected readonly form = this.formBuilder.group({
    title: ['', [notBlank, Validators.maxLength(MAX_TITLE_LENGTH)]],
    description: ['', [notBlank, Validators.maxLength(MAX_DESCRIPTION_LENGTH)]],
    categoryId: [''], // empty means "no category"
    urgency: [''], // empty means "not sure"
  });

  protected readonly urgencies: IncidentPriority[] = ['Low', 'Medium', 'High', 'Critical'];

  protected readonly categories = signal<Category[]>([]);
  protected readonly categoriesFailed = signal(false);
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  constructor() {
    // Only active categories can be chosen. If they cannot be loaded, the form still works without one.
    this.incidents
      .getCategories()
      .pipe(
        catchError(() => {
          this.categoriesFailed.set(true);
          return of([] as Category[]);
        }),
        takeUntilDestroyed(),
      )
      .subscribe((all) => this.categories.set(all.filter((c) => c.isActive)));
  }

  protected submit(): void {
    this.errorMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched(); // every field shows what is wrong
      return;
    }

    const { title, description, categoryId, urgency } = this.form.getRawValue();
    this.submitting.set(true);

    this.incidents
      .createIncident(title.trim(), description.trim(), categoryId || undefined, (urgency as IncidentPriority) || undefined)
      .subscribe({
      next: (created) => {
        this.snackBar.open(`Incident ${created.incidentNumber} created`, 'Dismiss', { duration: 6000 });
        this.router.navigate(['/incidents', created.id]);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.showError(error);
      },
    });
  }

  /** Puts the API's complaint next to the field it is about; anything else goes in a general message. */
  private showError(error: unknown): void {
    const problems = fieldErrors(error);

    if (problems) {
      const shown = this.applyFieldProblems(problems);
      if (shown) {
        return;
      }
    }

    this.errorMessage.set(
      error instanceof HttpErrorResponse && error.status === 0
        ? 'Cannot reach the server. Your incident was not saved.'
        : 'Your incident could not be saved. Please try again.',
    );
  }

  private applyFieldProblems(problems: Record<string, string>): boolean {
    let shown = false;
    const controls = this.form.controls;

    for (const [field, control] of [
      ['title', controls.title],
      ['description', controls.description],
      ['categoryid', controls.categoryId],
    ] as const) {
      if (problems[field]) {
        control.setErrors({ server: problems[field] });
        control.markAsTouched();
        shown = true;
      }
    }
    return shown;
  }

  protected serverMessage(control: 'title' | 'description' | 'categoryId'): string | null {
    const error = this.form.controls[control].errors?.['server'];
    return typeof error === 'string' ? error : null;
  }
}
