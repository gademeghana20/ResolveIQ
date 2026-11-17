import { FormControl, FormGroup } from '@angular/forms';

import { fieldsMatch, mismatchErrorMatcher, notBlank } from './validators';

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
