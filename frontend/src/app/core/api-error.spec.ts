import { HttpErrorResponse } from '@angular/common/http';

import { fieldErrors, firstValidationMessage } from './api-error';

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

describe('firstValidationMessage', () => {
  it('returns the first message the API gave', () => {
    expect(firstValidationMessage(badRequest({ errors: { Status: ['Not allowed'] } }))).toBe('Not allowed');
  });

  it('is null when there is none', () => {
    expect(firstValidationMessage(badRequest({}))).toBeNull();
    expect(firstValidationMessage(new HttpErrorResponse({ status: 404 }))).toBeNull();
  });
});
