import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import { ErrorStateMatcher } from '@angular/material/core';

/** Text must contain something other than spaces. Angular's own "required" accepts "   ". */
export function notBlank(control: AbstractControl<string | null>): ValidationErrors | null {
  return (control.value ?? '').trim().length > 0 ? null : { required: true };
}

// Something, an @, then at least one "label." and an ending of two or more characters. No spaces, no empty parts.
// The API applies the same rule, so what the screen accepts is what the API accepts.
const EMAIL_WITH_DOMAIN = /^[^\s@]+@([^\s@.]+\.)+[^\s@.]{2,}$/;

/**
 * A full email address such as name@example.com. Angular's built-in Validators.email also accepts
 * "name@example" (no ending), which on the public internet is nearly always a typo. Use this where an address
 * is first accepted (sign-up, creating a user), and keep the built-in one on sign-in so older accounts still work.
 * An empty box is left to the "required" check. The error is called "email", like the built-in one.
 */
export function emailWithDomain(control: AbstractControl<string | null>): ValidationErrors | null {
  const value = (control.value ?? '').trim();
  return value === '' || EMAIL_WITH_DOMAIN.test(value) ? null : { email: true };
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
