import { render, screen, waitFor } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import type { EditorialChapterRequest } from '@/api/generated';
import { t } from '@/i18n';
import ChapterForm from './ChapterForm.vue';
import type { ChapterSubmission } from './chapterSchema';

// The form reads a request that names no kind as a text chapter: it is handed to onSave as it is, and nothing is
// uploaded first. The form's own schema always names a kind (the radio group starts on Text), so no kind cannot be
// produced through the fields; here the schema is the form's own with the kind taken off its request, and the form is
// driven as the other ChapterForm tests drive it.
vi.mock('./chapterSchema', async (importOriginal) => {
  const original = await importOriginal<typeof import('./chapterSchema')>();
  return {
    ...original,
    chapterSchema: (...args: Parameters<typeof original.chapterSchema>) =>
      original
        .chapterSchema(...args)
        .transform(({ request, upload }): ChapterSubmission => ({ request: { ...request, kind: undefined }, upload })),
  };
});

const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
const onSave = vi.fn<(request: EditorialChapterRequest) => Promise<boolean>>();
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();

beforeEach(() => {
  onSave.mockReset();
  onSave.mockResolvedValue(true);
  host.mockReset();
  vi.stubGlobal('fetch', host);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

test('a request without a kind and without a file is sent as a text chapter, with nothing uploaded', async () => {
  const user = userEvent.setup();
  render(ChapterForm, { props: { versionId, questions: [], onSave, saving: false } });

  await user.click(screen.getByRole('textbox', { name: t('editorial.chapterForm.title') }));
  await user.paste('Einleitung');
  await user.click(screen.getByRole('textbox', { name: t('editorial.chapterForm.text') }));
  await user.paste('# Willkommen');
  await user.click(screen.getByRole('button', { name: t('editorial.chapterForm.submit') }));

  await waitFor(() => expect(onSave).toHaveBeenCalledOnce());
  expect(onSave).toHaveBeenCalledWith({
    title: 'Einleitung',
    kind: undefined,
    text: '# Willkommen',
    file: null,
    durationSeconds: null,
  });
  expect(host).not.toHaveBeenCalled();
});
