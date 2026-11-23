import { accessToken } from '@/auth/accessToken';

/** A response outside 2xx; the generated hooks report it as their error. */
export class ApiError extends Error {
  readonly status: number;
  /**
   * A module's own refusal, as the host sent it: the problem details' detail, else its title, below 500 and with a
   * module's code (`<module>.<code>`, not invalid-argument). Absent otherwise: the framework's titles (Bad Request,
   * Forbidden, already-exists' Conflict) and a failed guard's detail are English and not for the user.
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
  const body = await response.text();
  if (!response.ok) {
    throw new ApiError(response.status, body);
  }

  return (body === '' ? undefined : JSON.parse(body)) as T;
}
