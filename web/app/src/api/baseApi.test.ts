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

// The server's own words, shown as it sent them until #147 translates its codes. Only a module's ApiError speaks to
// the user: problem details below 500 whose code extension names it (src/BuildingBlocks/Http/Wiring/
// ResultHttpExtensions.cs writes the module's message as the title and its code as code). A framework's title ("Bad
// Request", "An error occurred while processing your request.") or a failed guard's detail (code invalid-argument) is
// English the user must not read: the catalog's text shows instead.
test("a module's refusal with a detail carries the problem's detail as the server's message", async () => {
  const error = await refusalOf(
    problem(400, {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
      title: 'Bad Request',
      status: 400,
      detail: 'Der Name einer Apotheke hat 1 bis 120 Zeichen und ist nicht leer.',
      code: 'organisation.invalid-pharmacy-name',
    }),
  );

  expect(error.status).toBe(400);
  expect(error.serverMessage).toBe('Der Name einer Apotheke hat 1 bis 120 Zeichen und ist nicht leer.');
});

test("a module's refusal as ToHttpResult writes it, the message as the title, carries the title", async () => {
  const error = await refusalOf(
    problem(403, {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.4',
      title: 'Das Konto liefert keine Person, keinen Namen oder keine gültige E-Mail-Adresse.',
      status: 403,
      code: 'organisation.identity-incomplete',
    }),
  );

  expect(error.status).toBe(403);
  expect(error.serverMessage).toBe('Das Konto liefert keine Person, keinen Namen oder keine gültige E-Mail-Adresse.');
});

test("a module's refusal with a blank detail carries the title instead", async () => {
  const error = await refusalOf(
    problem(409, {
      title: 'Die Apotheke wurde inzwischen geändert.',
      detail: '',
      code: 'organisation.pharmacy-changed',
    }),
  );

  expect(error.serverMessage).toBe('Die Apotheke wurde inzwischen geändert.');
});

test.each([
  ['an empty body', () => new Response('', { status: 500 })],
  ['a plain-text body', () => new Response('Bad Gateway', { status: 502, headers: { 'Content-Type': 'text/plain' } })],
  ['a JSON body without a title or detail', () => Response.json({ errors: ['x'] }, { status: 400 })],
  ['a JSON body that is not an object', () => Response.json('refused', { status: 400 })],
  // ASP.NET Core's exception handler: a bug, answered 500 with the framework's English title.
  [
    "a 500 with the framework's problem title",
    () =>
      problem(500, {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.6.1',
        title: 'An error occurred while processing your request.',
        status: 500,
      }),
  ],
  // A body the endpoint could not bind: the framework's title, and no code.
  [
    'a 400 binding failure without a code',
    () =>
      problem(400, {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'Bad Request',
        status: 400,
        detail: 'Failed to read parameter "RegisterPharmacyRequest request" from the request body as JSON.',
      }),
  ],
  // The authorization middleware's refusal: a title, no code.
  ['a 403 without a code', () => problem(403, { title: 'Forbidden', status: 403 })],
  // A failed guard (ErrorResponses.cs): the backstop's own title and the guard's English message.
  [
    'a 400 from a failed guard',
    () =>
      problem(400, {
        title: 'The request is invalid.',
        status: 400,
        detail: "The value of 'name' must not be null or white space.",
        code: 'invalid-argument',
      }),
  ],
  // A code on a 5xx is no module's ApiError the user may read.
  [
    'a 503 with a code',
    () =>
      problem(503, { title: 'Service Unavailable', status: 503, detail: 'No model.', code: 'model-not-configured' }),
  ],
  // A framework backstop's code has no module prefix (UniqueConstraintResponses.cs, DuplicateStreamResponses.cs): its
  // title is the framework's English, not a module's message.
  [
    'a 409 with a framework code',
    () =>
      problem(409, {
        title: 'The resource already exists.',
        status: 409,
        detail: 'Another request created it first.',
        code: 'already-exists',
      }),
  ],
  ['a code that is not a string', () => problem(400, { title: 'Der Name fehlt.', status: 400, code: 42 })],
  ['a blank code', () => problem(400, { title: 'Der Name fehlt.', status: 400, code: ' ' })],
])('a refusal with %s carries no server message', async (_body, response) => {
  const error = await refusalOf(response());

  expect(error.serverMessage).toBeUndefined();
});

// A page tells refusals of one status apart by the module's code (the two 403s and the two 409s of accepting an
// invitation): the code travels with the refusal on the same terms as the server's message.
test("a module's refusal carries its code", async () => {
  const error = await refusalOf(
    problem(410, {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.11',
      title: 'Die Einladung ist abgelaufen.',
      status: 410,
      code: 'organisation.invitation-expired',
    }),
  );

  expect(error.status).toBe(410);
  expect(error.code).toBe('organisation.invitation-expired');
});

test.each([
  ['already-exists', 409],
  ['invalid-argument', 400],
])("a framework's code is not a module's code: %s", async (code, status) => {
  const error = await refusalOf(problem(status, { title: 'The request is invalid.', status, detail: 'No.', code }));

  expect(error.code).toBeUndefined();
});

test('a 5xx carries no code', async () => {
  const error = await refusalOf(
    problem(503, { title: 'Der Dienst ist nicht erreichbar.', status: 503, code: 'organisation.unavailable' }),
  );

  expect(error.status).toBe(503);
  expect(error.code).toBeUndefined();
});
