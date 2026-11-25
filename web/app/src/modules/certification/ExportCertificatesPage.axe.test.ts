import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, fireEvent, render, screen } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { createMemoryHistory } from 'vue-router';
import { signedInAsDeveloper } from '@/auth/signedInAsDeveloper';
import { type Language, languages, setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import * as data from './ExportCertificatesPage.data';
import ExportCertificatesPage from './ExportCertificatesPage.vue';

// The screen's axe check, generated with it. It runs whatever the screen's own component test does, so a check skipped
// or commented out there never leaves the screen unchecked. It asks nothing of what the screen shows, only that axe
// finds no violations in it: what the screen does is its own tests'.

// The page renders on its own, without a query client or the host. Each hook of the generated client answers as if its
// request were still on its way or, once the page is shown with data, with its example in the data file: examples, or
// emptyExamples for the page with nothing to show.
// A mutation resolves with its answer in answers, once it is sent: the steps after it can be checked too.
const shown = vi.hoisted(() => ({
  examples: undefined as Record<string, unknown> | undefined,
  answers: {} as Record<string, unknown>,
}));
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
  const idle = (hook: string) => ({
    data: ref(undefined),
    error: ref(null),
    isIdle: ref(true),
    isPending: ref(false),
    isSuccess: ref(false),
    isError: ref(false),
    status: ref('idle'),
    mutate: vi.fn(),
    mutateAsync: vi.fn(async () => shown.answers[hook]),
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

const { examples } = data;
// A data file written before it had an empty example has none: the page is then not checked with nothing to show.
const emptyExamples = (data as { emptyExamples?: Record<string, unknown> }).emptyExamples ?? {};

// Whatever else asks the host waits for an answer that never comes.
vi.stubGlobal('fetch', () => new Promise<Response>(() => undefined));

// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);
afterEach(() => {
  shown.examples = undefined;
  shown.answers = {};
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
  await router.push('/certification/export');

  // The page reads the current pharmacy (a Pinia store over the memberships) and the query client (it marks the
  // memberships stale after a 404).
  const { container } = render(ExportCertificatesPage, {
    global: {
      plugins: [router, createPinia(), [VueQueryPlugin, { queryClient: new QueryClient() }], signedInAsDeveloper],
    },
  });
  // The screen speaks the language of the moment: axe checks what that language's user hears.
  expect(
    screen.getByRole('heading', { level: 1, name: t('modules.certification.pages.export.title') }),
  ).toBeInTheDocument();
  return container;
}

/** The retention period form's field and button, by language (#419): their texts come with the section. */
const retentionForm: Record<Language, { years: string; submit: string }> = {
  'de-DE': { years: 'Aufbewahrungsfrist in Jahren', submit: 'Frist speichern' },
  'en-GB': { years: 'Retention period in years', submit: 'Save period' },
};

// Each language in turn: the texts differ, and so may what axe finds in them.
describe.each(languages)('in %s', (language) => {
  beforeEach(() => {
    setLanguage(language, { remember: false });
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-11-24T10:00:00Z'));
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  test('the Export records page has no accessibility violations axe can find', async () => {
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });

  test.each(themes)(
    'the Export records page has no accessibility violations axe can find in the $name theme',
    async ({ switchOn }) => {
      switchOn();
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  // Until ExportCertificatesPage.data.ts has an example (AGENTS.md), the page is checked only as it loads.
  test.runIf(Object.keys(examples).length > 0)('the Export records page passes axe with its example data', async () => {
    shown.examples = examples;
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });

  test.runIf(Object.keys(examples).length > 0).each(themes)(
    'the Export records page passes axe with its example data in the $name theme',
    async ({ switchOn }) => {
      shown.examples = examples;
      switchOn();
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  // Until ExportCertificatesPage.data.ts has an empty example (AGENTS.md), the page is not checked with nothing to show.
  test.runIf(Object.keys(emptyExamples).length > 0)(
    'the Export records page passes axe when it has nothing to show',
    async () => {
      shown.examples = emptyExamples;
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  // The form as it shows after a refused range: the field's error in its alert.
  test.each([{ name: 'light', switchOn: () => undefined }, ...themes])(
    'the Export records page passes axe with a field error, in the $name theme',
    async ({ switchOn }) => {
      shown.examples = examples;
      switchOn();
      const user = userEvent.setup();
      const container = await renderPage();

      await fireEvent.update(screen.getByLabelText(t('certification.export.to')), '2025-01-01');
      await user.click(screen.getByRole('button', { name: t('certification.export.submit') }));
      expect(await screen.findByRole('alert')).toHaveTextContent(t('certification.export.period.order'));

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  // The retention period form as it shows after a refused period (#419): the field's error in its alert. The generated
  // mock's mutation is idle, and the form refuses 31 before anything is sent.
  test.each([{ name: 'light', switchOn: () => undefined }, ...themes])(
    'the Export records page passes axe with a retention period error, in the $name theme',
    async ({ switchOn }) => {
      shown.examples = examples;
      switchOn();
      const user = userEvent.setup();
      const container = await renderPage();

      await fireEvent.update(screen.getByLabelText(retentionForm[language].years), '31');
      await user.click(screen.getByRole('button', { name: retentionForm[language].submit }));
      expect(await screen.findByRole('alert')).toHaveTextContent(
        t('errors.certification.invalid-retention-period', { min: 2, max: 30 }),
      );

      expect(await axe(container)).toHaveNoViolations();
    },
  );
});
