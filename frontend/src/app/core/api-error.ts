import { HttpErrorResponse } from '@angular/common/http';

/**
 * The first validation message the API sent back (it answers a rejected request with
 * { errors: { Field: ["message"] } }), or null when there is none.
 */
export function firstValidationMessage(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) {
    return null;
  }

  const errors: unknown = error.error?.errors;
  if (errors && typeof errors === 'object') {
    for (const messages of Object.values(errors)) {
      if (Array.isArray(messages) && typeof messages[0] === 'string') {
        return messages[0];
      }
    }
  }
  return null;
}
