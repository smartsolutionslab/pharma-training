import { ApiError } from '@/api/baseApi';
import type { MessageKey } from '@/i18n';

/** What the page says when something fails: a text of its own, or the host's own words. */
export type CertificateFailure = { key: MessageKey } | { message: string };

/** The module's refusals of the list the page has words for. */
const listRefusals: Partial<Record<string, MessageKey>> = {
  'certification.identity-incomplete': 'certification.mine.identityIncomplete',
};

/** The module's refusals of a PDF the page has words for; each names the course. */
const documentRefusals: Partial<Record<string, MessageKey>> = {
  'certification.document-not-ready': 'certification.mine.documentNotReady',
  'certification.identity-incomplete': 'certification.mine.identityIncomplete',
};

function failureOf(error: unknown, refusals: Partial<Record<string, MessageKey>>, otherwise: MessageKey) {
  if (error instanceof ApiError && error.code !== undefined) {
    const key = refusals[error.code];
    if (key !== undefined) {
      return { key };
    }
    if (error.code.startsWith('certification.') && error.serverMessage !== undefined) {
      return { message: error.serverMessage };
    }
  }
  return { key: otherwise };
}

/**
 * What a failed read of the certificates says: a refusal the page has words for gives its key; another of the module's
 * own refusals the host's words (ADR 0007); anything else, the network included, that they could not be loaded.
 */
export function certificatesFailure(error: unknown): CertificateFailure {
  return failureOf(error, listRefusals, 'certification.mine.loadFailed');
}

/**
 * What a PDF the host does not sign a URL for says, by the same rules: a document not stored yet that it is still being
 * prepared; anything else, the network included, that the PDF could not be opened.
 */
export function documentFailure(error: unknown): CertificateFailure {
  return failureOf(error, documentRefusals, 'certification.mine.documentFailed');
}
