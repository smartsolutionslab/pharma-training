import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { render, screen } from '@testing-library/vue';
import { createMemoryHistory } from 'vue-router';
import type { EditorialCourseVersionDto, EditorialCourseVersionStatus } from '@/api/generated';
import { createAppRouter } from '@/router';
import CourseVersionPage from './CourseVersionPage.vue';
import { versionOf } from './courseTestFixtures';

// The host as the course version page's workflow tests (#26) reach it through the generated client: GET
// /editorial/course-versions/{id} answers the version the test holds (under the id asked for), and POST
// /editorial/course-versions/{id}/{step} answers what the test says, by default 200, the version moved on as the host
// moves it. Each test file stubs fetch with answering(host) and mocks @/lib/notify itself.

export type Fetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

export type WorkflowStep = 'submit' | 'publish' | 'return' | 'retire';

export interface WorkflowHost {
  /** The version GET answers. */
  version: EditorialCourseVersionDto;
  /** What a step answers; by default stepTaken. */
  step: (step: WorkflowStep, body: unknown) => Promise<Response>;
}

/** Where each step takes a version. */
const movesTo: Record<WorkflowStep, EditorialCourseVersionStatus> = {
  submit: 'InReview',
  publish: 'Published',
  return: 'Draft',
  retire: 'Retired',
};

/** A host holding the version, whose steps succeed. */
export function workflowHost(version: EditorialCourseVersionDto = versionOf('Draft')): WorkflowHost {
  const host: WorkflowHost = {
    version,
    step: async (step) => stepTaken(host, step),
  };
  return host;
}

/** The step taken: the version moves on, and the host answers 200 with Unit. */
export function stepTaken(host: WorkflowHost, step: WorkflowStep): Response {
  host.version = { ...host.version, status: movesTo[step] };
  return Response.json({});
}

/** A refusal as the host sends it: problem details, with a module's code where it has one. */
export function refused(status: number, code?: string): Response {
  return new Response(
    JSON.stringify({ title: 'Refused', status, detail: 'Words of the host, not for the user.', code }),
    {
      status,
      headers: { 'Content-Type': 'application/problem+json' },
    },
  );
}

const versionPath = /^\/api\/editorial\/course-versions\/([0-9a-f-]{36})$/;
const stepPath = /^\/api\/editorial\/course-versions\/([0-9a-f-]{36})\/(submit|publish|return|retire)$/;

/** The fetch the page reaches the host through. */
export function answering(host: WorkflowHost): Fetch {
  return async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    const read = versionPath.exec(url.pathname);
    if (method === 'GET' && read !== null) {
      return Response.json({ ...host.version, id: read[1] });
    }
    const step = stepPath.exec(url.pathname);
    if (method === 'POST' && step !== null) {
      const body = init?.body === undefined ? undefined : JSON.parse(String(init.body));
      return host.step(step[2] as WorkflowStep, body);
    }
    return new Response('', { status: 404 });
  };
}

/** The requests the page made, by method, path below /api and body. */
export function requestsOf(fetch: { mock: { calls: Parameters<Fetch>[] } }) {
  return fetch.mock.calls.map(([input, init]) => ({
    method: init?.method ?? 'GET',
    path: new URL(String(input), 'http://localhost').pathname.replace(/^\/api/, ''),
    body: init?.body === undefined ? undefined : JSON.parse(String(init.body)),
  }));
}

/** The page at the version's address, with a query client of its own that does not retry. */
export async function renderVersionPage(id: string, title: string) {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/editorial/versions/${id}`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const { container } = render(CourseVersionPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  const heading = await screen.findByRole('heading', { level: 1, name: title });
  return { router, queryClient, container, heading };
}
