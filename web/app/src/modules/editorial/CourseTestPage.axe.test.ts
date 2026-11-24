import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { createMemoryHistory } from 'vue-router';
import { signedInAsDeveloper } from '@/auth/signedInAsDeveloper';
import { languages, setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import * as data from './CourseTestPage.data';
import CourseTestPage from './CourseTestPage.vue';

// The screen's axe check, generated with it. It runs whatever the screen's own component test does, so a check skipped
// or commented out there never leaves the screen unchecked. It asks nothing of what the screen shows, only that axe
// finds no violations in it: what the screen does is its own tests'.

// The page renders on its own, without the host: a query client of its own holds nothing. Each hook of the generated client answers as if its
// request were still on its way or, once the page is shown with data, with its example in the data file: examples, or
// emptyExamples for the page with nothing to show.
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
    mutate: vi.fn(),
    mutateAsync: vi.fn(),
  });
  const answer = (hook: string) =>
    shown.examples !== undefined && hook in shown.examples
      ? state(shown.examples[hook], false)
      : state(undefined, true);
  // A mutation nobody has triggered is idle, not saving: the page shows as it first does. Triggered, it answers with
  // its example, if the data shown has one.
  const idle = (hook: string) => ({
    data: ref(undefined),
    error: ref(null),
    isIdle: ref(true),
    isPending: ref(false),
    isSuccess: ref(false),
    isError: ref(false),
    status: ref('idle'),
    mutate: vi.fn(),
    mutateAsync: vi.fn(async () => shown.examples?.[hook]),
    reset: vi.fn(),
  });
  const generated = await importOriginal<Record<string, unknown>>();
  const isMutation = (hook: string) => `get${hook.slice('use'.length)}MutationOptions` in generated;
  return Object.fromEntries(
    Object.entries(generated).map(([name, value]) => {
      if (!/^use[A-Z]/.test(name)) {
        return [name, value];
      }
      return [name, isMutation(name) ? () => idle(name) : () => answer(name)];
    }),
  );
});

const { examples, pdfDraftExamples } = data;
// A data file written before it had an empty example has none: the page is then not checked with nothing to show.
const emptyExamples = (data as { emptyExamples?: Record<string, unknown> }).emptyExamples ?? {};

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

// The router is on the screen's route before it mounts, and the screen has rendered when render returns.
// The screen renders for the local user developer, signed in, without the sign-in plugin: the check sees the screen as
// the developer does.
const versionId = '0198c1a0-0000-7000-8000-0000000000c1';

async function renderPage() {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/editorial/versions/${versionId}/test`);

  const { container } = render(CourseTestPage, {
    global: { plugins: [router, signedInAsDeveloper, [VueQueryPlugin, { queryClient: new QueryClient() }]] },
  });
  // The screen speaks the language of the moment: axe checks what that language's user hears. Shown with a version, it
  // is headed by the test of the version's title; while it loads, it shows skeletons only.
  const shownVersion = shown.examples?.useGetEditorialCourseVersionsId as { title: string } | undefined;
  if (shownVersion !== undefined) {
    expect(
      screen.getByRole('heading', { level: 1, name: t('editorial.test.heading', { title: shownVersion.title }) }),
    ).toBeInTheDocument();
  }
  return container;
}

// Each language in turn: the texts differ, and so may what axe finds in them.
describe.each(languages)('in %s', (language) => {
  beforeEach(() => {
    setLanguage(language, { remember: false });
  });

  test('the course test page has no accessibility violations axe can find', async () => {
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });

  test.each(themes)(
    'the course test page has no accessibility violations axe can find in the $name theme',
    async ({ switchOn }) => {
      switchOn();
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  // Until CourseTestPage.data.ts has an example (AGENTS.md), the page is checked only as it loads.
  test.runIf(Object.keys(examples).length > 0)('the course test page passes axe with its example data', async () => {
    shown.examples = examples;
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });

  test.runIf(Object.keys(examples).length > 0).each(themes)(
    'the course test page passes axe with its example data in the $name theme',
    async ({ switchOn }) => {
      shown.examples = examples;
      switchOn();
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  // Until CourseTestPage.data.ts has an empty example (AGENTS.md), the page is not checked with nothing to show.
  test.runIf(Object.keys(emptyExamples).length > 0)(
    'the course test page passes axe when it has nothing to show',
    async () => {
      shown.examples = emptyExamples;
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  test.runIf(Object.keys(emptyExamples).length > 0).each(themes)(
    'the course test page passes axe when it has nothing to show in the $name theme',
    async ({ switchOn }) => {
      shown.examples = emptyExamples;
      switchOn();
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  // A version of PDF chapters only, after a draft that left out a PDF without text and a video and cut a long PDF
  // (#301): the note that names them is checked too.
  async function renderDraftedPdfs(user: ReturnType<typeof userEvent.setup>) {
    shown.examples = pdfDraftExamples;
    const container = await renderPage();
    await user.click(screen.getByRole('button', { name: t('editorial.test.draft.start') }));
    const dialog = within(await screen.findByRole('dialog', { name: t('editorial.test.draft.confirm.title') }));
    await user.click(dialog.getByRole('button', { name: t('editorial.test.draft.confirm.submit') }));
    await screen.findByText(t('editorial.test.draft.truncated'));
    expect(screen.getByText(t('editorial.test.draft.leftOut'))).toBeInTheDocument();
    return container;
  }

  test('the course test page passes axe with a drafted version of PDF chapters only', async () => {
    const container = await renderDraftedPdfs(userEvent.setup());

    expect(await axe(container)).toHaveNoViolations();
  });

  test.each(themes)(
    'the course test page passes axe with a drafted version of PDF chapters only in the $name theme',
    async ({ switchOn }) => {
      switchOn();
      const container = await renderDraftedPdfs(userEvent.setup());

      expect(await axe(container)).toHaveNoViolations();
    },
  );
});
