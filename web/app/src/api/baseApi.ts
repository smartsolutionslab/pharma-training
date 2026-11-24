import { accessToken } from '@/auth/accessToken';

/** A response outside 2xx; the generated hooks report it as their error. */
export class ApiError extends Error {
  readonly status: number;
  /**
   * The host's English fallback message, shown only for a module code the catalog lacks (#147, ADR 0007), whatever the
   * UI's language; only src/i18n/errorText.ts reads it. It is the problem details' detail, else its title, below 500
   * and with a module's code (`<module>.<code>`, not invalid-argument). Absent otherwise: the framework's titles (Bad
   * Request, Forbidden, already-exists' Conflict) and a failed guard's detail are no words for the user.
   */
  readonly serverMessage?: string;
  /**
   * A module's own code (`<module>.<code>`), on the same terms as serverMessage: below 500 only, and absent for the
   * framework's codes (invalid-argument, already-exists). A page tells refusals of one status apart by it.
   */
  readonly code?: string;
  /**
   * The problem details' code at any status, the framework's included (model-not-configured, already-exists). Never
   * words for the user: a page tells the host's own refusals above 500 from an edge's by it. Absent without one.
   */
  readonly failureCode?: string;

  constructor(status: number, body: string) {
    super(`The API answered ${status}: ${body}`);
    this.name = 'ApiError';
    this.status = status;
    const refusal = status < 500 ? moduleRefusalOf(body) : undefined;
    this.serverMessage = refusal === undefined ? undefined : serverMessageOf(refusal);
    this.code = refusal?.code;
    this.failureCode = failureCodeOf(body);
  }
}

function failureCodeOf(body: string): string | undefined {
  let problem: unknown;
  try {
    problem = JSON.parse(body);
  } catch {
    return undefined;
  }
  if (typeof problem !== 'object' || problem === null || Array.isArray(problem)) {
    return undefined;
  }
  const { code } = problem as Record<string, unknown>;
  return typeof code === 'string' && code.trim() !== '' ? code : undefined;
}

/** A refusal of a module's own: problem details with a module's code (`<module>.<code>`). */
interface ModuleRefusal {
  code: string;
  detail: unknown;
  title: unknown;
}

function moduleRefusalOf(body: string): ModuleRefusal | undefined {
  let problem: unknown;
  try {
    problem = JSON.parse(body);
  } catch {
    return undefined;
  }
  if (typeof problem !== 'object' || problem === null || Array.isArray(problem)) {
    return undefined;
  }
  const { code, detail, title } = problem as Record<string, unknown>;
  return isModuleCode(code) ? { code, detail, title } : undefined;
}

function serverMessageOf({ detail, title }: ModuleRefusal): string | undefined {
  for (const candidate of [detail, title]) {
    if (typeof candidate === 'string' && candidate.trim() !== '') {
      return candidate;
    }
  }
  return undefined;
}

/** A code a module's ApiError carries, <module>.<code>; invalid-argument and already-exists are the framework's. */
function isModuleCode(code: unknown): code is string {
  return typeof code === 'string' && code.trim() !== '' && code !== 'invalid-argument' && code.includes('.');
}

/** Every generated call (src/api/generated.ts) goes through here; /api reaches the host. */
export async function apiFetch<T>(url: string, init: RequestInit): Promise<T> {
  const headers = new Headers(init.headers);
  const token = accessToken();
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }
  const response = await fetch(`/api${url}`, { ...init, headers });
  if (response.ok && isBinary(response.headers.get('Content-Type'))) {
    return (await binaryOf(response)) as T;
  }
  const body = await response.text();
  if (!response.ok) {
    throw new ApiError(response.status, body);
  }

  return (body === '' ? undefined : JSON.parse(body)) as T;
}

/** An answer neither JSON, text nor without a type: a file the host streams (ADR 0032's certificate export). */
function isBinary(contentType: string | null): boolean {
  if (contentType === null || contentType.trim() === '') {
    return false;
  }
  const mediaType = (contentType.split(';')[0] ?? '').trim().toLowerCase();
  return mediaType !== 'application/json' && !mediaType.endsWith('+json') && !mediaType.startsWith('text/');
}

/** The response's own Blob, wrapped in a File only to carry the name Content-Disposition gives; never a copy. */
async function binaryOf(response: Response): Promise<Blob> {
  const blob = await response.blob();
  const name = fileNameOf(response.headers.get('Content-Disposition'));
  if (name === undefined) {
    return blob;
  }
  const type = response.headers.get('Content-Type') ?? blob.type;
  return new File([blob], name, { type });
}

/** The file a Content-Disposition names: filename* (RFC 5987) first, else filename. */
function fileNameOf(disposition: string | null): string | undefined {
  if (disposition === null) {
    return undefined;
  }
  const parameters = parametersOf(disposition);
  const extended = parameters.get('filename*');
  if (extended !== undefined) {
    const encoded = /^[^']*'[^']*'(.+)$/.exec(extended)?.[1];
    if (encoded !== undefined) {
      try {
        const name = decodeURIComponent(encoded.trim());
        if (name !== '') {
          return name;
        }
      } catch {
        // A malformed filename*: fall back to filename.
      }
    }
  }
  const name = parameters.get('filename')?.trim();
  return name === undefined || name === '' ? undefined : name;
}

/**
 * A header's parameters after its first value, by lower-cased name: each name stands at the start of a parameter (after
 * `;`), and a quoted value is read to its closing quote, its quoted-pairs (`\"`) unescaped (RFC 9110 5.6.4, RFC 6266).
 */
function parametersOf(header: string): Map<string, string> {
  const parameters = new Map<string, string>();
  let index = header.indexOf(';');
  while (index !== -1 && index < header.length) {
    index += 1;
    const equals = header.indexOf('=', index);
    const semicolon = header.indexOf(';', index);
    if (equals === -1 || (semicolon !== -1 && semicolon < equals)) {
      index = semicolon;
      continue;
    }
    const name = header.slice(index, equals).trim().toLowerCase();
    let position = equals + 1;
    while (header.charAt(position) === ' ' || header.charAt(position) === '\t') {
      position += 1;
    }
    let value = '';
    if (header.charAt(position) === '"') {
      position += 1;
      while (position < header.length && header.charAt(position) !== '"') {
        if (header.charAt(position) === '\\' && position + 1 < header.length) {
          position += 1;
        }
        value += header.charAt(position);
        position += 1;
      }
      index = header.indexOf(';', position);
    } else {
      const end = header.indexOf(';', position);
      value = header.slice(position, end === -1 ? undefined : end).trim();
      index = end;
    }
    if (name !== '' && !parameters.has(name)) {
      parameters.set(name, value);
    }
  }
  return parameters;
}
