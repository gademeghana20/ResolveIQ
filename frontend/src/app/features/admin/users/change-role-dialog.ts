import { Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';

import { Role } from '../../../core/auth/auth.models';
import { ROLES } from './create-user-dialog';

export interface ChangeRoleDialogData {
  email: string;
  current: Role | null;
}

/** Picks a different role for a person. Closes with the chosen role, or nothing if cancelled. */
@Component({
  selector: 'app-change-role-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatSelectModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Change role</h2>
    <mat-dialog-content>
      <p>
        Choose a new role for <strong>{{ data.email }}</strong
        >. They are signed out at once and sign in again with their new permissions.
      </p>
      <mat-form-field appearance="outline" class="full">
        <mat-label>Role</mat-label>
        <mat-select [formControl]="role">
          @for (option of roles; track option) {
            <mat-option [value]="option">{{ option }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>Cancel</button>
      <button mat-flat-button type="button" [disabled]="!hasChanged()" (click)="confirm()">Change role</button>
    </mat-dialog-actions>
  `,
  styles: '.full { width: 100%; min-width: 280px; }',
})
export class ChangeRoleDialog {
  private readonly ref = inject<MatDialogRef<ChangeRoleDialog, Role>>(MatDialogRef);
  protected readonly data = inject<ChangeRoleDialogData>(MAT_DIALOG_DATA);

  protected readonly roles = ROLES;
  protected readonly role = new FormControl<Role | null>(this.data.current);

  protected hasChanged(): boolean {
    return this.role.value !== null && this.role.value !== this.data.current;
  }

  protected confirm(): void {
    if (this.hasChanged()) {
      this.ref.close(this.role.value!);
    }
  }
}
