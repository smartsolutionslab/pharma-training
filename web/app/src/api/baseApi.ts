import { accessToken } from '@/auth/accessToken';

/** A response outside 2xx; the generated hooks report it as their error. */
export class ApiError extends Error {
  readonly status: number;
  /** The problem details' detail, else its title, as the host sent them; absent when the body is no problem. */
  readonly serverMessage?: string;

  constructor(status: number, body: string) {
    super(`The API answered ${status}: ${body}`);
    this.name = 'ApiError';
    this.status = status;
    this.serverMessage = serverMessageOf(body);
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
  const { detail, title } = problem as Record<string, unknown>;
  for (const candidate of [detail, title]) {
    if (typeof candidate === 'string' && candidate.trim() !== '') {
      return candidate;
    }
  }
  return undefined;
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
