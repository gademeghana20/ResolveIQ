import { TestBed } from '@angular/core/testing';

import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  const root = document.documentElement;

  function create(): ThemeService {
    TestBed.configureTestingModule({});
    return TestBed.inject(ThemeService);
  }

  beforeEach(() => {
    localStorage.clear();
    root.removeAttribute('data-theme');
  });

  afterEach(() => {
    vi.restoreAllMocks();
    root.removeAttribute('data-theme');
  });

  it('follows the computer by default: no saved choice, no data-theme', () => {
    const theme = create();

    expect(theme.preference()).toBe('system');
    expect(root.hasAttribute('data-theme')).toBe(false);
  });

  it.each(['light', 'dark'] as const)('restores a saved "%s" choice and applies it at once', (saved) => {
    localStorage.setItem('resolveiq.theme', saved);

    const theme = create();

    expect(theme.preference()).toBe(saved);
    expect(root.getAttribute('data-theme')).toBe(saved);
  });

  it('ignores a saved value it does not recognise', () => {
    localStorage.setItem('resolveiq.theme', 'neon-pink');

    const theme = create();

    expect(theme.preference()).toBe('system');
    expect(root.hasAttribute('data-theme')).toBe(false);
  });

  it('applies and remembers a new choice', () => {
    const theme = create();

    theme.set('dark');

    expect(theme.preference()).toBe('dark');
    expect(root.getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem('resolveiq.theme')).toBe('dark');
  });

  it('can switch from one choice to another', () => {
    const theme = create();

    theme.set('dark');
    theme.set('light');

    expect(root.getAttribute('data-theme')).toBe('light');
    expect(localStorage.getItem('resolveiq.theme')).toBe('light');
  });

  it('goes back to following the computer, and forgets the saved choice', () => {
    localStorage.setItem('resolveiq.theme', 'dark');
    const theme = create();

    theme.set('system');

    expect(theme.preference()).toBe('system');
    expect(root.hasAttribute('data-theme')).toBe(false);
    expect(localStorage.getItem('resolveiq.theme')).toBeNull();
  });

  describe('when the browser blocks storage', () => {
    it('still starts, following the computer', () => {
      vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
        throw new Error('blocked');
      });

      expect(create().preference()).toBe('system');
    });

    it('still applies a choice for this visit, even though it cannot be remembered', () => {
      const theme = create();
      vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
        throw new Error('blocked');
      });

      theme.set('dark');

      expect(theme.preference()).toBe('dark');
      expect(root.getAttribute('data-theme')).toBe('dark');
    });
  });
});
