import { expect, test } from 'vitest';
import { parseVerificationCode } from './verificationCode';

// The verification page (#54) reads the code from its address, /verify/:code, before it asks the host anything. A code
// the host could never have issued (VerificationCode: 24 to 64 characters of the Base64Url alphabet) is not sent: the
// page shows the same "Nicht prüfbar" the host's 404 gives, and a malformed link spends none of the rate limit every
// verifier shares (ADR 0026).

const issued = 'AbCdEfGhIjKlMnOpQrStUvWxYz012-_9';

test.each([
  ['a code as the host issues it, 32 characters', issued],
  ['the shortest code the host takes, 24 characters', 'A'.repeat(24)],
  ['the longest code the host takes, 64 characters', 'z'.repeat(64)],
  ['a code of digits, dashes and underscores only', '0123456789-_0123456789-_'],
])('%s is kept as it is', (_name, code) => {
  expect(issued).toHaveLength(32);
  expect(parseVerificationCode({ code })).toBe(code);
});

test.each([
  ['a code of 23 characters', 'A'.repeat(23)],
  ['a code of 65 characters', 'A'.repeat(65)],
  ['a code with +, which is Base64 but not Base64Url', `${issued.slice(0, 31)}+`],
  ['a code with /, which is Base64 but not Base64Url', `${issued.slice(0, 31)}/`],
  ['a code with an escaped space', `${issued.slice(0, 29)}%20`],
  ['a code with a space', `${issued.slice(0, 31)} `],
  ['a code with padding', `${issued.slice(0, 31)}=`],
  ['an empty code', ''],
])('%s is not sent', (_name, code) => {
  expect(parseVerificationCode({ code })).toBeUndefined();
});

test('an address without a code, or with a repeated one, has no code to send', () => {
  expect(parseVerificationCode({})).toBeUndefined();
  expect(parseVerificationCode({ code: [issued] })).toBeUndefined();
  expect(parseVerificationCode({ code: [issued, issued] })).toBeUndefined();
});
