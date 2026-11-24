import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen } from '@testing-library/vue';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { createMemoryHistory } from 'vue-router';
import { signedInAsDeveloper } from '@/auth/signedInAsDeveloper';
import { languages, setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import * as data from './CoursePlayerPage.data';
import CoursePlayerPage from './CoursePlayerPage.vue';
import { FakeIntersectionObserver } from './fakeIntersectionObserver';

// The screen's axe check. It runs whatever the screen's own component test does, so a check skipped or commented out
// there never leaves the screen unchecked. It asks nothing of what the screen shows, only that axe finds no violations
// in it: what the screen does is its own tests'.

// The page renders on its own, without the host: a query client of its own holds nothing. Each query hook of the
// generated client answers as if its request were still on its way or, once the page is shown with data, with its
// example in the data file. The start is a mutation: shown with data, it answers its example as its data and from
// mutate and mutateAsync, as the host would have answered the POST.
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
  // A mutation without an example is still on its way, as the start is while the page loads; with one, it has answered.
  const mutation = (hook: string) => {
    const example = shown.examples === undefined ? undefined : shown.examples[hook];
    const answered = example !== undefined;
    return {
      data: ref(example),
      error: ref(null),
      isIdle: ref(false),
      isPending: ref(!answered),
      isSuccess: ref(answered),
      isError: ref(false),
      status: ref(answered ? 'success' : 'pending'),
      mutate: vi.fn((_variables: unknown, options?: { onSuccess?: (data: unknown) => void }) => {
        if (answered) {
          options?.onSuccess?.(example);
        }
      }),
      mutateAsync: vi.fn(() => (answered ? Promise.resolve(example) : new Promise(() => undefined))),
      reset: vi.fn(),
    };
  };
  const generated = await importOriginal<Record<string, unknown>>();
  const isMutation = (hook: string) => `get${hook.slice('use'.length)}MutationOptions` in generated;
  return Object.fromEntries(
    Object.entries(generated).map(([name, value]) => {
      if (!/^use[A-Z]/.test(name)) {
        return [name, value];
      }
      return [name, isMutation(name) ? () => mutation(name) : () => answer(name)];
    }),
  );
});

const { examples, emptyExamples, shownChapterId } = data;

// Whatever else asks the host waits for an answer that never comes.
vi.stubGlobal('fetch', () => new Promise<Response>(() => undefined));
// jsdom has no IntersectionObserver; the text chapter watches its end with one.
FakeIntersectionObserver.install();
beforeEach(() => FakeIntersectionObserver.reset());

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

const versionId = '0198c1a0-0000-7000-8000-0000000000c1';

// The router is on the screen's route before it mounts. The screen renders for the local user developer, signed in,
// without the sign-in plugin: the check sees the screen as the developer does.
async function renderPage() {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/learning/courses/${versionId}?chapter=${shownChapterId}`);

  const { container } = render(CoursePlayerPage, {
    global: { plugins: [router, signedInAsDeveloper, [VueQueryPlugin, { queryClient: new QueryClient() }]] },
  });
  // The screen speaks the language of the moment: axe checks what that language's user hears. Shown with a version, it
  // is headed by the course's title and names its chapters (or says it has none); while it loads, it shows skeletons.
  const shownVersion = shown.examples?.useGetCatalogCourseVersionsId as
    { title: string; chapters: { title: string; chapterId: string }[] } | undefined;
  if (shownVersion === undefined) {
    expect(container.querySelector('[data-slot="skeleton"]')).not.toBeNull();
  } else {
    expect(await screen.findByRole('heading', { level: 1, name: shownVersion.title })).toBeInTheDocument();
    if (shownVersion.chapters.length === 0) {
      expect(await screen.findByText(t('learning.player.empty'))).toBeInTheDocument();
    } else {
      expect(screen.getByRole('navigation', { name: t('learning.player.chapters') })).toBeInTheDocument();
      const current = shownVersion.chapters.find((chapter) => chapter.chapterId === shownChapterId);
      expect(await screen.findByRole('heading', { level: 2, name: current?.title })).toBeInTheDocument();
      expect(screen.getByText(t('learning.player.text.hint'))).toBeInTheDocument();
    }
  }
  return container;
}

// Each language in turn: the texts differ, and so may what axe finds in them.
describe.each(languages)('in %s', (language) => {
  beforeEach(() => {
    setLanguage(language, { remember: false });
  });

  test('the course player has no accessibility violations axe can find while it loads', async () => {
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });

  test.each(themes)(
    'the course player has no accessibility violations axe can find while it loads in the $name theme',
    async ({ switchOn }) => {
      switchOn();
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  test('the course player passes axe with its example data', async () => {
    shown.examples = examples;
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });

  test.each(themes)('the course player passes axe with its example data in the $name theme', async ({ switchOn }) => {
    shown.examples = examples;
    switchOn();
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });

  test('the course player passes axe when the course has no chapters', async () => {
    shown.examples = emptyExamples;
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });

  test.each(themes)(
    'the course player passes axe when the course has no chapters in the $name theme',
    async ({ switchOn }) => {
      shown.examples = emptyExamples;
      switchOn();
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );
});
