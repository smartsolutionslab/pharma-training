import { onBeforeUnmount, readonly, ref } from 'vue';
import { postEditorialCourseVersionsIdUploads } from '@/api/generated';
import { ApiError } from '@/api/baseApi';
import { t } from '@/i18n';
import { notify } from '@/lib/notify';
import { type FileKind, type FileProblem, fileLimits, fileProblem } from './chapterFile';
import { UploadCancelled, UploadFailed, uploadToStorage } from './uploadToStorage';

/** Why an upload failed: the URL expired (403), storage was not reached, storage refused, or the file was refused. */
export type UploadFailure = 'expired' | 'network' | 'status' | FileProblem;

export type ChapterUploadState =
  | { status: 'idle' }
  | { status: 'requesting' }
  | { status: 'uploading'; loaded: number; total: number }
  | { status: 'attaching'; blobName: string }
  | { status: 'done' }
  | { status: 'cancelled' }
  | { status: 'failed'; reason: UploadFailure };

function failureOf(error: unknown): UploadFailure {
  if (error instanceof UploadFailed) {
    if (error.status === 403) {
      return 'expired';
    }
    return error.status === 0 ? 'network' : 'status';
  }
  return error instanceof ApiError ? 'status' : 'network';
}

/**
 * A PDF's or a video's way into storage (spec 2.6): an upload URL from the host, the file checked against what it
 * issued, then put there with progress. start answers the blob's name for the chapter, or undefined when the file was
 * refused, the upload failed or was cancelled; attached() marks the chapter saved. Leaving aborts the upload.
 */
export function useChapterUpload() {
  const state = ref<ChapterUploadState>({ status: 'idle' });
  let current: AbortController | undefined;

  const cancel = () => {
    current?.abort();
  };
  onBeforeUnmount(cancel);

  const start = async (versionId: string, kind: FileKind, file: File): Promise<string | undefined> => {
    cancel();
    current = undefined;
    const refused = fileProblem(file, fileLimits[kind]);
    if (refused !== undefined) {
      state.value = { status: 'failed', reason: refused };
      return undefined;
    }
    const controller = new AbortController();
    current = controller;
    const { signal } = controller;
    // A start superseded by a later one leaves the state to that one.
    const become = (next: ChapterUploadState) => {
      if (current === controller) {
        state.value = next;
      }
    };
    try {
      become({ status: 'requesting' });
      const issued = await postEditorialCourseVersionsIdUploads(versionId, { kind }, { signal });
      if (signal.aborted) {
        throw new UploadCancelled();
      }
      const issuedRefusal = fileProblem(file, {
        contentType: issued.contentType,
        maxBytes: Number(issued.maxBytes),
      });
      if (issuedRefusal !== undefined) {
        become({ status: 'failed', reason: issuedRefusal });
        return undefined;
      }
      become({ status: 'uploading', loaded: 0, total: file.size });
      await uploadToStorage({
        url: issued.uploadUrl,
        file,
        contentType: issued.contentType,
        signal,
        onProgress: ({ loaded, total }) => {
          become({ status: 'uploading', loaded, total });
        },
      });
      become({ status: 'attaching', blobName: issued.blobName });
      return issued.blobName;
    } catch (error) {
      if (signal.aborted || error instanceof UploadCancelled) {
        become({ status: 'cancelled' });
        return undefined;
      }
      become({ status: 'failed', reason: failureOf(error) });
      notify.failure(t('editorial.upload.failed'));
      return undefined;
    } finally {
      if (current === controller) {
        current = undefined;
      }
    }
  };

  const attached = () => {
    state.value = { status: 'done' };
  };

  return { state: readonly(state), start, attached, cancel };
}
