import { expect, test } from 'vitest';
import { type MessageKey, setLanguage, t } from '@/i18n';

// The chapter player's texts (#44), each a key in both catalogs (ADR 0007), German with Sie. The keys of the video, the
// PDF and next/previous come with their own tasks.
const texts: Record<string, { de: string; en: string }> = {
  'modules.learning.details.player.title': { de: 'Schulung', en: 'Course' },
  'learning.player.loadFailed': {
    de: 'Die Schulung konnte nicht geladen werden.',
    en: 'The course could not be loaded.',
  },
  'learning.player.notFound': { de: 'Diese Schulung gibt es nicht.', en: 'This course does not exist.' },
  'learning.player.notReady': {
    de: 'Die Schulung wird gerade bereitgestellt. Versuchen Sie es in einem Moment erneut.',
    en: 'The course is being made available. Please try again in a moment.',
  },
  'learning.player.noIdentity': {
    de: 'Ihr Konto ist unvollständig. Bitte melden Sie sich erneut an.',
    en: 'Your account is incomplete. Please sign in again.',
  },
  'learning.player.empty': { de: 'Diese Schulung hat keine Kapitel.', en: 'This course has no chapters.' },
  'learning.player.chapters': { de: 'Kapitel', en: 'Chapters' },
  'learning.player.state.done': { de: 'Erledigt', en: 'Done' },
  'learning.player.state.progress': { de: '{percent} bearbeitet', en: '{percent} done' },
  'learning.player.state.open': { de: 'Offen', en: 'Not started' },
  'learning.player.chapterDone': { de: 'Kapitel „{title}“ erledigt.', en: 'Chapter “{title}” done.' },
  'learning.player.contentCompleted': {
    de: 'Sie haben alle Kapitel bearbeitet. Der Test ist jetzt freigeschaltet.',
    en: 'You have worked through all chapters. The test is now unlocked.',
  },
  'learning.player.text.hint': {
    de: 'Scrollen Sie bis zum Ende, um das Kapitel abzuschließen.',
    en: 'Scroll to the end to complete the chapter.',
  },
  'learning.player.progress.failed': {
    de: 'Ihr Fortschritt konnte nicht gespeichert werden. Er wird erneut gesendet.',
    en: 'Your progress could not be saved. It will be sent again.',
  },
  'learning.player.progress.saveNow': { de: 'Jetzt speichern', en: 'Save now' },
  'learning.player.progress.finished': {
    de: 'Dieser Lerndurchlauf ist bereits abgeschlossen. Weiterer Fortschritt wird nicht gespeichert.',
    en: 'This learning session is already finished. Further progress is not saved.',
  },
};

// A table of keys, some not in the catalogs yet: each is looked up as a key, and an unknown one comes back as itself.
const message = (key: string, values: Record<string, string> = {}) => t(key as MessageKey, values);
// The placeholders stay placeholders here, so the message reads as the catalog writes it.
const placeholders = { percent: '{percent}', title: '{title}' };

test("the chapter player's texts are in the German catalog", () => {
  setLanguage('de-DE', { remember: false });

  expect(Object.fromEntries(Object.keys(texts).map((key) => [key, message(key, placeholders)]))).toEqual(
    Object.fromEntries(Object.entries(texts).map(([key, { de }]) => [key, de])),
  );
});

test("the chapter player's texts are in the English catalog", () => {
  setLanguage('en-GB', { remember: false });

  expect(Object.fromEntries(Object.keys(texts).map((key) => [key, message(key, placeholders)]))).toEqual(
    Object.fromEntries(Object.entries(texts).map(([key, { en }]) => [key, en])),
  );
});

test("a chapter's state and announcement fill in the percent and the title", () => {
  expect(t('learning.player.state.progress', { percent: '40 %' })).toBe('40 % bearbeitet');
  expect(t('learning.player.chapterDone', { title: 'Einleitung' })).toBe('Kapitel „Einleitung“ erledigt.');
  setLanguage('en-GB', { remember: false });
  expect(t('learning.player.state.progress', { percent: '40%' })).toBe('40% done');
  expect(t('learning.player.chapterDone', { title: 'Introduction' })).toBe('Chapter “Introduction” done.');
});
