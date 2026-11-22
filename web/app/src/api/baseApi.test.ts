import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { ApiError, apiFetch } from './baseApi';

// The host, as the generated client reaches it through apiFetch: it answers what the test says.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();

beforeEach(() => {
  vi.stubGlobal('fetch', host);
});
afterEach(() => {
  host.mockReset();
  vi.unstubAllGlobals();
});

const problem = (status: number, body: Record<string, unknown>) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } });

/** What apiFetch rejects with when the host answers the response. */
async function refusalOf(response: Response): Promise<ApiError> {
  host.mockResolvedValue(response);
  const error: unknown = await apiFetch('/organisation/pharmacies', { method: 'POST' }).catch(
    (caught: unknown) => caught,
  );
  expect(error).toBeInstanceOf(ApiError);
  return error as ApiError;
}

// The server's own words, shown as it sent them until #147 translates its codes.
test("a refusal with problem details carries the problem's detail as the server's message", async () => {
  const error = await refusalOf(
    problem(400, {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
      title: 'Bad Request',
      status: 400,
      detail: 'A pharmacy name is at most 120 characters.',
      code: 'organisation.invalid-pharmacy-name',
    }),
  );

  expect(error.status).toBe(400);
  expect(error.serverMessage).toBe('A pharmacy name is at most 120 characters.');
});

test("a refusal with problem details but no detail carries the problem's title", async () => {
  const error = await refusalOf(problem(403, { title: 'Forbidden', status: 403 }));

  expect(error.status).toBe(403);
  expect(error.serverMessage).toBe('Forbidden');
});

test.each([
  ['an empty body', () => new Response('', { status: 500 })],
  ['a plain-text body', () => new Response('Bad Gateway', { status: 502, headers: { 'Content-Type': 'text/plain' } })],
  ['a JSON body without a title or detail', () => Response.json({ errors: ['x'] }, { status: 400 })],
  ['a JSON body that is not an object', () => Response.json('refused', { status: 400 })],
])('a refusal with %s carries no server message', async (_body, response) => {
  const error = await refusalOf(response());

  expect(error.serverMessage).toBeUndefined();
});

test('a blank detail is no message: the title is carried instead', async () => {
  const error = await refusalOf(problem(400, { title: 'Bad Request', detail: '' }));

  expect(error.serverMessage).toBe('Bad Request');
});
