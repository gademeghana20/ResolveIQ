import { formatDay, formatDuration, formatHours, formatPercent, niceAxis } from './format';

describe('formatDuration', () => {
  it.each([
    [0, '0 min'],
    [45, '45 min'],
    [60, '1 h'],
    [200, '3 h 20 min'],
    [1440, '1 d'],
    [3000, '2 d 2 h'],
  ])('%d minutes reads as "%s"', (minutes, expected) => {
    expect(formatDuration(minutes)).toBe(expected);
  });

  it('never shows a negative or fractional time', () => {
    expect(formatDuration(-5)).toBe('0 min');
    expect(formatDuration(59.6)).toBe('1 h');
  });
});

describe('formatHours', () => {
  it('turns hours into a readable time', () => {
    expect(formatHours(4.5)).toBe('4 h 30 min');
    expect(formatHours(0.5)).toBe('30 min');
    expect(formatHours(48)).toBe('2 d');
  });
});

describe('formatPercent', () => {
  it('rounds to a whole percent', () => {
    expect(formatPercent(83.333)).toBe('83%');
    expect(formatPercent(99.6)).toBe('100%');
    expect(formatPercent(0)).toBe('0%');
  });
});

describe('formatDay', () => {
  it('writes a plain date without letting the time zone shift it by a day', () => {
    const text = formatDay('2026-11-17');

    expect(text).toContain('17');
    expect(text).not.toContain('16');
    expect(text).not.toContain('18');
  });
});

describe('niceAxis', () => {
  it('rounds the top up to a tidy number with whole-number steps', () => {
    expect(niceAxis(7)).toEqual({ top: 8, step: 2 });
    expect(niceAxis(10)).toEqual({ top: 10, step: 5 });
    expect(niceAxis(100)).toEqual({ top: 100, step: 50 });
  });

  it('always covers the largest value', () => {
    for (const largest of [1, 2, 3, 5, 9, 13, 47, 99, 101, 999]) {
      expect(niceAxis(largest).top).toBeGreaterThanOrEqual(largest);
    }
  });

  it('never uses a step smaller than 1, because incidents are whole numbers', () => {
    expect(niceAxis(1).step).toBe(1);
    expect(niceAxis(2).step).toBeGreaterThanOrEqual(1);
  });

  it('gives an empty chart a sensible scale instead of dividing by zero', () => {
    expect(niceAxis(0)).toEqual({ top: 4, step: 1 });
  });
});
