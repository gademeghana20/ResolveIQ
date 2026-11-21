import { AttachmentSettings } from '../incident.models';

/** ".PNG" for "Screenshot.PNG"; an empty text when there is no extension. */
export function fileExtension(fileName: string): string {
  const dot = fileName.lastIndexOf('.');
  return dot < 0 ? '' : fileName.slice(dot).toLowerCase();
}

/** "12 B", "340 KB", "1.5 MB". */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${Math.round(bytes / 1024)} KB`;
  }
  const megabytes = Math.round((bytes / (1024 * 1024)) * 10) / 10;
  return `${megabytes} MB`;
}

/**
 * Why a chosen file cannot be attached, or null if it looks fine. This only saves a round trip: the server checks the
 * type, size and real contents again, and its answer is the one that counts.
 */
export function fileProblem(file: File, settings: AttachmentSettings): string | null {
  if (!settings.allowedExtensions.includes(fileExtension(file.name))) {
    return `${file.name}: that type of file is not allowed. You can attach ${settings.allowedExtensions.join(', ')}.`;
  }
  if (file.size === 0) {
    return `${file.name}: the file is empty.`;
  }
  if (file.size > settings.maxFileBytes) {
    return `${file.name}: that file is too big. The limit is ${formatBytes(settings.maxFileBytes)}.`;
  }
  return null;
}
