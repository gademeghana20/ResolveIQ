import { AbstractControl, ValidationErrors } from '@angular/forms';

/** Text must contain something other than spaces. Angular's own "required" accepts "   ". */
export function notBlank(control: AbstractControl<string | null>): ValidationErrors | null {
  return (control.value ?? '').trim().length > 0 ? null : { required: true };
}
