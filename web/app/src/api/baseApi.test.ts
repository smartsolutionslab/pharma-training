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

// The problem's code at any status, apart from code (#25, S3): the host's own refusals above 500 (503
// model-not-configured, 502 editorial.ai-draft-malformed) are told apart from an edge's 503 or 502 by it. It is never
// words for the user, so it is kept apart from code and serverMessage, whose rules stay.
test.each([
  ['a 503 model-not-configured', 503, 'model-not-configured'],
  ['a 502 ai-draft-malformed', 502, 'editorial.ai-draft-malformed'],
  ["a 409 module's refusal", 409, 'editorial.not-a-draft'],
  ["a 409 framework's code", 409, 'already-exists'],
  ['a 400 failed guard', 400, 'invalid-argument'],
])("%s carries the problem's code as its failure code", async (_answer, status, code) => {
  const error = await refusalOf(problem(status, { title: 'Refused', status, detail: 'Words of the host.', code }));

  expect(error.status).toBe(status);
  expect(error.failureCode).toBe(code);
});

test('a 5xx with a code keeps code and serverMessage absent while carrying the failure code', async () => {
  const error = await refusalOf(
    problem(503, { title: 'Service Unavailable', status: 503, detail: 'No model.', code: 'model-not-configured' }),
  );

  expect(error.failureCode).toBe('model-not-configured');
  expect(error.code).toBeUndefined();
  expect(error.serverMessage).toBeUndefined();
});

test.each([
  ['an empty body', () => new Response('', { status: 503 })],
  ['a plain-text body', () => new Response('Bad Gateway', { status: 502, headers: { 'Content-Type': 'text/plain' } })],
  ['a problem without a code', () => problem(503, { title: 'Service Unavailable', status: 503 })],
  ['a code that is not a string', () => problem(502, { title: 'Bad Gateway', status: 502, code: 42 })],
  ['a blank code', () => problem(503, { title: 'Service Unavailable', status: 503, code: ' ' })],
  ['a JSON body that is not an object', () => Response.json('model-not-configured', { status: 503 })],
])('a refusal with %s carries no failure code', async (_body, response) => {
  const error = await refusalOf(response());

  expect(error.failureCode).toBeUndefined();
});

test('an ApiError made from a body directly carries its failure code too', () => {
  const error = new ApiError(503, JSON.stringify({ title: 'Refused', status: 503, code: 'model-not-configured' }));

  expect(error.failureCode).toBe('model-not-configured');
});

// A binary answer (#297, ADR 0032: the certificate export streams a ZIP): a 2xx whose Content-Type is neither JSON nor
// absent comes back as the bytes, a File when Content-Disposition names one (filename* first), else a Blob.
const zipBytes = new Uint8Array([0x50, 0x4b, 0x03, 0x04, 0x14, 0x00]);

/**
 * The ZIP as the host streams it. Its blob() answers a Blob of the page's own realm: undici's Blob, which a Response
 * gives in a test, is no `instanceof Blob` in jsdom, and a File made from one holds "[object Blob]" instead of the bytes.
 */
function zip(headers: Record<string, string> = {}): Response {
  const response = new Response(zipBytes, { status: 200, headers: { 'Content-Type': 'application/zip', ...headers } });
  vi.spyOn(response, 'blob').mockImplementation(async () => new Blob([zipBytes], { type: 'application/zip' }));
  return response;
}

async function answerTo(response: Response): Promise<unknown> {
  host.mockResolvedValue(response);
  return apiFetch<unknown>('/certification/pharmacies/p/certificates/export', { method: 'GET' });
}

async function bytesOf(blob: Blob): Promise<number[]> {
  return [...new Uint8Array(await blob.arrayBuffer())];
}

test('a binary answer with a disposition naming a file is a File of that name, filename* first', async () => {
  const answer = await answerTo(zip({ 'Content-Disposition': "attachment; filename=a.zip; filename*=UTF-8''b.zip" }));

  expect(answer).toBeInstanceOf(File);
  const file = answer as File;
  expect(file.name).toBe('b.zip');
  expect(file.type).toBe('application/zip');
  expect(await bytesOf(file)).toEqual([...zipBytes]);
});

test('a binary answer without a disposition is a Blob, not a File, with the bytes sent', async () => {
  const answer = await answerTo(zip());

  expect(answer).toBeInstanceOf(Blob);
  expect(answer).not.toBeInstanceOf(File);
  expect(await bytesOf(answer as Blob)).toEqual([...zipBytes]);
});

test('a JSON answer is parsed, as before', async () => {
  const answer = await answerTo(Response.json({ items: [1, 2], totalCount: 2 }, { status: 200 }));

  expect(answer).toEqual({ items: [1, 2], totalCount: 2 });
});

test('a 404 problem is an ApiError with its code, as before', async () => {
  const error = await refusalOf(
    problem(404, { title: 'Die Apotheke gibt es nicht.', status: 404, code: 'certification.pharmacy-not-found' }),
  );

  expect(error.status).toBe(404);
  expect(error.code).toBe('certification.pharmacy-not-found');
});

test('an empty 204 is undefined, as before', async () => {
  const answer = await answerTo(new Response(null, { status: 204 }));

  expect(answer).toBeUndefined();
});

// The review of #297: the bytes are not copied. The answer is response.blob() itself, wrapped in a File only to carry
// the disposition's name; reading the body as an ArrayBuffer and building a Blob from it holds the ZIP twice.
test('a binary answer is read with blob(), never arrayBuffer()', async () => {
  const response = zip({ 'Content-Disposition': 'attachment; filename=teilnahmebestaetigungen.zip' });
  const arrayBuffer = vi.spyOn(response, 'arrayBuffer');

  const answer = await answerTo(response);

  expect(response.blob).toHaveBeenCalledTimes(1);
  expect(arrayBuffer).not.toHaveBeenCalled();
  expect(answer).toBeInstanceOf(File);
  expect((answer as File).name).toBe('teilnahmebestaetigungen.zip');
  expect((answer as File).type).toBe('application/zip');
  expect(await bytesOf(answer as File)).toEqual([...zipBytes]);
});

test('a binary answer without a disposition is the blob the response gave, not a copy', async () => {
  const response = zip();
  const blob = new Blob([zipBytes], { type: 'application/zip' });
  vi.spyOn(response, 'blob').mockResolvedValue(blob);
  const arrayBuffer = vi.spyOn(response, 'arrayBuffer');

  const answer = await answerTo(response);

  expect(answer).toBe(blob);
  expect(arrayBuffer).not.toHaveBeenCalled();
});

// Only binary types are binary: a 2xx text/* answer (an edge's or a proxy's HTML page, a plain-text body) is no file.
// Binary is any media type that is neither JSON (application/json, +json) nor text/* (application/zip, application/pdf,
// application/octet-stream, ...); a text answer is read as JSON, as before, and rejects when it is none.
test.each([
  ['text/html', '<!doctype html><title>Anmelden</title>'],
  ['text/html; charset=utf-8', '<html><body>Gateway</body></html>'],
  ['text/plain', 'OK'],
])('a 2xx %s answer is no binary file: it rejects as a JSON parse of it would', async (type, body) => {
  host.mockResolvedValue(new Response(body, { status: 200, headers: { 'Content-Type': type } }));

  const outcome: unknown = await apiFetch<unknown>('/certification/pharmacies/p/certificates/export', {
    method: 'GET',
  }).then(
    (answer) => ({ resolved: answer }),
    (caught: unknown) => ({ rejected: caught }),
  );

  expect(outcome).not.toHaveProperty('resolved');
  expect(outcome).toHaveProperty('rejected');
  expect((outcome as { rejected: unknown }).rejected).not.toBeInstanceOf(Blob);
});

// Content-Disposition's parameter names are whole names: filename is a parameter of its own, not the end of another
// name or a part of a quoted value; a quoted filename unescapes its quoted-pairs (RFC 6266, RFC 9110 5.6.4).
test.each([
  ['a name ending in filename', 'attachment; xfilename=evil.zip'],
  ["filename= inside another parameter's quoted value", 'attachment; foo="filename=x.zip"'],
])('a disposition with %s names no file: the answer is a Blob, not a File', async (_case, disposition) => {
  const answer = await answerTo(zip({ 'Content-Disposition': disposition }));

  expect(answer).toBeInstanceOf(Blob);
  expect(answer).not.toBeInstanceOf(File);
});

test('a quoted filename with an escaped quote names the file with the quote', async () => {
  const answer = await answerTo(zip({ 'Content-Disposition': 'attachment; filename="it\\"s.zip"' }));

  expect(answer).toBeInstanceOf(File);
  expect((answer as File).name).toBe('it"s.zip');
});
