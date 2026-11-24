import { cleanup, screen, within } from '@testing-library/vue';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { setLanguage, t } from '@/i18n';
import { coursePath } from './coursePath';
import {
  answering,
  type Fetch,
  inProgress,
  locked,
  myCourses,
  myCoursesHost,
  open,
  overdue,
  renderMyCoursesPage,
  retired,
} from './myCoursesFixtures';

// Each of my courses (#38, T3): its title, its status in the shared look (an icon and a word, never a colour alone),
// its due line, and the one action, Start or Continue, a link to the chapter player on the version Compliance names
// (ADR 0024). An overdue course is highlighted by its word, its icon, "War fällig am" and a red left border, each of
// which says "overdue" without the others.

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);

beforeEach(() => {
  localStorage.clear();
  fetch.mockImplementation(answering(myCoursesHost()));
});
afterEach(() => {
  fetch.mockReset();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** A course's item in the list, found by its title, its h3. */
async function itemOf(title: string): Promise<HTMLElement> {
  const list = await screen.findByRole('list');
  const heading = await within(list).findByRole('heading', { level: 3, name: title });
  const item = heading.closest<HTMLElement>('li');
  expect(item, `${title} is in a list item`).not.toBeNull();
  return item ?? heading;
}

/** The icons of an element: lucide's SVGs, which name their icon in their class. */
const iconsIn = (element: Element) => Array.from(element.querySelectorAll('svg'));

/** Each state the host sends for an active assignment, its word, its icon (lucide's class), and its item's due line. */
const states = [
  ['Overdue', 'Überfällig', 'lucide-circle-alert', overdue.courseTitle, 'War fällig am 20.11.2026'],
  ['Locked', 'Gesperrt', 'lucide-lock', locked.courseTitle, 'Fällig am 01.12.2026'],
  ['InProgress', 'In Bearbeitung', 'lucide-circle-dot', inProgress.courseTitle, 'Fällig am 15.12.2026'],
  ['Open', 'Offen', 'lucide-circle', open.courseTitle, 'Fällig am 10.01.2027'],
] as const;

describe('the status', () => {
  test.each(states)('%s shows its word %s, its icon and its due line', async (_state, word, icon, title, dueLine) => {
    await renderMyCoursesPage();

    const item = await itemOf(title);
    expect(item).toHaveTextContent(word);
    expect(item).toHaveTextContent(dueLine);
    const shown = iconsIn(item).filter((svg) => svg.classList.contains(icon));
    expect(shown, `${title} shows ${icon}`).toHaveLength(1);
    expect(shown[0]).toHaveAttribute('aria-hidden', 'true');
  });

  test('only the overdue course says it was due; the others say when they are due', async () => {
    await renderMyCoursesPage();

    expect(await itemOf(overdue.courseTitle)).toHaveTextContent('War fällig am 20.11.2026');
    for (const course of [locked, inProgress, open, retired]) {
      const item = await itemOf(course.courseTitle);
      expect(item).not.toHaveTextContent('War fällig am');
      expect(item).not.toHaveTextContent('Überfällig');
    }
  });

  test('the status word is text in the item, not only a label', async () => {
    await renderMyCoursesPage();

    const item = await itemOf(overdue.courseTitle);
    expect(within(item).getByText('Überfällig')).toBeVisible();
  });

  test('the due date is in tabular figures', async () => {
    await renderMyCoursesPage();

    const item = await itemOf(open.courseTitle);
    expect(
      within(item)
        .getByText(/Fällig am 10\.01\.2027/)
        .closest('.tabular-nums'),
    ).not.toBeNull();
  });
});

describe('the highlight', () => {
  test('only the overdue course has the red left border', async () => {
    await renderMyCoursesPage();

    expect(await itemOf(overdue.courseTitle)).toHaveClass('border-l-4', 'border-l-destructive');
    for (const course of [locked, inProgress, open, retired]) {
      expect(await itemOf(course.courseTitle)).not.toHaveClass('border-l-destructive');
    }
  });

  // The done-when, not by colour alone: whatever its classes, the overdue item says so in words.
  test('the overdue course says it is overdue in its text, whatever its classes', async () => {
    await renderMyCoursesPage();

    const item = await itemOf(overdue.courseTitle);
    expect(item.textContent).toContain('Überfällig');
    expect(item.textContent).toContain('War fällig am 20.11.2026');
  });

  test('each course sits on the page’s background with a border, never on a card or a tint', async () => {
    await renderMyCoursesPage();

    for (const course of myCourses) {
      const item = await itemOf(course.courseTitle);
      expect(item).toHaveClass('border', 'bg-background');
      expect(item).not.toHaveClass('bg-card');
    }
  });
});

test('a locked course says the test is locked until the content is worked through again', async () => {
  await renderMyCoursesPage();

  const text = 'Der Test ist gesperrt. Arbeiten Sie die Inhalte erneut durch, um ihn wieder freizuschalten.';
  expect(await itemOf(locked.courseTitle)).toHaveTextContent(text);
  for (const course of [overdue, inProgress, open, retired]) {
    expect(await itemOf(course.courseTitle)).not.toHaveTextContent(text);
  }
});

describe('the action', () => {
  test('a course not started links Starten to the player on its start version, named after the course', async () => {
    await renderMyCoursesPage();

    const item = await itemOf(overdue.courseTitle);
    const link = within(item).getByRole('link', { name: 'Starten: Hygiene in der Apotheke' });
    expect(link).toHaveAttribute('href', coursePath(overdue.startCourseVersionId ?? ''));
    expect(link).toHaveAttribute('href', '/learning/courses/0198c1a0-0000-7000-8000-0000000000e1');
    expect(link).toHaveTextContent(/^Starten$/);
  });

  test('a running course links Fortsetzen to the player on its session’s version, named after the course', async () => {
    await renderMyCoursesPage();

    const item = await itemOf(inProgress.courseTitle);
    const link = within(item).getByRole('link', { name: 'Fortsetzen: Notfallmanagement' });
    expect(link).toHaveAttribute('href', coursePath(inProgress.startCourseVersionId ?? ''));
    expect(link).toHaveTextContent(/^Fortsetzen$/);
  });

  test('each course with a start version has exactly one link, Start or Continue as the host says', async () => {
    await renderMyCoursesPage();

    for (const course of [overdue, locked, inProgress, open]) {
      const word = course.continuesSession ? 'Fortsetzen' : 'Starten';
      const links = within(await itemOf(course.courseTitle)).getAllByRole('link');
      expect(links).toHaveLength(1);
      expect(links[0]).toHaveAccessibleName(`${word}: ${course.courseTitle}`);
      expect(links[0]).toHaveAttribute('href', coursePath(course.startCourseVersionId ?? ''));
    }
  });

  test('a course without a start version has no link and says it cannot be started', async () => {
    await renderMyCoursesPage();

    const item = await itemOf(retired.courseTitle);
    expect(within(item).queryByRole('link')).not.toBeInTheDocument();
    expect(item).toHaveTextContent(
      'Diese Schulung kann derzeit nicht gestartet werden: ihre Version ist zurückgezogen.',
    );
  });

  test('the page starts no session: it only links', async () => {
    await renderMyCoursesPage();

    await itemOf(overdue.courseTitle);
    expect(fetch.mock.calls.filter(([, init]) => (init?.method ?? 'GET') !== 'GET')).toEqual([]);
  });
});

describe('in English', () => {
  beforeEach(() => {
    setLanguage('en-GB', { remember: false });
  });

  test('the overdue course says Overdue and Was due, with a link Start', async () => {
    await renderMyCoursesPage();

    const item = await itemOf(overdue.courseTitle);
    expect(item).toHaveTextContent('Overdue');
    expect(item).toHaveTextContent('Was due 20/11/2026');
    expect(within(item).getByRole('link', { name: 'Start: Hygiene in der Apotheke' })).toHaveTextContent(/^Start$/);
  });

  test('a running course says Due, with a link Continue', async () => {
    await renderMyCoursesPage();

    const item = await itemOf(inProgress.courseTitle);
    expect(item).toHaveTextContent('In progress');
    expect(item).toHaveTextContent('Due 15/12/2026');
    expect(within(item).getByRole('link', { name: 'Continue: Notfallmanagement' })).toHaveTextContent(/^Continue$/);
  });

  test('a locked course and one that cannot be started say so in English', async () => {
    await renderMyCoursesPage();

    expect(await itemOf(locked.courseTitle)).toHaveTextContent(
      'The test is locked. Work through the content again to unlock it.',
    );
    expect(await itemOf(retired.courseTitle)).toHaveTextContent(
      'This course cannot be started at the moment: its version has been retired.',
    );
    expect(t('learning.myCourses.list.title')).toBe('Assigned courses');
  });
});
