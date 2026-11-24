import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { render } from '@testing-library/vue';
import type { Component } from 'vue';
import { createMemoryHistory } from 'vue-router';
import type { AssessmentExaminationDto } from '@/api/generated';
import { createAppRouter } from '@/router';
import {
  attempt,
  courseVersion,
  examinationId,
  inAttempt,
  result,
  saved,
  sessionId,
  versionId,
} from './examinationFixtures';

// The host as the test page and the result page (#48) see it, answering each request by a handler the test sets: the
// read by session, the start, an answer, the submit, and the catalog's version.

export type Fetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

export interface ExaminationHost {
  reading: () => Promise<Response>;
  starting: () => Promise<Response>;
  answering: (questionId: string, optionIds: string[]) => Promise<Response>;
  submitting: () => Promise<Response>;
  catalogReading: () => Promise<Response>;
}

/** A host that answers the read with the examination, and everything else as it succeeds. */
export function hostAnswering(examination: AssessmentExaminationDto = inAttempt()): ExaminationHost {
  return {
    reading: async () => Response.json(examination),
    starting: async () => Response.json(attempt(), { status: 201 }),
    answering: async () => saved(),
    submitting: async () => Response.json(result()),
    catalogReading: async () => Response.json(courseVersion()),
  };
}

const base = `/api/assessment/examinations`;

/** Routes each request to the host's handler, as the host's addresses are; anything else is a 404. */
export function routed(host: ExaminationHost): Fetch {
  return async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    const path = url.pathname;
    if (method === 'GET' && path === `${base}/by-session/${sessionId}`) {
      return host.reading();
    }
    if (method === 'POST' && path === `${base}/${examinationId}/attempts`) {
      return host.starting();
    }
    const answer = new RegExp(`^${base}/${examinationId}/attempts/current/answers/([^/]+)$`).exec(path);
    if (method === 'PUT' && answer !== null) {
      const { optionIds } = JSON.parse(String(init?.body)) as { optionIds: string[] };
      return host.answering(answer[1] ?? '', optionIds);
    }
    if (method === 'POST' && path === `${base}/${examinationId}/attempts/current/submit`) {
      return host.submitting();
    }
    if (method === 'GET' && path === `/api/catalog/course-versions/${versionId}`) {
      return host.catalogReading();
    }
    return new Response('', { status: 404 });
  };
}

export interface SentRequest {
  method: string;
  /** Below /api. */
  path: string;
  body: unknown;
}

/** What the page asked of the host, in order, by method and path below /api, with the body it sent. */
export function requestsOf(calls: Parameters<Fetch>[]): SentRequest[] {
  return calls.map(([input, init]) => ({
    method: init?.method ?? 'GET',
    path: new URL(String(input), 'http://localhost').pathname.replace(/^\/api/, ''),
    body: init?.body === undefined || init.body === null ? undefined : (JSON.parse(String(init.body)) as unknown),
  }));
}

export const readPath = `/assessment/examinations/by-session/${sessionId}`;
export const startPath = `/assessment/examinations/${examinationId}/attempts`;
export const answerPath = (questionId: string) =>
  `/assessment/examinations/${examinationId}/attempts/current/answers/${questionId}`;
export const submitPath = `/assessment/examinations/${examinationId}/attempts/current/submit`;

export const testAddress = `/assessment/sessions/${sessionId}`;
export const resultAddress = `/assessment/sessions/${sessionId}/result`;

/** The page at its address, with a query client of its own that does not retry, so a failure shows at once. */
export async function renderAt(component: Component, address: string) {
  const router = createAppRouter(createMemoryHistory());
  await router.push(address);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const rendered = render(component, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  return { router, queryClient, container: rendered.container };
}
