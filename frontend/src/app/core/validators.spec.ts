import { FormControl, FormGroup } from '@angular/forms';

import { emailWithDomain, fieldsMatch, mismatchErrorMatcher, notBlank } from './validators';

describe('notBlank', () => {
  it('accepts text', () => {
    expect(notBlank(new FormControl('hello'))).toBeNull();
  });

  it.each(['', '   ', '\n\t'])('refuses %j, which has nothing a person can read', (value) => {
    expect(notBlank(new FormControl(value))).toEqual({ required: true });
  });

  it('refuses nothing at all', () => {
    expect(notBlank(new FormControl(null))).toEqual({ required: true });
  });
});

describe('emailWithDomain', () => {
  const check = (value: string | null) => emailWithDomain(new FormControl(value));

  it.each([
    'name@example.com',
    'first.last@example.com',
    'name+tag@example.com',
    'name@mail.example.com',
    'name@example.co.uk',
    'n@e.co',
    '  name@example.com  ', // surrounding spaces are ignored; the screens trim before sending
  ])('accepts %j', (value) => {
    expect(check(value)).toBeNull();
  });

  it.each([
    'test1@example', // no ending: the case that slipped through
    'name@localhost',
    'name@gmail',
    'name@example.',
    'name@.com',
    'name@example..com',
    'name@example.c',
    '@example.com',
    'name@',
    'name',
    'na me@example.com',
    'name@@example.com',
  ])('refuses %j, with the same "email" error the built-in check uses', (value) => {
    expect(check(value)).toEqual({ email: true });
  });

  it('leaves an empty box to the "required" check', () => {
    expect(check('')).toBeNull();
    expect(check('   ')).toBeNull();
    expect(check(null)).toBeNull();
  });

  it('agrees with the API on the cases that matter, so a screen never accepts what the API would refuse', () => {
    // The same table is in the API's tests (EmailWithDomainTests). Keep the two in step.
    expect(check('someone@example')).not.toBeNull();
    expect(check('someone@example.com')).toBeNull();
  });
});

describe('fieldsMatch', () => {
  const group = (first: string, second: string) =>
    new FormGroup({ a: new FormControl(first), b: new FormControl(second) }, { validators: fieldsMatch('a', 'b') });

  it('is fine when the two fields are the same', () => {
    expect(group('secret', 'secret').valid).toBe(true);
  });

  it('puts a "mismatch" error on the form when they differ', () => {
    expect(group('secret', 'secreT').hasError('mismatch')).toBe(true);
  });

  it('notices when one of them is edited afterwards', () => {
    const form = group('secret', 'secret');

    form.controls.b.setValue('other');
    expect(form.hasError('mismatch')).toBe(true);

    form.controls.b.setValue('secret');
    expect(form.hasError('mismatch')).toBe(false);
  });
});

describe('mismatchErrorMatcher', () => {
  const second = (first: string, repeat: string) => {
    const form = new FormGroup({ a: new FormControl(first), b: new FormControl(repeat) }, { validators: fieldsMatch('a', 'b') });
    return form.controls.b;
  };

  it('counts the form\'s mismatch as an error on the second box, once it has been touched', () => {
    const box = second('one', 'two');
    box.markAsTouched();

    expect(mismatchErrorMatcher.isErrorState(box, null)).toBe(true);
  });

  it('stays quiet until the person has been to the second box', () => {
    expect(mismatchErrorMatcher.isErrorState(second('one', 'two'), null)).toBe(false);
  });

  it('shows no error when the two match', () => {
    const box = second('same', 'same');
    box.markAsTouched();

    expect(mismatchErrorMatcher.isErrorState(box, null)).toBe(false);
  });
});
