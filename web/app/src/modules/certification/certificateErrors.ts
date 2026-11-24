import { ApiError } from '@/api/baseApi';
import type { MessageKey } from '@/i18n';
import { type ErrorMessage, errorMessage } from '@/i18n/errorText';

/** The page's own words for opening a PDF, where they depend on the action (#147): each names the course. */
const documentOverrides: Record<string, MessageKey> = {
  'certification.document-not-ready': 'certification.mine.documentNotReady',
};

/** Whether the refusal is one of the module's own, which the page reads by its code. */
const isModuleRefusal = (error: unknown) =>
  error instanceof ApiError && error.code !== undefined && error.code.startsWith('certification.');

/**
 * What a failed read of the certificates says, by the refusal's code (src/i18n/errorText.ts): the module's own refusals
 * by the catalog's errors.<code>, the host's words only for a code the catalog does not know; anything else, the
 * network included, that they could not be loaded.
 */
export function certificatesFailure(error: unknown): ErrorMessage {
  const fallback: MessageKey = 'certification.mine.loadFailed';
  return isModuleRefusal(error) ? errorMessage(error, fallback) : { key: fallback, values: {} };
}

/**
 * What a PDF the host does not sign a URL for says, by the same rules, naming the course: a document not stored yet
 * that it is still being prepared, in the page's own words; anything else that the PDF could not be opened.
 */
export function documentFailure(error: unknown, course: string): ErrorMessage {
  const fallback: MessageKey = 'certification.mine.documentFailed';
  return isModuleRefusal(error)
    ? errorMessage(error, fallback, { values: { course }, overrides: documentOverrides })
    : { key: fallback, values: { course } };
}

/** Whether reading the certificates again can help: for any failure but a token that names no person. */
export function certificatesRetryable(error: unknown): boolean {
  return !(error instanceof ApiError && error.code === 'certification.identity-incomplete');
}
