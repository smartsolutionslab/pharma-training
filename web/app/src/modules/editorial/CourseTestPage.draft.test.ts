import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import { createMemoryHistory, RouterView } from 'vue-router';
import type { EditorialChapterDto, EditorialCourseVersionDto, EditorialQuestionDto } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import CourseTestPage from './CourseTestPage.vue';
import {
  approvedAi,
  handWritten,
  handout,
  introduction,
  refusal,
  unreviewedAi,
  versionId,
  versionOf,
} from './courseTestFixtures';

// The AI draft of the test (#25, S8 and T7; ADR 0008): asked for after a confirmation that says how many questions the
// AI drafts (the blueprint less the approved ones) and warns how many unreviewed questions it replaces, hand-written
// ones included (the owner's decision); while it runs the page says so and takes no other change; afterwards the
// version is read again and the chapters the AI left out are listed. Every refusal has a text of the page's own.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

const versionPath = `/api/editorial/course-versions/${versionId}`;
const draftPath = `${versionPath}/test-draft`;

/** Two questions the AI drafted about the introduction, as the host keeps them after the draft. */
const drafted: EditorialQuestionDto[] = [
  {
    ...unreviewedAi,
    id: '0198c1a0-0000-7000-8000-0000000000a7',
    position: 2,
    text: 'Womit desinfizieren Sie die Hände?',
  },
  {
    ...unreviewedAi,
    id: '0198c1a0-0000-7000-8000-0000000000a8',
    position: 3,
    type: 'SingleChoice',
    text: 'Wann waschen Sie die Hände?',
    options: [
      { id: '0198c1a0-0000-7000-8000-0000000000bb', text: 'Vor jedem Kontakt', correct: true },
      { id: '0198c1a0-0000-7000-8000-0000000000bc', text: 'Einmal am Tag', correct: false },
    ],
  },
];

let version: EditorialCourseVersionDto;
let drafting: () => Promise<Response>;

beforeEach(() => {
  version = versionOf('Draft');
  // By default the host drafts as it would: the approved question stays, the unreviewed ones are replaced, and the
  // PDF chapter is left out.
  drafting = async () => {
    version = { ...version, questions: [approvedAi, ...drafted] };
    return Response.json({
      draftedQuestionIds: drafted.map((question) => question.id),
      leftOutChapterIds: [handout.id],
    });
  };
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === versionPath) {
      return Response.json(version);
    }
    if (method === 'POST' && url.pathname === draftPath) {
      return drafting();
    }
    return new Response('', { status: 404 });
  });
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => host.mockReset());
afterEach(cleanup);

const drafts = () =>
  host.mock.calls.filter(
    ([input, init]) => init?.method === 'POST' && new URL(String(input), 'http://localhost').pathname === draftPath,
  );
const changes = () => host.mock.calls.filter(([, init]) => (init?.method ?? 'GET') !== 'GET');
const reads = () => host.mock.calls.filter(([, init]) => (init?.method ?? 'GET') === 'GET');

async function renderPage() {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/editorial/versions/${versionId}/test`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const rendered = render(CourseTestPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  await screen.findByRole('heading', { level: 1, name: t('editorial.test.heading', { title: version.title }) });
  return rendered;
}

type User = ReturnType<typeof userEvent.setup>;

/** The blueprint's section, which holds the draft. */
const blueprintSection = () =>
  within(
    screen.getByRole('heading', { level: 2, name: t('editorial.test.blueprint.title') }).closest('section') ??
      document.body,
  );
const startButton = () => blueprintSection().getByRole('button', { name: t('editorial.test.draft.start') });

/** The dialog that asks before the draft, an alert dialog or a dialog. */
const confirmation = () =>
  screen.queryByRole('alertdialog', { name: t('editorial.test.draft.confirm.title') }) ??
  screen.queryByRole('dialog', { name: t('editorial.test.draft.confirm.title') });

async function openConfirmation(user: User) {
  await user.click(startButton());
  await waitFor(() => expect(confirmation()).not.toBeNull());
  return within(confirmation() ?? document.body);
}

async function confirmDraft(user: User) {
  const dialog = await openConfirmation(user);
  await user.click(dialog.getByRole('button', { name: t('editorial.test.draft.confirm.submit') }));
}

const questionList = () => screen.getByRole('list', { name: t('editorial.test.questions.title') });
const cards = () => [...questionList().querySelectorAll(':scope > li')].map((card) => card as HTMLElement);

/** A control that is not offered: absent, or there but disabled. */
const unavailable = (control: HTMLElement | null) => control === null || control.hasAttribute('disabled');

/** The note under the button that lists the left-out chapters: the element around its sentence that holds a list. */
function leftOutNote(): HTMLElement | null {
  let note: HTMLElement | null = screen.queryByText(t('editorial.test.draft.leftOut'));
  while (note !== null && note.querySelector('ul') === null) {
    note = note.parentElement;
  }
  return note;
}

describe('the draft block', () => {
  test('a draft explains the AI draft in the blueprint’s section and offers it', async () => {
    await renderPage();

    expect(blueprintSection().getByText(t('editorial.test.draft.explain'))).toBeInTheDocument();
    expect(
      blueprintSection().getByText(
        'Die KI entwirft die fehlenden Fragen aus den Textkapiteln. Sie prüfen jede Frage, bevor sie gilt.',
      ),
    ).toBeInTheDocument();
    expect(startButton()).toBeEnabled();
    expect(blueprintSection().getByRole('button', { name: 'KI-Entwurf erstellen' })).toBe(startButton());
  });

  test('the draft is disabled without a text chapter, and says why', async () => {
    version = versionOf('Draft', { chapters: [handout], questions: [handWritten] });
    await renderPage();

    expect(startButton()).toBeDisabled();
    expect(startButton()).toHaveAccessibleDescription(t('editorial.test.draft.noTextChapters'));
    expect(startButton()).toHaveAccessibleDescription(
      'Die KI entwirft nur aus Textkapiteln. Legen Sie zuerst ein Textkapitel an.',
    );
  });

  test('the draft is disabled when the approved questions fill the blueprint, and says why', async () => {
    version = versionOf('Draft', { blueprint: { questions: 1, passingScorePercent: 80, validityMonths: 12 } });
    await renderPage();

    expect(startButton()).toBeDisabled();
    expect(startButton()).toHaveAccessibleDescription(t('editorial.test.draft.nothingToDraft'));
    expect(startButton()).toHaveAccessibleDescription(
      'Die geprüften Fragen erfüllen die Vorgaben bereits; es gibt nichts zu entwerfen.',
    );
  });

  test.each(['InReview', 'Published', 'Superseded', 'Retired'] as const)(
    'a version %s offers no draft',
    async (status) => {
      version = versionOf(status);
      await renderPage();

      expect(screen.queryByRole('button', { name: t('editorial.test.draft.start') })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'KI-Entwurf erstellen' })).not.toBeInTheDocument();
    },
  );
});

describe('the confirmation', () => {
  test('the draft asks for confirmation before calling the host, and cancelling sends nothing', async () => {
    const user = userEvent.setup();
    await renderPage();

    const dialog = await openConfirmation(user);
    expect(
      screen.queryByRole('alertdialog', { name: 'KI-Entwurf erstellen?' }) ??
        screen.queryByRole('dialog', { name: 'KI-Entwurf erstellen?' }),
    ).not.toBeNull();
    expect(drafts()).toHaveLength(0);
    await user.click(dialog.getByRole('button', { name: t('editorial.cancel') }));

    await waitFor(() => expect(confirmation()).toBeNull());
    expect(changes()).toHaveLength(0);
  });

  test('the confirmation says how many questions are drafted: the blueprint less the approved ones', async () => {
    const user = userEvent.setup();
    await renderPage();

    const dialog = await openConfirmation(user);

    expect(dialog.getByText(t('editorial.test.draft.confirm.requested', { count: 9 }, 9))).toBeInTheDocument();
    expect(dialog.getByText('Die KI entwirft 9 Fragen. Geprüfte Fragen bleiben erhalten.')).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Entwurf erstellen' })).toBeEnabled();
  });

  test('one question to draft is said in the singular', async () => {
    const user = userEvent.setup();
    version = versionOf('Draft', { blueprint: { questions: 2, passingScorePercent: 80, validityMonths: 12 } });
    await renderPage();

    const dialog = await openConfirmation(user);

    expect(dialog.getByText('Die KI entwirft eine Frage. Geprüfte Fragen bleiben erhalten.')).toBeInTheDocument();
  });

  test('it warns that 2 unreviewed questions will be replaced, hand-written ones included', async () => {
    const user = userEvent.setup();
    await renderPage();

    const dialog = await openConfirmation(user);

    const warning = dialog.getByText(t('editorial.test.draft.confirm.replaces', { count: 2 }, 2));
    expect(warning).toHaveTextContent('2 ungeprüfte Fragen werden ersetzt, auch von Hand geschriebene.');
    // A warning in the dialog's description, not an alert of its own.
    expect(warning.closest('[role="alert"]')).toBeNull();
  });

  test('it warns about one unreviewed hand-written question in the singular', async () => {
    const user = userEvent.setup();
    version = versionOf('Draft', { questions: [approvedAi, handWritten] });
    await renderPage();

    const dialog = await openConfirmation(user);

    expect(
      dialog.getByText('Eine ungeprüfte Frage wird ersetzt, auch wenn sie von Hand geschrieben ist.'),
    ).toBeInTheDocument();
  });

  test('with no unreviewed question it gives no warning', async () => {
    const user = userEvent.setup();
    version = versionOf('Draft', { chapters: [introduction], questions: [approvedAi] });
    await renderPage();

    const dialog = await openConfirmation(user);

    expect(dialog.getByText('Die KI entwirft 9 Fragen. Geprüfte Fragen bleiben erhalten.')).toBeInTheDocument();
    expect(dialog.queryByText(/ungeprüfte Frage/)).not.toBeInTheDocument();
  });

  test('a version with a PDF chapter is told PDF and video chapters are not read', async () => {
    const user = userEvent.setup();
    await renderPage();

    const dialog = await openConfirmation(user);

    expect(dialog.getByText(t('editorial.test.draft.confirm.leftOut'))).toBeInTheDocument();
    expect(
      dialog.getByText('PDF- und Videokapitel liest die KI nicht; Fragen dazu schreiben Sie von Hand.'),
    ).toBeInTheDocument();
  });

  test('a version of text chapters only is not told about PDF and video chapters', async () => {
    const user = userEvent.setup();
    version = versionOf('Draft', { chapters: [introduction], questions: [approvedAi, unreviewedAi] });
    await renderPage();

    const dialog = await openConfirmation(user);

    expect(dialog.getByText('Die KI entwirft 9 Fragen. Geprüfte Fragen bleiben erhalten.')).toBeInTheDocument();
    expect(dialog.queryByText(/PDF- und Videokapitel/)).not.toBeInTheDocument();
  });

  test('the confirmation speaks English when switched', async () => {
    const user = userEvent.setup();
    await renderPage();

    setLanguage('en-GB', { remember: false });
    // Wait for the page to have re-rendered in English before finding the button by its English name.
    await user.click(await screen.findByRole('button', { name: 'Draft with AI' }));

    const dialog = within(
      await waitFor(() => {
        const found =
          screen.queryByRole('alertdialog', { name: 'Draft with AI?' }) ??
          screen.queryByRole('dialog', { name: 'Draft with AI?' });
        expect(found).not.toBeNull();
        return found ?? document.body;
      }),
    );
    expect(dialog.getByText('The AI drafts 9 questions. Approved questions stay.')).toBeInTheDocument();
    expect(
      dialog.getByText('2 unreviewed questions will be replaced, including those written by hand.'),
    ).toBeInTheDocument();
    expect(
      dialog.getByText('The AI does not read PDF and video chapters; write their questions by hand.'),
    ).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Draft' })).toBeInTheDocument();
  });
});

describe('the draft', () => {
  test('while the draft runs the page says so and offers no other change', async () => {
    const user = userEvent.setup();
    let answer: (response: Response) => void = () => undefined;
    drafting = () =>
      new Promise<Response>((resolve) => {
        answer = resolve;
      });
    await renderPage();

    await confirmDraft(user);

    const running = await screen.findByText(t('editorial.test.draft.running'));
    expect(running.closest('[role="status"]')).not.toBeNull();
    expect(running).toHaveTextContent(
      'Die KI entwirft den Test. Das kann einige Minuten dauern; bitte lassen Sie die Seite geöffnet.',
    );
    await waitFor(() => expect(confirmation()).toBeNull());
    expect(drafts()).toHaveLength(1);
    expect(startButton()).toBeDisabled();
    // No other change while it runs: the draft's save would lose the race after the model was paid.
    expect(unavailable(screen.queryByRole('button', { name: t('editorial.test.blueprint.edit') }))).toBe(true);
    expect(unavailable(screen.queryByRole('button', { name: t('editorial.test.questions.add') }))).toBe(true);
    for (const position of [1, 2, 3]) {
      for (const key of [
        'editorial.test.questions.edit',
        'editorial.test.questions.remove',
        'editorial.test.questions.approve',
      ] as const) {
        expect(unavailable(screen.queryByRole('button', { name: t(key, { position }) }))).toBe(true);
      }
    }
    // And no way to cancel it: the model call would go on.
    expect(screen.queryByRole('button', { name: t('editorial.cancel') })).not.toBeInTheDocument();

    answer(Response.json({ draftedQuestionIds: [], leftOutChapterIds: [] }));

    await waitFor(() => expect(screen.queryByText(t('editorial.test.draft.running'))).not.toBeInTheDocument());
    await waitFor(() => expect(startButton()).toBeEnabled());
    expect(screen.getByRole('button', { name: t('editorial.test.blueprint.edit') })).toBeEnabled();
    expect(screen.getByRole('button', { name: t('editorial.test.questions.edit', { position: 2 }) })).toBeEnabled();
  });

  test('after the draft the approved question is still there and the drafted ones are marked unreviewed', async () => {
    const user = userEvent.setup();
    await renderPage();

    await confirmDraft(user);

    await waitFor(() => expect(notify.success).toHaveBeenCalledWith(t('editorial.test.draft.done', { count: 2 }, 2)));
    expect(notify.success).toHaveBeenCalledWith('Die KI hat 2 Fragen entworfen. Bitte prüfen Sie sie.');
    expect(drafts()).toHaveLength(1);
    expect(drafts()[0]?.[1]?.body ?? undefined).toBeUndefined();
    await waitFor(() => expect(cards()).toHaveLength(3));
    await waitFor(() => expect(cards()[1]).toHaveTextContent('Womit desinfizieren Sie die Hände?'));
    const [kept, first, second] = cards();
    expect(kept).toHaveTextContent(approvedAi.text);
    expect(within(kept ?? document.body).getByText('Geprüft')).toBeInTheDocument();
    for (const card of [first, second]) {
      expect(within(card ?? document.body).getByText('Ungeprüft')).toBeInTheDocument();
      expect(within(card ?? document.body).getByText('KI-entworfen, noch nicht geprüft')).toBeInTheDocument();
    }
    expect(second).toHaveTextContent('Wann waschen Sie die Hände?');
    expect(screen.queryByText(handWritten.text)).not.toBeInTheDocument();
    expect(notify.failure).not.toHaveBeenCalled();
  });

  test('one drafted question is reported in the singular, in English when switched', async () => {
    const user = userEvent.setup();
    drafting = async () => Response.json({ draftedQuestionIds: [drafted[0]?.id], leftOutChapterIds: [] });
    await renderPage();

    setLanguage('en-GB', { remember: false });
    await screen.findByRole('button', { name: 'Draft with AI' });
    await user.click(startButton());
    const dialog = await waitFor(() => {
      expect(confirmation()).not.toBeNull();
      return within(confirmation() ?? document.body);
    });
    await user.click(dialog.getByRole('button', { name: 'Draft' }));

    await waitFor(() => expect(notify.success).toHaveBeenCalledWith('The AI drafted one question. Please review it.'));
  });

  test('the chapters the draft left out are listed by title, until dismissed', async () => {
    const user = userEvent.setup();
    const video: EditorialChapterDto = {
      id: '0198c1a0-0000-7000-8000-0000000000e3',
      position: 3,
      title: 'Video: <b>Hände</b> richtig waschen',
      kind: 'Video',
      text: null,
      file: `course-versions/${versionId}/0198c1a0-0000-7000-8000-0000000000f3.mp4`,
      durationSeconds: 95,
      pdfText: null,
    };
    version = versionOf('Draft', { chapters: [introduction, handout, video] });
    drafting = async () => {
      version = { ...version, questions: [approvedAi, ...drafted] };
      return Response.json({
        draftedQuestionIds: drafted.map((question) => question.id),
        // A chapter the version does not have is skipped.
        leftOutChapterIds: [handout.id, '0198c1a0-0000-7000-8000-0000000000ef', video.id],
      });
    };
    const { container } = await renderPage();

    await confirmDraft(user);

    await waitFor(() => expect(leftOutNote()).not.toBeNull());
    expect(screen.getByText(t('editorial.test.draft.leftOut'))).toHaveTextContent(
      'Zu diesen Kapiteln hat die KI keine Fragen entworfen; schreiben Sie sie von Hand:',
    );
    const list = within(leftOutNote()?.querySelector('ul') ?? document.body);
    expect(list.getAllByRole('listitem').map((item) => item.textContent?.trim())).toEqual([
      'Handout',
      'Video: <b>Hände</b> richtig waschen',
    ]);
    // A title is text, never markup.
    expect(container.querySelector('b')).toBeNull();

    await user.click(screen.getByRole('button', { name: t('editorial.test.draft.dismiss') }));

    await waitFor(() => expect(leftOutNote()).toBeNull());
    expect(screen.queryByRole('button', { name: 'Hinweis schließen' })).not.toBeInTheDocument();
  });

  test('a draft that left nothing out lists nothing', async () => {
    const user = userEvent.setup();
    drafting = async () => {
      version = { ...version, questions: [approvedAi, ...drafted] };
      return Response.json({ draftedQuestionIds: drafted.map((question) => question.id), leftOutChapterIds: [] });
    };
    await renderPage();

    await confirmDraft(user);

    await waitFor(() => expect(notify.success).toHaveBeenCalledOnce());
    expect(screen.queryByText(t('editorial.test.draft.leftOut'))).not.toBeInTheDocument();
  });

  test('an answer of the host that is not the draft’s shape is not trusted: nothing of it is shown', async () => {
    const user = userEvent.setup();
    drafting = async () =>
      Response.json({
        draftedQuestionIds: 'alle',
        leftOutChapterIds: [42, '<img src=x onerror="window.hacked = true">', { id: handout.id }],
      });
    const { container } = await renderPage();

    await confirmDraft(user);

    await waitFor(() => expect(screen.queryByText(t('editorial.test.draft.running'))).not.toBeInTheDocument());
    await waitFor(() => expect(reads().length).toBeGreaterThan(1));
    expect(drafts()).toHaveLength(1);
    expect(container.querySelector('img')).toBeNull();
    expect(document.querySelector('img')).toBeNull();
    expect(screen.queryByText(t('editorial.test.draft.leftOut'))).not.toBeInTheDocument();
    expect(screen.queryByText(/onerror/)).not.toBeInTheDocument();
    // Whether the host drafted is not known: the page says so, never that it failed.
    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(t('editorial.test.draft.errors.unknown')));
    expect(notify.failure).toHaveBeenCalledWith(
      'Ob der KI-Entwurf übernommen wurde, ist nicht bekannt. Die Fragen werden neu geladen; prüfen Sie sie, bevor Sie erneut einen Entwurf erstellen.',
    );
    expect(notify.failure).toHaveBeenCalledOnce();
    expect(notify.failure).not.toHaveBeenCalledWith(t('editorial.test.draft.errors.failed'));
    expect(notify.success).not.toHaveBeenCalled();
    // The page is still there.
    expect(
      screen.getByRole('heading', { level: 1, name: t('editorial.test.heading', { title: version.title }) }),
    ).toBeInTheDocument();
  });
});

describe('a refused or failed draft', () => {
  test.each([
    [
      '503 model-not-configured',
      () => refusal(503, 'model-not-configured', 'No language model is configured.'),
      'Es ist kein Sprachmodell eingerichtet; KI-Funktionen stehen nicht zur Verfügung. Wenden Sie sich an den Betrieb der Plattform.',
    ],
    [
      '429',
      () => new Response(JSON.stringify({ title: 'Too Many Requests', status: 429 }), { status: 429 }),
      'Sie haben in kurzer Zeit zu viele KI-Entwürfe angefordert. Versuchen Sie es später erneut.',
    ],
    // The per-editor limit (#174) is told from any other 429 by its code; the host's title is never shown.
    [
      '429 model-limit-reached',
      () =>
        refusal(429, 'editorial.model-limit-reached', 'You have reached your limit of AI requests; try again later.'),
      'Sie haben Ihr Limit für KI-Anfragen erreicht. Versuchen Sie es später erneut.',
    ],
    [
      '502 ai-draft-malformed',
      () => refusal(502, 'editorial.ai-draft-malformed', 'The answer could not be read.'),
      'Die Antwort der KI war unbrauchbar; es wurde nichts geändert. Versuchen Sie es erneut.',
    ],
    [
      '409 no-text-chapters',
      () => refusal(409, 'editorial.no-text-chapters', 'Keine Textkapitel.'),
      'Die KI entwirft nur aus Textkapiteln. Legen Sie zuerst ein Textkapitel an.',
    ],
    [
      '409 nothing-to-draft',
      () => refusal(409, 'editorial.nothing-to-draft', 'Nichts zu entwerfen.'),
      'Die geprüften Fragen erfüllen die Vorgaben bereits; es gibt nichts zu entwerfen.',
    ],
    [
      '422 too-much-content-to-draft',
      () => refusal(422, 'editorial.too-much-content-to-draft', 'Zu viel Inhalt.'),
      'Die Textkapitel sind zu lang für einen KI-Entwurf. Kürzen Sie sie oder schreiben Sie die Fragen von Hand.',
    ],
    [
      '409 course-version-changed',
      () => refusal(409, 'editorial.course-version-changed', 'Die Version wurde geändert.'),
      'Die Version wurde inzwischen geändert; der Entwurf wurde verworfen. Versuchen Sie es erneut.',
    ],
    [
      '409 not-a-draft',
      () => refusal(409, 'editorial.not-a-draft', 'Kein Entwurf.'),
      'Diese Version ist kein Entwurf mehr; ihr Test kann nicht neu entworfen werden.',
    ],
    [
      '504',
      () => new Response('', { status: 504 }),
      'Der KI-Entwurf hat zu lange gedauert. Die Fragen werden neu geladen; prüfen Sie, ob er übernommen wurde.',
    ],
    ['500', () => new Response('', { status: 500 }), 'Der KI-Entwurf ist fehlgeschlagen. Versuchen Sie es erneut.'],
    // An edge's 503 or 502 is no answer of the host's: it carries no code of its own, and the draft failed.
    [
      '503 from the edge',
      () => new Response('', { status: 503 }),
      'Der KI-Entwurf ist fehlgeschlagen. Versuchen Sie es erneut.',
    ],
    [
      '502 from the edge',
      () => new Response('Bad Gateway', { status: 502, headers: { 'Content-Type': 'text/plain' } }),
      'Der KI-Entwurf ist fehlgeschlagen. Versuchen Sie es erneut.',
    ],
  ])(
    'a %s is reported in the page’s words, the version read again and the questions unchanged',
    async (_answer, answer, reported) => {
      const user = userEvent.setup();
      drafting = async () => answer();
      await renderPage();

      await confirmDraft(user);

      await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(reported));
      expect(notify.failure).toHaveBeenCalledOnce();
      expect(notify.success).not.toHaveBeenCalled();
      await waitFor(() => expect(reads().length).toBeGreaterThan(1));
      await waitFor(() => expect(screen.queryByText(t('editorial.test.draft.running'))).not.toBeInTheDocument());
      expect(cards()).toHaveLength(3);
      expect(screen.getByText(handWritten.text)).toBeInTheDocument();
      expect(screen.queryByText(t('editorial.test.draft.leftOut'))).not.toBeInTheDocument();
    },
  );

  test('a draft whose answer never came is reported as unknown, not as failed, and the questions reloaded', async () => {
    const user = userEvent.setup();
    drafting = async () => {
      throw new TypeError('Failed to fetch');
    };
    await renderPage();

    await confirmDraft(user);

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(t('editorial.test.draft.errors.unknown')));
    expect(notify.failure).toHaveBeenCalledWith(
      'Ob der KI-Entwurf übernommen wurde, ist nicht bekannt. Die Fragen werden neu geladen; prüfen Sie sie, bevor Sie erneut einen Entwurf erstellen.',
    );
    expect(notify.failure).toHaveBeenCalledOnce();
    await waitFor(() => expect(reads().length).toBeGreaterThan(1));
    await waitFor(() => expect(startButton()).toBeEnabled());
  });

  test('a 200 whose body is not JSON is reported as unknown, not as failed, and the questions reloaded', async () => {
    const user = userEvent.setup();
    drafting = async () =>
      new Response('<html><body>Proxy</body></html>', { status: 200, headers: { 'Content-Type': 'text/html' } });
    await renderPage();

    await confirmDraft(user);

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(t('editorial.test.draft.errors.unknown')));
    expect(notify.failure).toHaveBeenCalledWith(
      'Ob der KI-Entwurf übernommen wurde, ist nicht bekannt. Die Fragen werden neu geladen; prüfen Sie sie, bevor Sie erneut einen Entwurf erstellen.',
    );
    expect(notify.failure).toHaveBeenCalledOnce();
    expect(notify.success).not.toHaveBeenCalled();
    await waitFor(() => expect(reads().length).toBeGreaterThan(1));
    await waitFor(() => expect(screen.queryByText(t('editorial.test.draft.running'))).not.toBeInTheDocument());
  });

  test('an unknown outcome is reported in English when switched', async () => {
    const user = userEvent.setup();
    drafting = async () => {
      throw new TypeError('Failed to fetch');
    };
    await renderPage();

    setLanguage('en-GB', { remember: false });
    await screen.findByRole('button', { name: 'Draft with AI' });
    await user.click(startButton());
    await waitFor(() => expect(confirmation()).not.toBeNull());
    await user.click(within(confirmation() ?? document.body).getByRole('button', { name: 'Draft' }));

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith(
        'It is not known whether the AI draft was applied. The questions are reloaded; check them before you create another draft.',
      ),
    );
  });

  test('a 503 is reported in English when switched', async () => {
    const user = userEvent.setup();
    drafting = async () => refusal(503, 'model-not-configured', 'No language model is configured.');
    await renderPage();

    setLanguage('en-GB', { remember: false });
    await screen.findByRole('button', { name: 'Draft with AI' });
    await user.click(startButton());
    await waitFor(() => expect(confirmation()).not.toBeNull());
    await user.click(within(confirmation() ?? document.body).getByRole('button', { name: 'Draft' }));

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith(
        "No language model is set up, so AI features are not available. Contact the platform's operators.",
      ),
    );
  });

  test('a model limit is reported in English when switched', async () => {
    const user = userEvent.setup();
    drafting = async () =>
      refusal(429, 'editorial.model-limit-reached', 'You have reached your limit of AI requests; try again later.');
    await renderPage();

    setLanguage('en-GB', { remember: false });
    await screen.findByRole('button', { name: 'Draft with AI' });
    await user.click(startButton());
    await waitFor(() => expect(confirmation()).not.toBeNull());
    await user.click(within(confirmation() ?? document.body).getByRole('button', { name: 'Draft' }));

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith('You have reached your limit for AI requests. Try again later.'),
    );
    expect(notify.failure).not.toHaveBeenCalledWith('You have reached your limit of AI requests; try again later.');
  });
});

describe('one draft at a time', () => {
  test('a double-click on the confirmation sends exactly one draft', async () => {
    const user = userEvent.setup();
    let answer: (response: Response) => void = () => undefined;
    drafting = () =>
      new Promise<Response>((resolve) => {
        answer = resolve;
      });
    await renderPage();

    const dialog = await openConfirmation(user);
    await user.dblClick(dialog.getByRole('button', { name: t('editorial.test.draft.confirm.submit') }));

    await screen.findByText(t('editorial.test.draft.running'));
    expect(drafts()).toHaveLength(1);
    answer(Response.json({ draftedQuestionIds: [], leftOutChapterIds: [] }));
    await waitFor(() => expect(startButton()).toBeEnabled());
    expect(drafts()).toHaveLength(1);
  });

  test('the confirmation’s button is disabled once pressed', async () => {
    let answer: (response: Response) => void = () => undefined;
    drafting = () =>
      new Promise<Response>((resolve) => {
        answer = resolve;
      });
    const user = userEvent.setup();
    await renderPage();

    const dialog = await openConfirmation(user);
    const submit = dialog.getByRole('button', { name: t('editorial.test.draft.confirm.submit') });
    // A press, without letting the page render in between: the button itself refuses a second one.
    submit.click();
    submit.click();

    await waitFor(() => expect(drafts()).toHaveLength(1));
    expect(submit.isConnected ? submit.hasAttribute('disabled') : true).toBe(true);
    answer(Response.json({ draftedQuestionIds: [], leftOutChapterIds: [] }));
    await waitFor(() => expect(startButton()).toBeEnabled());
    expect(drafts()).toHaveLength(1);
  });

  test('the draft cannot be started while an approval of the page is running', async () => {
    const user = userEvent.setup();
    let approved: (response: Response) => void = () => undefined;
    const fallback = host.getMockImplementation();
    host.mockImplementation(async (input, init) => {
      const url = new URL(String(input), 'http://localhost');
      if (init?.method === 'POST' && url.pathname.endsWith('/approve')) {
        return new Promise<Response>((resolve) => {
          approved = resolve;
        });
      }
      return fallback === undefined ? new Response('', { status: 404 }) : fallback(input, init);
    });
    await renderPage();
    expect(startButton()).toBeEnabled();

    await user.click(screen.getByRole('button', { name: t('editorial.test.questions.approve', { position: 2 }) }));

    await waitFor(() => expect(startButton()).toBeDisabled());
    expect(drafts()).toHaveLength(0);
    approved(Response.json({ value: null }));
    await waitFor(() => expect(startButton()).toBeEnabled());
  });
});

describe('the running draft outlives the page', () => {
  /** The app's router view, so leaving the test page unmounts it, as in the app. */
  const App = defineComponent({ render: () => h(RouterView) });

  test('leaving the page while the draft runs and coming back still shows it running, until it settles', async () => {
    const user = userEvent.setup();
    let answer: (response: Response) => void = () => undefined;
    drafting = () =>
      new Promise<Response>((resolve) => {
        answer = resolve;
      });
    const router = createAppRouter(createMemoryHistory());
    await router.push(`/editorial/versions/${versionId}/test`);
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
    render(App, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
    await screen.findByRole('heading', { level: 1, name: t('editorial.test.heading', { title: version.title }) });

    await confirmDraft(user);
    await screen.findByText(t('editorial.test.draft.running'));

    await router.push(`/editorial/versions/${versionId}`);
    await waitFor(() =>
      expect(
        screen.queryByRole('heading', { level: 1, name: t('editorial.test.heading', { title: version.title }) }),
      ).not.toBeInTheDocument(),
    );
    await router.push(`/editorial/versions/${versionId}/test`);
    await screen.findByRole('heading', { level: 1, name: t('editorial.test.heading', { title: version.title }) });

    expect(await screen.findByText(t('editorial.test.draft.running'))).toBeInTheDocument();
    expect(startButton()).toBeDisabled();
    expect(drafts()).toHaveLength(1);

    answer(Response.json({ draftedQuestionIds: [], leftOutChapterIds: [] }));

    await waitFor(() => expect(screen.queryByText(t('editorial.test.draft.running'))).not.toBeInTheDocument());
    await waitFor(() => expect(startButton()).toBeEnabled());
  });
});

test('the done report counts in the page’s number format', async () => {
  const user = userEvent.setup();
  const many = Array.from({ length: 1000 }, (_, index) => `0198c1a0-0000-7000-8002-${String(index).padStart(12, '0')}`);
  drafting = async () => Response.json({ draftedQuestionIds: many, leftOutChapterIds: [] });
  await renderPage();

  await confirmDraft(user);

  await waitFor(() => expect(notify.success).toHaveBeenCalledOnce());
  expect(notify.success).toHaveBeenCalledWith('Die KI hat 1.000 Fragen entworfen. Bitte prüfen Sie sie.');
});
