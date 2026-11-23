import { expect, test } from 'vitest';
import { formatDuration } from './formatDuration';

// A video chapter's duration (VideoDuration: 1 to 14,400 whole seconds) as minutes:seconds, the way the chapter form
// takes it: minutes not capped at 59, seconds always two digits.
test.each([
  [1, '0:01'],
  [59, '0:59'],
  [60, '1:00'],
  [725, '12:05'],
  [3600, '60:00'],
  [14_400, '240:00'],
])('%i seconds read %s', (seconds, shown) => {
  expect(formatDuration(seconds)).toBe(shown);
});

// The generated client types an int64 as number | string: the host may send either.
test('a duration the host sends as a string reads the same', () => {
  expect(formatDuration('725')).toBe('12:05');
});
