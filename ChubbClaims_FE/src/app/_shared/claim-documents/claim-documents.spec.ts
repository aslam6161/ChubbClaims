import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { problemInterceptor } from '../../_interceptors/problem.interceptor';
import type { ClaimDocument } from '../../_models/api-types';
import { Toast } from '../../_services/toast';
import { ClaimDocumentsPanel } from './claim-documents';

const doc = (overrides: Partial<ClaimDocument>): ClaimDocument => ({
  id: 'd1',
  fileName: 'bumper.jpg',
  contentType: 'image/jpeg',
  sizeBytes: 2_400_000,
  uploadedByRole: 'Claimant',
  uploadedAt: '2026-11-22T08:00:00Z',
  ...overrides,
});
const file = (name: string, type: string): File => new File([new Uint8Array(100)], name, { type });
const tick = () => new Promise((resolve) => setTimeout(resolve));

describe('ClaimDocumentsPanel', () => {
  let backend: HttpTestingController;
  let toast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  async function render(documents: ClaimDocument[], canAttach = true, viewerIsStaff = false) {
    toast = { success: vi.fn(), error: vi.fn() };
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => 'blob:preview');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      imports: [ClaimDocumentsPanel],
      providers: [
        provideHttpClient(withInterceptors([problemInterceptor])),
        provideHttpClientTesting(),
        { provide: Toast, useValue: toast },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ClaimDocumentsPanel);
    fixture.componentRef.setInput('claimId', 'c1');
    fixture.componentRef.setInput('documents', documents);
    fixture.componentRef.setInput('canAttach', canAttach);
    fixture.componentRef.setInput('viewerIsStaff', viewerIsStaff);
    const attached = vi.fn();
    fixture.componentInstance.attached.subscribe(attached);
    fixture.detectChanges();
    return { fixture, page: fixture.nativeElement as HTMLElement, attached };
  }

  afterEach(() => vi.restoreAllMocks());

  it('shows photos as previews fetched with the user token, and PDFs as a file icon', async () => {
    const { fixture, page } = await render([
      doc({}),
      doc({
        id: 'd2',
        fileName: 'report.pdf',
        contentType: 'application/pdf',
        uploadedByRole: 'ClaimsOfficer',
      }),
    ]);

    // Only the photo is downloaded for a preview; the PDF waits until it is opened.
    backend.expectOne('/api/claims/c1/documents/d1/content').flush(new Blob(['jpeg']));
    backend.expectNone('/api/claims/c1/documents/d2/content');
    await tick();
    fixture.detectChanges();

    expect(page.querySelector<HTMLImageElement>('.tile img')!.src).toBe('blob:preview');
    expect(page.textContent).toContain('picture_as_pdf');
    const meta = [...page.querySelectorAll('.meta')].map((m) =>
      m.textContent!.replace(/\s+/g, ' ').trim(),
    );
    expect(meta).toEqual(['You · 22 Nov · 2.3 MB', 'Claims team · 22 Nov · 2.3 MB']);
  });

  it('a closed claim shows the files but no way to add more', async () => {
    const { page } = await render([], false);

    expect(page.textContent).toContain('No photos or documents yet.');
    expect(page.querySelector('input[type=file]')).toBeNull();
    expect(page.textContent).not.toContain('Add photos or files');
  });

  it('uploads the good files one by one, lists the refused ones, and asks the page to reload', async () => {
    const { fixture, page, attached } = await render([]);
    const panel = fixture.componentInstance;

    const done = panel.add([
      file('a.jpg', 'image/jpeg'),
      file('movie.mp4', 'video/mp4'),
      file('b.pdf', 'application/pdf'),
    ]);
    fixture.detectChanges();
    expect(page.textContent).toContain('movie.mp4: Only JPEG, PNG or WebP photos and PDF files.');

    const first = backend.expectOne('/api/claims/c1/documents');
    expect(first.request.method).toBe('POST');
    expect((first.request.body as FormData).get('file')).toBeInstanceOf(File);
    first.flush(doc({ id: 'n1', fileName: 'a.jpg' }));
    await tick();
    backend.expectOne('/api/claims/c1/documents').flush(doc({ id: 'n2', fileName: 'b.pdf' }));
    await done;

    expect(toast.success).toHaveBeenCalledWith('2 files added.');
    expect(attached).toHaveBeenCalledTimes(1);
  });

  it('shows the server reason when a file is refused there', async () => {
    const { fixture, page, attached } = await render([]);

    const done = fixture.componentInstance.add([file('renamed.jpg', 'image/jpeg')]);
    backend.expectOne('/api/claims/c1/documents').flush(
      {
        status: 422,
        title: 'Business rule violated',
        detail: 'Only JPEG, PNG or WebP photos and PDF documents can be attached.',
      },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await done;
    fixture.detectChanges();

    expect(page.querySelector('[role=alert]')!.textContent).toContain(
      'renamed.jpg: Only JPEG, PNG or WebP photos and PDF documents can be attached.',
    );
    expect(attached).not.toHaveBeenCalled();
    expect(toast.success).not.toHaveBeenCalled();
  });
});
