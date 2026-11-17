import { HttpErrorResponse } from '@angular/common/http';

/**
 * What the API said was wrong with each field, as { fieldName: "message" } (lower-cased names),
 * or null when the error is not a validation error.
 */
export function fieldErrors(error: unknown): Record<string, string> | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) {
    return null;
  }

  const errors: unknown = error.error?.errors;
  if (!errors || typeof errors !== 'object') {
    return null;
  }

  const result: Record<string, string> = {};
  for (const [field, messages] of Object.entries(errors)) {
    if (Array.isArray(messages) && typeof messages[0] === 'string') {
      result[field.toLowerCase()] = messages[0];
    }
  }
  return Object.keys(result).length > 0 ? result : null;
}

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
