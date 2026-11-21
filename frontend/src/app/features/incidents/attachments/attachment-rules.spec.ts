import { ATTACHMENT_SETTINGS as SETTINGS } from '../../../testing/attachment-settings';
import { fileExtension, fileProblem, formatBytes } from './attachment-rules';

const file = (name: string, size: number) => new File([new Uint8Array(size)], name);

describe('attachment rules', () => {
  it('reads the extension in lower case', () => {
    expect(fileExtension('Screenshot.PNG')).toBe('.png');
    expect(fileExtension('archive.tar.gz')).toBe('.gz');
    expect(fileExtension('README')).toBe('');
  });

  it('writes sizes the way people read them', () => {
    expect(formatBytes(12)).toBe('12 B');
    expect(formatBytes(340 * 1024)).toBe('340 KB');
    expect(formatBytes(1.5 * 1024 * 1024)).toBe('1.5 MB');
    expect(formatBytes(5 * 1024 * 1024)).toBe('5 MB');
  });

  describe('fileProblem', () => {
    it('accepts a file of an allowed kind and size', () => {
      expect(fileProblem(file('shot.PNG', 1000), SETTINGS)).toBeNull();
      expect(fileProblem(file('notes.txt', 1), SETTINGS)).toBeNull();
    });

    it('refuses a kind that is not on the list, naming the file and the kinds that are', () => {
      const problem = fileProblem(file('program.exe', 1000), SETTINGS)!;

      expect(problem).toContain('program.exe');
      expect(problem).toContain('not allowed');
      expect(problem).toContain('.png');
    });

    it('refuses a name with no extension', () => {
      expect(fileProblem(file('README', 10), SETTINGS)).toContain('not allowed');
    });

    it('refuses an empty file', () => {
      expect(fileProblem(file('empty.txt', 0), SETTINGS)).toContain('empty');
    });

    it('refuses a file over the limit, saying what the limit is', () => {
      const problem = fileProblem(file('huge.png', SETTINGS.maxFileBytes + 1), SETTINGS)!;

      expect(problem).toContain('too big');
      expect(problem).toContain('5 MB');
    });

    it('accepts a file exactly at the limit', () => {
      expect(fileProblem(file('exact.png', SETTINGS.maxFileBytes), SETTINGS)).toBeNull();
    });
  });
});
