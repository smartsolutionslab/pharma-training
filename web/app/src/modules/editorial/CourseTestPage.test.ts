import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor } from '@testing-library/vue';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { EditorialCourseVersionDto } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import CourseTestPage from './CourseTestPage.vue';
import { refusal, versionId, versionOf } from './courseTestFixtures';
import { route } from './routes';

// The test page of a course version (spec #25, S1 and T2): /editorial/versions/:id/test, a detail of its own beside the
// version page. Headed by the version's title, with its number and status, and the way back to the version; read-only
// unless the version is a draft.

// The host, as the generated client reaches it: GET /editorial/course-versions/{id} answers the version the test gives.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

let version: EditorialCourseVersionDto;
let reading: () => Promise<Response>;

beforeEach(() => {
  version = versionOf('Draft');
  reading = async () => Response.json(version);
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    if ((init?.method ?? 'GET') === 'GET' && url.pathname === `/api/editorial/course-versions/${versionId}`) {
      return reading();
    }
    return new Response('', { status: 404 });
  });
});
afterEach(() => host.mockReset());
afterEach(cleanup);

/** What the page asked of the host, by method and path below /api. */
const requests = () =>
  host.mock.calls.map(([input, init]) => ({
    method: init?.method ?? 'GET',
    path: new URL(String(input), 'http://localhost').pathname.replace(/^\/api/, ''),
  }));

const heading = () => t('editorial.test.heading', { title: version.title });

/** The page at its address, with a query client of its own that does not retry, so a failure shows at once. */
async function renderAt(id = versionId) {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/editorial/versions/${id}/test`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(CourseTestPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
}

async function renderPage() {
  const rendered = await renderAt();
  await screen.findByRole('heading', { level: 1, name: heading() });
  return rendered;
}

test('the editorial detail versions/:id/test opens the course test page, for editors, titled Test', () => {
  const detail = route.details?.find((each) => each.path === 'versions/:id/test');

  expect(detail?.component).toBe(CourseTestPage);
  expect(detail?.audience).toBe('editor');
  expect(detail?.titleKey).toBe('modules.editorial.details.test.title');
  expect(t('modules.editorial.details.test.title')).toBe('Test');
  // The version's own page is still there, beside it.
  expect(route.details?.map((each) => each.path)).toEqual(
    expect.arrayContaining(['versions/:id', 'versions/:id/test']),
  );
});

describe('a draft', () => {
  test("the test page is headed by the version's title, with its number and status", async () => {
    await renderPage();

    expect(screen.getByRole('heading', { level: 1, name: heading() })).toBeInTheDocument();
    expect(screen.getByText(t('editorial.version.number', { number: 2 }))).toBeInTheDocument();
    expect(screen.getByText(t('glossary.courseVersionStatus.Draft'))).toBeInTheDocument();
    // A draft can be changed: it says no reason why not.
    expect(screen.queryByText(/editorial\.version\.readOnly/)).not.toBeInTheDocument();
    expect(screen.queryByText(t('editorial.version.readOnly.Published'))).not.toBeInTheDocument();
  });

  test('it links back to its version', async () => {
    await renderPage();

    expect(screen.getByRole('link', { name: t('editorial.test.backToVersion') })).toHaveAttribute(
      'href',
      `/editorial/versions/${versionId}`,
    );
  });

  test('speaks German by default', async () => {
    await renderPage();

    expect(screen.getByRole('heading', { level: 1, name: 'Test zu „Hygiene in der Apotheke“' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Zurück zur Version' })).toBeInTheDocument();
    expect(screen.getByText('Version 2')).toBeInTheDocument();
    expect(screen.getByText('Entwurf')).toBeInTheDocument();
  });

  test('switched to English, speaks English', async () => {
    await renderPage();

    setLanguage('en-GB', { remember: false });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Test for “Hygiene in der Apotheke”' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to the version' })).toBeInTheDocument();
    expect(screen.getByText('Draft')).toBeInTheDocument();
  });
});

// Read-only unless the version is a draft: the existing notice says why, and the page has no control that would change
// the test (no form, no input, no change button), and asks the host for nothing but the version.
describe.each(['InReview' as const, 'Published' as const, 'Superseded' as const, 'Retired' as const])(
  'a version %s',
  (status) => {
    beforeEach(() => {
      version = versionOf(status);
    });

    test('a non-draft says why it cannot be changed', async () => {
      await renderPage();

      expect(screen.getByText(t(`editorial.version.readOnly.${status}`))).toBeInTheDocument();
      expect(screen.getByText(t(`glossary.courseVersionStatus.${status}`))).toBeInTheDocument();
      expect(screen.getByRole('link', { name: t('editorial.test.backToVersion') })).toHaveAttribute(
        'href',
        `/editorial/versions/${versionId}`,
      );
    });

    test('has no control that would change the test', async () => {
      const { container } = await renderPage();

      expect(screen.queryByRole('button', { name: t('editorial.test.blueprint.edit') })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: t('editorial.test.questions.add') })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: t('editorial.test.draft.start') })).not.toBeInTheDocument();
      const names = screen
        .queryAllByRole('button')
        .map((button) => [button.textContent, button.getAttribute('aria-label'), button.title].join(' '));
      expect(names.filter((name) => /bearbeiten|löschen|ändern|hinzufügen|speichern|entwurf/i.test(name))).toEqual([]);
      expect(container.querySelector('form, input, textarea, select')).toBeNull();
    });

    test('asks the host for nothing but the version', async () => {
      await renderPage();

      await waitFor(() =>
        expect(requests()).toEqual([{ method: 'GET', path: `/editorial/course-versions/${versionId}` }]),
      );
    });
  },
);

test('a published version says so in German', async () => {
  version = versionOf('Published');
  await renderPage();

  expect(
    screen.getByText(
      'Diese Version ist veröffentlicht und kann nicht mehr geändert werden. Änderungen machen Sie in einer neuen Version.',
    ),
  ).toBeInTheDocument();
});

test('while the version loads the page shows skeletons shaped like the blueprint and three questions', async () => {
  reading = () => new Promise<Response>(() => undefined);

  const { container } = await renderAt();

  await waitFor(() => expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThanOrEqual(4));
  expect(screen.queryByRole('heading', { level: 1 })).not.toBeInTheDocument();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

test('a version that does not exist says so', async () => {
  reading = async () => refusal(404, 'editorial.course-version-not-found', 'Die Version gibt es nicht.');

  await renderAt();

  expect(await screen.findByText(t('editorial.version.notFound'))).toBeVisible();
  expect(screen.getByText('Diese Version gibt es nicht.')).toBeVisible();
  expect(screen.queryByRole('heading', { level: 1 })).not.toBeInTheDocument();
});

test('an address whose id is no version says the version does not exist, and asks the host nothing', async () => {
  await renderAt('not-a-version');

  expect(await screen.findByText(t('editorial.version.notFound'))).toBeVisible();
  expect(requests()).toEqual([]);
});

test('a version the host cannot give is reported', async () => {
  reading = async () => new Response('', { status: 500 });

  await renderAt();

  expect(await screen.findByRole('alert')).toHaveTextContent(t('editorial.version.loadFailed'));
  expect(screen.getByRole('alert')).toHaveTextContent('Die Version konnte nicht geladen werden.');
  expect(screen.queryByText(t('editorial.version.notFound'))).not.toBeInTheDocument();
});
