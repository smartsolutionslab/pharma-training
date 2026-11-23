import { describe, expect, test } from 'vitest';
import { setLanguage, t } from '@/i18n';
import { returnReasonSchema } from './returnReasonSchema';

// The return dialog's schema (#26, ADR 0013): the reason trimmed, 1 to 1000 UTF-16 units, plain text: no control
// character but tab, line feed and carriage return, no format, separator, private-use or unassigned character and no
// lone surrogate. Markup is text like any other and kept as it is. Each message is a key, rendered with t where shown.

/** The messages the schema gives for the reason. */
function refusals(reason: unknown): string[] {
  const result = returnReasonSchema.safeParse({ reason });
  return result.success ? [] : result.error.issues.map(({ message }) => message);
}

describe('accepts', () => {
  test('500 emoji, 1000 UTF-16 code units as the host counts them', () => {
    expect(refusals('\u{1F600}'.repeat(500))).toEqual([]);
  });

  test('a reason, and hands it over trimmed', () => {
    const result = returnReasonSchema.safeParse({ reason: '  Kapitel 2 fehlt die Quelle.\n ' });

    expect(result.success).toBe(true);
    expect(result.data).toEqual({ reason: 'Kapitel 2 fehlt die Quelle.' });
  });

  test('1000 characters once trimmed', () => {
    const reason = 'a'.repeat(1000);

    const result = returnReasonSchema.safeParse({ reason: `   ${reason}  ` });

    expect(result.success).toBe(true);
    expect(result.data).toEqual({ reason });
  });

  test('tabs, line feeds and carriage returns inside the reason', () => {
    const reason = 'Erstens:\tQuelle fehlt.\nZweitens:\r\nFrage 3 ist doppelt.';

    expect(returnReasonSchema.safeParse({ reason }).data).toEqual({ reason });
  });

  test('markup, kept as text', () => {
    const reason = '<b>Kapitel 2</b> & <script>alert(1)</script> [Link](javascript:x)';

    expect(returnReasonSchema.safeParse({ reason }).data).toEqual({ reason });
  });

  test('umlauts and characters beyond the basic plane', () => {
    const reason = 'Überarbeiten: Größe 𝔸 und 😀';

    expect(returnReasonSchema.safeParse({ reason }).data).toEqual({ reason });
  });
});

describe('refuses', () => {
  test('an empty reason as required', () => {
    expect(refusals('')).toEqual(['editorial.workflow.returnForm.reasonRequired']);
  });

  test('a reason of blanks as required', () => {
    expect(refusals(' \n\t  ')).toEqual(['editorial.workflow.returnForm.reasonRequired']);
  });

  test('1001 characters once trimmed as too long', () => {
    expect(refusals(` ${'a'.repeat(1001)} `)).toEqual(['editorial.workflow.returnForm.reasonTooLong']);
  });

  // The host counts UTF-16 code units: an emoji outside the BMP is two.
  test('501 emoji, 1002 UTF-16 code units, as too long', () => {
    expect(refusals('\u{1F600}'.repeat(501))).toEqual(['editorial.workflow.returnForm.reasonTooLong']);
  });

  test.each([
    ['a zero-width space alone (Cf, which trimming keeps)', '\u200B'],
    ['a bell (Cc)', 'Bitte \u0007 überarbeiten'],
    ['a NUL (Cc)', 'Bitte\u0000überarbeiten'],
    ['a lone high surrogate (Cs)', 'Bitte \uD800 überarbeiten'],
    ['a lone low surrogate (Cs)', 'Bitte \uDC00 überarbeiten'],
    ['a right-to-left override (Cf)', 'Bitte \u202E überarbeiten'],
    ['a line separator (Zl)', 'Bitte\u2028überarbeiten'],
    ['a paragraph separator (Zp)', 'Bitte\u2029überarbeiten'],
    ['a private-use character (Co)', 'Bitte \uE000 überarbeiten'],
    ['an unassigned code point (Cn)', 'Bitte \u0378 überarbeiten'],
    ['a DEL (Cc)', 'Bitte \u007F überarbeiten'],
  ])('%s as not allowed', (_name, reason) => {
    expect(refusals(reason)).toEqual(['editorial.workflow.returnForm.reasonInvalid']);
  });
});

// The return dialog's texts, in both languages.
const texts: [string, Record<string, number>, string, string][] = [
  ['editorial.workflow.returnForm.title', {}, 'Version zurückgeben', 'Return the version to draft'],
  ['editorial.workflow.returnForm.confirm', {}, 'Zurückgeben', 'Return to draft'],
  [
    'editorial.workflow.returnForm.description',
    {},
    'Die Version wird wieder ein Entwurf. Beschreiben Sie, was überarbeitet werden muss.',
    'The version becomes a draft again. Describe what needs to be reworked.',
  ],
  ['editorial.workflow.returnForm.reason', {}, 'Grund', 'Reason'],
  [
    'editorial.workflow.returnForm.hint',
    {},
    'Bitte nennen Sie im Grund keine Personen, sondern beschreiben Sie, was an der Version zu ändern ist.',
    'Please do not name people in the reason; describe what needs to change in the version.',
  ],
  ['editorial.workflow.returnForm.reasonRequired', {}, 'Geben Sie einen Grund an.', 'Please give a reason.'],
  [
    'editorial.workflow.returnForm.reasonTooLong',
    { max: 1000 },
    'Der Grund darf höchstens 1000 Zeichen lang sein.',
    'The reason can be at most 1000 characters long.',
  ],
  [
    'editorial.workflow.returnForm.reasonInvalid',
    {},
    'Der Grund enthält unzulässige Steuer- oder Formatzeichen.',
    'The reason contains control or format characters that are not allowed.',
  ],
];

describe('says', () => {
  test.each(texts)('%s in German', (key, values, german) => {
    expect(t(key, values)).toBe(german);
  });

  test.each(texts)('%s in English', (key, values, _german, english) => {
    setLanguage('en-GB', { remember: false });

    expect(t(key, values)).toBe(english);
  });
});
