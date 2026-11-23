import { expect, test } from 'vitest';
import { i18n } from '@/i18n';
import { rateFigure } from './matrixRate';

// The matrix's compliance rate (#276): "100 %" means everyone is compliant and "0 %" nobody. Near either end the figure
// shows one decimal, rounded towards the middle, so 200 of 201 never reads 100 % and 1 of 1000 never reads 0 %.

const shown = (rate: number, locale: 'de-DE' | 'en-GB' = 'de-DE') => {
  const { value, format } = rateFigure(rate);
  return i18n.global.n(value, format, locale);
};

test.each([
  [1, '100 %'],
  [0, '0 %'],
  [0.5, '50 %'],
  [0.8, '80 %'],
  [0.984, '98 %'],
])('a rate of %d reads %s, in whole percent', (rate, text) => {
  expect(shown(rate)).toBe(text);
});

test.each([
  [200 / 201, '99,5 %'],
  [0.999, '99,9 %'],
  [0.99999, '99,9 %'],
  [0.991, '99,1 %'],
])('a rate of %d just under everyone reads %s, never 100 %', (rate, text) => {
  expect(shown(rate)).toBe(text);
});

test.each([
  [1 / 1000, '0,1 %'],
  [1 / 100000, '0,1 %'],
  [0.004, '0,4 %'],
])('a rate of %d just above nobody reads %s, never 0 %', (rate, text) => {
  expect(shown(rate)).toBe(text);
});

test('in English the figures read the same way', () => {
  expect(shown(200 / 201, 'en-GB')).toBe('99.5%');
  expect(shown(1, 'en-GB')).toBe('100%');
});
