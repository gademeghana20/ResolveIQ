import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import { ErrorStateMatcher } from '@angular/material/core';

/** Text must contain something other than spaces. Angular's own "required" accepts "   ". */
export function notBlank(control: AbstractControl<string | null>): ValidationErrors | null {
  return (control.value ?? '').trim().length > 0 ? null : { required: true };
}

/**
 * For a form group: the two named fields must hold the same text (a password and its repeat).
 * The group gets the error { mismatch: true }.
 */
export function fieldsMatch(first: string, second: string): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null =>
    group.get(first)?.value === group.get(second)?.value ? null : { mismatch: true };
}

/**
 * A mismatch is an error of the whole form, but it should show on the "repeat" box. A Material field only
 * shows an error when its own control is invalid, so this tells it to count the form's mismatch as well.
 */
export const mismatchErrorMatcher: ErrorStateMatcher = {
  isErrorState: (control) => !!control && control.touched && !!control.parent?.hasError('mismatch'),
};
