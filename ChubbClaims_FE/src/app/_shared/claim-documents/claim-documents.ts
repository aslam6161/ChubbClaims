import { DatePipe } from '@angular/common';
import { HttpEventType } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { lastValueFrom, tap } from 'rxjs';
import {
  ACCEPT_ATTRIBUTE,
  checkFiles,
  formatSize,
  isImage,
  type RejectedFile,
  uploaderLabel,
} from '../../_helpers/documents';
import type { ClaimDocument, UserRole } from '../../_models/api-types';
import { AppError } from '../../_models/app-error';
import { ClaimDocuments } from '../../_services/claim-documents';
import { Toast } from '../../_services/toast';

interface UploadProgress {
  readonly name: string;
  /** 0–100. */
  readonly percent: number;
}

/**
 * Photos and documents on a claim: thumbnails, open in a viewer, and (while the claim is open)
 * add files by picking or dropping them. Used by the claimant's claim page and the staff workbench.
 * Files are fetched with the user's token and shown from object URLs, released when the panel goes.
 */
@Component({
  selector: 'app-claim-documents',
  imports: [DatePipe, MatButtonModule, MatIconModule, MatProgressBarModule],
  templateUrl: './claim-documents.html',
  styleUrl: './claim-documents.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClaimDocumentsPanel {
  private readonly service = inject(ClaimDocuments);
  private readonly toast = inject(Toast);
  private readonly dialog = inject(MatDialog);

  readonly claimId = input.required<string>();
  readonly documents = input.required<readonly ClaimDocument[]>();
  /** The claim is open and the viewer may attach (its claimant, or staff in its market). */
  readonly canAttach = input(false);
  readonly viewerIsStaff = input(false);
  /** After one or more files were attached, so the page reloads the claim. */
  readonly attached = output<void>();

  protected readonly accept = ACCEPT_ATTRIBUTE;
  protected readonly isImage = isImage;
  protected readonly size = formatSize;

  /** Document id → object URL of its image, once loaded. */
  protected readonly previews = signal<ReadonlyMap<string, string>>(new Map());
  protected readonly failedPreviews = signal<ReadonlySet<string>>(new Set());
  protected readonly uploads = signal<readonly UploadProgress[]>([]);
  protected readonly problems = signal<readonly RejectedFile[]>([]);
  protected readonly dragging = signal(false);
  protected readonly uploading = computed(() => this.uploads().length > 0);

  private readonly requested = new Set<string>();

  constructor() {
    effect(() => {
      for (const d of this.documents())
        if (isImage(d.contentType) && !this.requested.has(d.id)) void this.loadPreview(d);
    });
    inject(DestroyRef).onDestroy(() => {
      for (const url of this.previews().values()) URL.revokeObjectURL(url);
    });
  }

  protected uploader(role: UserRole): string {
    return uploaderLabel(role, this.viewerIsStaff());
  }

  protected onPicked(input: HTMLInputElement): void {
    void this.add(Array.from(input.files ?? []));
    input.value = ''; // so picking the same file again still fires (change)
  }

  protected onDragOver(event: DragEvent): void {
    if (!this.canAttach()) return;
    event.preventDefault(); // allow the drop
    this.dragging.set(true);
  }

  protected onDrop(event: DragEvent): void {
    if (!this.canAttach()) return;
    event.preventDefault();
    this.dragging.set(false);
    void this.add(Array.from(event.dataTransfer?.files ?? []));
  }

  /** Checks the files, then uploads the good ones one at a time with progress. */
  async add(files: readonly File[]): Promise<void> {
    if (files.length === 0 || this.uploading()) return;
    const { accepted, rejected } = checkFiles(files, this.documents().length);
    this.problems.set(rejected);
    if (accepted.length === 0) return;

    this.uploads.set(accepted.map((f) => ({ name: f.name, percent: 0 })));
    const failed: RejectedFile[] = [];
    let added = 0;
    for (const [index, file] of accepted.entries()) {
      try {
        await lastValueFrom(
          this.service.upload(this.claimId(), file).pipe(
            tap((event) => {
              if (event.type === HttpEventType.UploadProgress && event.total)
                this.setPercent(index, Math.round((100 * event.loaded) / event.total));
            }),
          ),
        );
        this.setPercent(index, 100);
        added++;
      } catch (e) {
        failed.push({ name: file.name, reason: AppError.from(e).userMessage });
      }
    }
    this.uploads.set([]);
    this.problems.update((p) => [...p, ...failed]);
    if (added > 0) {
      this.toast.success(
        added === 1
          ? `${accepted.length === 1 ? accepted[0].name : '1 file'} added.`
          : `${added} files added.`,
      );
      this.attached.emit();
    }
  }

  protected async open(document: ClaimDocument): Promise<void> {
    const preview = this.previews().get(document.id);
    if (preview) {
      this.dialog.open(DocumentViewer, {
        data: { url: preview, fileName: document.fileName } satisfies ViewerData,
        maxWidth: '95vw',
        autoFocus: 'dialog',
      });
      return;
    }
    // A PDF opens in a new tab. The tab is opened now, while the click still counts as the
    // user's, and pointed at the file once it has downloaded (a later window.open is blocked).
    const tab = window.open('', '_blank');
    try {
      const url = URL.createObjectURL(await this.service.content(this.claimId(), document.id));
      if (tab) tab.location.href = url;
      else this.download(url, document.fileName);
      setTimeout(() => URL.revokeObjectURL(url), 60_000);
    } catch (e) {
      tab?.close();
      this.toast.error(AppError.from(e).userMessage);
    }
  }

  private download(url: string, fileName: string): void {
    const link = Object.assign(window.document.createElement('a'), {
      href: url,
      download: fileName,
    });
    link.click();
  }

  private async loadPreview(document: ClaimDocument): Promise<void> {
    this.requested.add(document.id);
    try {
      const url = URL.createObjectURL(await this.service.content(this.claimId(), document.id));
      this.previews.update((m) => new Map(m).set(document.id, url));
    } catch {
      this.failedPreviews.update((s) => new Set(s).add(document.id));
    }
  }

  private setPercent(index: number, percent: number): void {
    this.uploads.update((list) => list.map((u, i) => (i === index ? { ...u, percent } : u)));
  }
}

interface ViewerData {
  readonly url: string;
  readonly fileName: string;
}

/** A photo at full size. */
@Component({
  selector: 'app-document-viewer',
  imports: [MatButtonModule, MatDialogModule, MatIconModule],
  template: `
    <h2 mat-dialog-title>{{ data.fileName }}</h2>
    <mat-dialog-content>
      <img [src]="data.url" [alt]="data.fileName" />
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <a mat-button [href]="data.url" [attr.download]="data.fileName"
        ><mat-icon>download</mat-icon> Download</a
      >
      <button mat-flat-button type="button" mat-dialog-close cdkFocusInitial>Close</button>
    </mat-dialog-actions>
  `,
  styles: `
    img {
      display: block;
      max-width: 100%;
      max-height: 70vh;
      margin: 0 auto;
      border-radius: 6px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DocumentViewer {
  protected readonly data = inject<ViewerData>(MAT_DIALOG_DATA);
}
