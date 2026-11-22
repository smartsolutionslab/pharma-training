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

  constructor(status: number, body: string) {
    super(`The API answered ${status}: ${body}`);
    this.name = 'ApiError';
    this.status = status;
    this.serverMessage = status < 500 ? serverMessageOf(body) : undefined;
  }
}

function serverMessageOf(body: string): string | undefined {
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
  if (!isModuleCode(code)) {
    return undefined;
  }
  for (const candidate of [detail, title]) {
    if (typeof candidate === 'string' && candidate.trim() !== '') {
      return candidate;
    }
  }
  return undefined;
}

/** A code a module's ApiError carries, <module>.<code>; invalid-argument and already-exists are the framework's. */
function isModuleCode(code: unknown): boolean {
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
