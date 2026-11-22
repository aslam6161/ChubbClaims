import { HttpClient, HttpEvent } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { firstValueFrom, Observable } from 'rxjs';
import type { ClaimDocument } from '../_models/api-types';

/**
 * Photos and documents on a claim. Files have no public URL: they are read through the API with
 * the user's token, so an image is fetched as a Blob and shown from a local object URL.
 */
@Service()
export class ClaimDocuments {
  private readonly http = inject(HttpClient);

  /** Upload events, so the screen can show progress. The last one carries the new document. */
  upload(claimId: string, file: File): Observable<HttpEvent<ClaimDocument>> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<ClaimDocument>(`/api/claims/${claimId}/documents`, form, {
      reportProgress: true,
      observe: 'events',
    });
  }

  content(claimId: string, documentId: string): Promise<Blob> {
    return firstValueFrom(
      this.http.get(`/api/claims/${claimId}/documents/${documentId}/content`, {
        responseType: 'blob',
      }),
    );
  }
}
