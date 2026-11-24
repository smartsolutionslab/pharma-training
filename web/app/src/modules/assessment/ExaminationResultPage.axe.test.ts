import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen } from '@testing-library/vue';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { createMemoryHistory } from 'vue-router';
import { signedInAsDeveloper } from '@/auth/signedInAsDeveloper';
import { languages, setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import * as data from './ExaminationResultPage.data';
import ExaminationResultPage from './ExaminationResultPage.vue';

// The screen's axe check. It runs whatever the screen's own component tests do, so a check skipped or commented out
// there never leaves the screen unchecked. It asks nothing of what the screen shows, only that axe finds no violations
// in it: what the screen does is its own tests'.

// The page renders on its own, without the host: a query client of its own holds nothing. Each query hook of the
// generated client answers as if its request were still on its way or, once the page is shown with data, with its
// example in the data file. The page sets off no mutation: any is idle here.
const shown = vi.hoisted(() => ({ examples: undefined as Record<string, unknown> | undefined }));
vi.mock('@/api/generated', async (importOriginal) => {
  const { ref } = await import('vue');
  const state = (data: unknown, loading: boolean) => ({
    data: ref(data),
    error: ref(null),
    isLoading: ref(loading),
    isPending: ref(loading),
    isFetching: ref(loading),
    isSuccess: ref(!loading),
    isError: ref(false),
    status: ref(loading ? 'pending' : 'success'),
    refetch: vi.fn(),
  });
  const answer = (hook: string) =>
    shown.examples !== undefined && hook in shown.examples
      ? state(shown.examples[hook], false)
      : state(undefined, true);
  const mutation = () => ({
    data: ref(undefined),
    error: ref(null),
    isIdle: ref(true),
    isPending: ref(false),
    isSuccess: ref(false),
    isError: ref(false),
    status: ref('idle'),
    mutate: vi.fn(),
    mutateAsync: vi.fn(() => new Promise(() => undefined)),
    reset: vi.fn(),
  });
  const generated = await importOriginal<Record<string, unknown>>();
  const isMutation = (hook: string) => `get${hook.slice('use'.length)}MutationOptions` in generated;
  return Object.fromEntries(
    Object.entries(generated).map(([name, value]) => {
      if (!/^use[A-Z]/.test(name)) {
        return [name, value];
      }
      return [name, isMutation(name) ? () => mutation() : () => answer(name)];
    }),
  );
});

const { examples, emptyExamples } = data;

// Whatever else asks the host waits for an answer that never comes.
vi.stubGlobal('fetch', () => new Promise<Response>(() => undefined));

// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);
afterEach(() => {
  shown.examples = undefined;
});

// The app's other themes: each check runs again in each, switched on as the app switches it before the screen renders.
const themes = [
  {
    name: 'dark',
    // As the theme toggle does: the choice stored, the class on <html>.
    switchOn: () => {
      localStorage.setItem('theme', 'dark');
      document.documentElement.classList.add('dark');
    },
  },
];

afterEach(() => {
  document.documentElement.removeAttribute('class');
  localStorage.clear();
});

const sessionId = '0198c1a0-0000-7000-8000-0000000000b1';

// The router is on the screen's route before it mounts. The screen renders for the local user developer, signed in.
async function renderPage() {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/assessment/sessions/${sessionId}/result`);

  const { container } = render(ExaminationResultPage, {
    global: { plugins: [router, signedInAsDeveloper, [VueQueryPlugin, { queryClient: new QueryClient() }]] },
  });
  // The screen speaks the language of the moment: axe checks what that language's user hears. With a result it is
  // headed by the course's title and lists the chapters to repeat; without one it says so; while it loads, it shows
  // skeletons.
  const read = shown.examples?.useGetAssessmentExaminationsBySessionSessionId as { lastResult: unknown } | undefined;
  if (read === undefined) {
    expect(container.querySelector('[data-slot="skeleton"]')).not.toBeNull();
  } else if (read.lastResult !== null) {
    expect(
      await screen.findByRole('heading', {
        level: 1,
        name: t('assessment.result.heading', { title: 'Hygiene in der Apotheke' }),
      }),
    ).toBeInTheDocument();
    expect(screen.getByText(t('assessment.result.locked'))).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: /Einleitung|Hygieneplan/ })).toHaveLength(2);
  } else {
    expect(await screen.findByText(t('assessment.result.none'))).toBeInTheDocument();
  }
  return container;
}

// Each language in turn: the texts differ, and so may what axe finds in them.
describe.each(languages)('in %s', (language) => {
  beforeEach(() => {
    setLanguage(language, { remember: false });
  });

  test('the result page has no accessibility violations axe can find while it loads', async () => {
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });

  test.each(themes)(
    'the result page has no accessibility violations axe can find while it loads in the $name theme',
    async ({ switchOn }) => {
      switchOn();
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  test('the result page passes axe with the test locked', async () => {
    shown.examples = examples;
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });

  test.each(themes)('the result page passes axe with the test locked in the $name theme', async ({ switchOn }) => {
    shown.examples = examples;
    switchOn();
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });

  test('the result page passes axe without a result', async () => {
    shown.examples = emptyExamples;
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });

  test.each(themes)('the result page passes axe without a result in the $name theme', async ({ switchOn }) => {
    shown.examples = emptyExamples;
    switchOn();
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });
});
