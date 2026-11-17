import { HttpErrorResponse } from '@angular/common/http';

import { allFieldMessages, fieldErrors, firstValidationMessage } from './api-error';

const badRequest = (body: unknown) => new HttpErrorResponse({ status: 400, error: body });

describe('fieldErrors', () => {
  it('maps each field to its first message, with lower-case names', () => {
    const error = badRequest({ errors: { Title: ['Too long', 'Second'], Description: ['Required'] } });

    expect(fieldErrors(error)).toEqual({ title: 'Too long', description: 'Required' });
  });

  it('is null for a 400 that has no field errors', () => {
    expect(fieldErrors(badRequest({}))).toBeNull();
    expect(fieldErrors(badRequest({ errors: {} }))).toBeNull();
    expect(fieldErrors(badRequest(null))).toBeNull();
  });

  it('is null for any other kind of failure', () => {
    expect(fieldErrors(new HttpErrorResponse({ status: 500 }))).toBeNull();
    expect(fieldErrors(new HttpErrorResponse({ status: 0 }))).toBeNull();
    expect(fieldErrors(new Error('boom'))).toBeNull();
  });

  it('ignores entries that are not a list of text messages', () => {
    expect(fieldErrors(badRequest({ errors: { Title: 'oops', Description: [] } }))).toBeNull();
  });
});

describe('allFieldMessages', () => {
  it('keeps every message, in order, with lower-case field names', () => {
    const error = badRequest({
      errors: {
        PasswordTooShort: ['Too short'],
        PasswordRequiresUpper: ['Needs a capital', 'Needs another thing'],
        DuplicateUserName: ['Taken'],
      },
    });

    expect(allFieldMessages(error)).toEqual([
      { field: 'passwordtooshort', message: 'Too short' },
      { field: 'passwordrequiresupper', message: 'Needs a capital' },
      { field: 'passwordrequiresupper', message: 'Needs another thing' },
      { field: 'duplicateusername', message: 'Taken' },
    ]);
  });

  it('is null when there are no messages', () => {
    expect(allFieldMessages(badRequest({}))).toBeNull();
    expect(allFieldMessages(badRequest({ errors: {} }))).toBeNull();
    expect(allFieldMessages(badRequest(null))).toBeNull();
  });

  it('is null for any failure that is not a validation error', () => {
    expect(allFieldMessages(new HttpErrorResponse({ status: 500 }))).toBeNull();
    expect(allFieldMessages(new HttpErrorResponse({ status: 0 }))).toBeNull();
    expect(allFieldMessages(new Error('boom'))).toBeNull();
  });

  it('skips anything that is not text', () => {
    expect(allFieldMessages(badRequest({ errors: { A: [1, null, 'real'] } }))).toEqual([{ field: 'a', message: 'real' }]);
  });
});

describe('firstValidationMessage', () => {
  it('returns the first message the API gave', () => {
    expect(firstValidationMessage(badRequest({ errors: { Status: ['Not allowed'] } }))).toBe('Not allowed');
  });

  it('is null when there is none', () => {
    expect(firstValidationMessage(badRequest({}))).toBeNull();
    expect(firstValidationMessage(new HttpErrorResponse({ status: 404 }))).toBeNull();
  });
});
