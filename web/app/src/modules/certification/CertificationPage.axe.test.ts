import { cleanup, render } from '@testing-library/vue';
import { afterEach, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { createMemoryHistory } from 'vue-router';
import { signedInAsDeveloper } from '@/auth/signedInAsDeveloper';
import { createAppRouter } from '@/router';
import * as data from './CertificationPage.data';
import CertificationPage from './CertificationPage.vue';

// The screen's axe check, generated with it. It runs whatever the screen's own component test does, so a check skipped
// or commented out there never leaves the screen unchecked. It asks nothing of what the screen shows, only that axe
// finds no violations in it: what the screen does is its own tests'.

// The page renders on its own, without a query client or the host. Each hook of the generated client answers as if its
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
  // A mutation nobody has triggered is idle, not saving: the page shows as it first does.
  const idle = () => ({
    data: ref(undefined),
    error: ref(null),
    isIdle: ref(true),
    isPending: ref(false),
    isSuccess: ref(false),
    isError: ref(false),
    status: ref('idle'),
    mutate: vi.fn(),
    mutateAsync: vi.fn(),
    reset: vi.fn(),
  });
  const generated = await importOriginal<Record<string, unknown>>();
  const isMutation = (hook: string) => `get${hook.slice('use'.length)}MutationOptions` in generated;
  return Object.fromEntries(
    Object.entries(generated).map(([name, value]) => {
      if (!/^use[A-Z]/.test(name)) {
        return [name, value];
      }
      return [name, isMutation(name) ? idle : () => answer(name)];
    }),
  );
});

const { examples } = data;
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
async function renderPage() {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/certification');

  return render(CertificationPage, {
    global: { plugins: [router, signedInAsDeveloper] },
  }).container;
}

test('the Certification page has no accessibility violations axe can find', async () => {
  const container = await renderPage();

  expect(await axe(container)).toHaveNoViolations();
});

test.each(themes)(
  'the Certification page has no accessibility violations axe can find in the $name theme',
  async ({ switchOn }) => {
    switchOn();
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  },
);

// Until CertificationPage.data.ts has an example (AGENTS.md), the page is checked only as it loads.
test.runIf(Object.keys(examples).length > 0)('the Certification page passes axe with its example data', async () => {
  shown.examples = examples;
  const container = await renderPage();

  expect(await axe(container)).toHaveNoViolations();
});

test.runIf(Object.keys(examples).length > 0).each(themes)(
  'the Certification page passes axe with its example data in the $name theme',
  async ({ switchOn }) => {
    shown.examples = examples;
    switchOn();
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  },
);

// Until CertificationPage.data.ts has an empty example (AGENTS.md), the page is not checked with nothing to show.
test.runIf(Object.keys(emptyExamples).length > 0)(
  'the Certification page passes axe when it has nothing to show',
  async () => {
    shown.examples = emptyExamples;
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  },
);

test.runIf(Object.keys(emptyExamples).length > 0).each(themes)(
  'the Certification page passes axe when it has nothing to show in the $name theme',
  async ({ switchOn }) => {
    shown.examples = emptyExamples;
    switchOn();
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  },
);
