import { expect, test } from 'vitest';
import { certificateFileName } from './certificateFileName';

// The name a certificate's PDF is saved under: the course's title, as a name every system's files may have.
test.each([
  ['Hygiene in der Apotheke', 'Hygiene in der Apotheke.pdf'],
  ['Kühlkette: Lagerung/Transport', 'Kühlkette- Lagerung-Transport.pdf'],
  ['Was ist "BtM"? <Teil 1|2>*', 'Was ist -BtM-- -Teil 1-2--.pdf'],
  ['  Datenschutz\t2026  ', 'Datenschutz 2026.pdf'],
  ['Ende.', 'Ende.pdf'],
])('%j is saved as %j', (course, name) => {
  expect(certificateFileName(course)).toBe(name);
});

test('a title with nothing a file name may hold is saved under a name of its own', () => {
  expect(certificateFileName(' ...  ')).toBe('certificate.pdf');
});
