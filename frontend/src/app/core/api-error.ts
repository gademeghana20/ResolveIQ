import { HttpErrorResponse } from '@angular/common/http';

/**
 * Every complaint the API made, in order, as { field, message } pairs (field names lower-cased), or null when
 * the error is not a validation error. Unlike fieldErrors, nothing is dropped: a password that breaks three
 * rules gives three messages.
 */
export function allFieldMessages(error: unknown): { field: string; message: string }[] | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) {
    return null;
  }

  const errors: unknown = error.error?.errors;
  if (!errors || typeof errors !== 'object') {
    return null;
  }

  const result: { field: string; message: string }[] = [];
  for (const [field, messages] of Object.entries(errors)) {
    if (Array.isArray(messages)) {
      for (const message of messages) {
        if (typeof message === 'string') {
          result.push({ field: field.toLowerCase(), message });
        }
      }
    }
  }
  return result.length > 0 ? result : null;
}

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
