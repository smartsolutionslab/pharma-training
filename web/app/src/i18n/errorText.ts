import { ApiError } from '@/api/baseApi';
import { errorValues } from './errorValues';
import { i18n, type MessageKey, t } from './index';

// A refusal is shown by its code (#147, ADR 0007), in this order: the page's own text for the code (an override), the
// catalog's errors.<code>, the host's English fallback message only for a module's code the catalog does not know (a
// newer host than this SPA; warned once per code in development, and English in either language), and else the
// action's own failed text.

/** What to show for a refusal: a catalog text with its values, or, for an unknown module code, the host's words. */
export type ErrorMessage = { key: MessageKey; values: Record<string, unknown> } | { serverMessage: string };

export interface ErrorOptions {
  /** The caller's values, for its fallback text and its overrides. */
  values?: Record<string, unknown>;
  /** The page's own text for a code, where its words depend on the step. */
  overrides?: Partial<Record<string, MessageKey>>;
}

/** The codes warned about in this page load, each once. */
const warned = new Set<string>();

/** Forgets the codes warned about, so a test sees a code's warning however many tests showed it before. Tests only. */
export function resetWarnedCodes(): void {
  warned.clear();
}

const isMessageKey = (key: string): key is MessageKey => i18n.global.te(key, 'de-DE');

/** A module's code (<module>.<name>): the only kind whose host words may show. The host's own are no sentence. */
const isModuleCode = (code: string) => code.includes('.');

/** The message for a code, without an ApiError: an override, its catalog text, the host's words it is given, else the fallback. */
export function codeMessage(
  code: string | undefined,
  fallback: MessageKey,
  { values = {}, overrides = {}, serverMessage }: ErrorOptions & { serverMessage?: string } = {},
): ErrorMessage {
  if (code !== undefined) {
    const codeValues = errorValues[code] ?? {};
    const override = overrides[code];
    if (override !== undefined) {
      return { key: override, values: { ...codeValues, ...values } };
    }
    const key = `errors.${code}`;
    if (isMessageKey(key)) {
      return { key, values: { ...codeValues } };
    }
    if (isModuleCode(code) && serverMessage !== undefined && serverMessage.trim() !== '') {
      if (import.meta.env.DEV && !warned.has(code)) {
        warned.add(code);
        console.warn(`No text in the catalogs for the error code ${code}; showing the host's words.`);
      }
      return { serverMessage };
    }
  }
  return { key: fallback, values: { ...values } };
}

/** The message for an error a call rejected with: by the ApiError's failure code, else the fallback. */
export function errorMessage(error: unknown, fallback: MessageKey, options: ErrorOptions = {}): ErrorMessage {
  if (!(error instanceof ApiError)) {
    return codeMessage(undefined, fallback, options);
  }
  return codeMessage(error.failureCode, fallback, { ...options, serverMessage: error.serverMessage });
}

/** The message's text in the language shown now; a kept message read again follows a switch of the language. */
export function messageText(message: ErrorMessage): string {
  if ('serverMessage' in message) {
    return message.serverMessage;
  }
  const values = Object.fromEntries(
    Object.entries(message.values).map(([name, value]) => [
      name,
      typeof value === 'number' ? i18n.global.n(value, 'integer') : value,
    ]),
  );
  return t(message.key, values);
}

/** The text to show for an error a call rejected with, as a toast says it. */
export function errorText(error: unknown, fallback: MessageKey, options: ErrorOptions = {}): string {
  return messageText(errorMessage(error, fallback, options));
}
