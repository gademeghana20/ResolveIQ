import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, concatMap, from, map, of, shareReplay, toArray } from 'rxjs';

import { API_URL } from '../../../core/api-config';
import { Attachment, AttachmentSettings } from '../incident.models';

export interface UploadSummary {
  /** How many files were attached. */
  uploaded: number;
  /** One sentence for each file that was not, naming the file and saying why. */
  failures: string[];
}

/** Talks to the attachment endpoints of the API. */
@Injectable({ providedIn: 'root' })
export class AttachmentService {
  private readonly http = inject(HttpClient);

  private settings$: Observable<AttachmentSettings> | undefined;

  /** The limits and whether attaching is on. Asked for once, then remembered. */
  getSettings(): Observable<AttachmentSettings> {
    this.settings$ ??= this.http.get<AttachmentSettings>(`${API_URL}/attachments/settings`).pipe(
      // An answer is kept for the next screen; a failed question is asked again next time.
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.settings$;
  }

  list(incidentId: string): Observable<Attachment[]> {
    return this.http.get<Attachment[]>(`${API_URL}/incidents/${incidentId}/attachments`);
  }

  upload(incidentId: string, file: File, commentId?: string): Observable<Attachment> {
    const form = new FormData();
    form.append('file', file, file.name);
    if (commentId) {
      form.append('commentId', commentId);
    }
    return this.http.post<Attachment>(`${API_URL}/incidents/${incidentId}/attachments`, form);
  }

  /**
   * Attaches the files one after another (so a failure on one does not stop the others) and says how it went.
   * It never fails: problems come back in `failures`.
   */
  uploadAll(incidentId: string, files: File[], commentId?: string): Observable<UploadSummary> {
    return from(files).pipe(
      concatMap((file) =>
        this.upload(incidentId, file, commentId).pipe(
          map(() => null),
          catchError((error: unknown) => of(`${file.name}: ${uploadProblem(error)}`)),
        ),
      ),
      toArray(),
      map((results) => ({
        uploaded: results.filter((r) => r === null).length,
        failures: results.filter((r): r is string => r !== null),
      })),
    );
  }

  download(id: string): Observable<Blob> {
    return this.http.get(`${API_URL}/attachments/${id}/download`, { responseType: 'blob' });
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${API_URL}/attachments/${id}`);
  }
}

/** The reason an upload was refused, in words a person can act on. */
export function uploadProblem(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return 'the server could not be reached.';
    }
    if (error.status === 404) {
      return 'this incident is no longer available.';
    }
    if (error.status === 413) {
      return 'the file is too big.';
    }
    if (error.status === 429) {
      return 'too many requests. Please wait a minute and try again.';
    }
    // The API explains what was wrong, for example "That file is too big. The limit is 5 MB."
    const reason = error.error?.errors?.['file']?.[0] ?? error.error?.detail;
    if (typeof reason === 'string' && reason.length > 0) {
      return reason;
    }
  }
  return 'it could not be attached. Please try again.';
}
