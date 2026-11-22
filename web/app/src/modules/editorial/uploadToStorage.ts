// A chapter's file goes straight to storage (spec 2.6, ADR 0002): one PUT of the File itself to the upload URL the host
// issued. XMLHttpRequest, since fetch reports no upload progress; send(file) streams the File from disk, so nothing here
// reads it into memory.

export interface UploadProgress {
  loaded: number;
  total: number;
}

export interface UploadToStorage {
  url: string;
  file: File;
  contentType: string;
  onProgress: (progress: UploadProgress) => void;
  signal: AbortSignal;
}

/** Storage refused the file or could not be reached: status is the HTTP status, 0 for a network error or timeout. */
export class UploadFailed extends Error {
  readonly status: number;
  readonly timedOut: boolean;

  constructor(status: number, timedOut = false) {
    super(status === 0 ? 'The upload did not reach storage.' : `Storage answered ${status}.`);
    this.name = 'UploadFailed';
    this.status = status;
    this.timedOut = timedOut;
  }
}

/** The upload was cancelled; it is no failure. */
export class UploadCancelled extends Error {
  constructor() {
    super('The upload was cancelled.');
    this.name = 'UploadCancelled';
  }
}

export function uploadToStorage({ url, file, contentType, onProgress, signal }: UploadToStorage): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    if (signal.aborted) {
      reject(new UploadCancelled());
      return;
    }
    const xhr = new XMLHttpRequest();
    const onAbort = () => xhr.abort();
    const settle = (outcome: () => void) => {
      signal.removeEventListener('abort', onAbort);
      outcome();
    };
    xhr.open('PUT', url);
    xhr.withCredentials = false;
    xhr.setRequestHeader('x-ms-blob-type', 'BlockBlob');
    xhr.setRequestHeader('Content-Type', contentType);
    xhr.upload.onprogress = (event) => onProgress({ loaded: event.loaded, total: event.total });
    xhr.onload = () =>
      settle(() => {
        if (xhr.status === 201) {
          resolve();
        } else {
          reject(new UploadFailed(xhr.status));
        }
      });
    xhr.onerror = () => settle(() => reject(new UploadFailed(0)));
    xhr.ontimeout = () => settle(() => reject(new UploadFailed(0, true)));
    xhr.onabort = () => settle(() => reject(new UploadCancelled()));
    signal.addEventListener('abort', onAbort, { once: true });
    xhr.send(file);
  });
}
