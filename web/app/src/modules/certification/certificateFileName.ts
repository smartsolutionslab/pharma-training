/** What no file name may hold on Windows, macOS or Linux: their separators, wildcards and control characters. */
const forbidden = /[\\/:*?"<>|\p{Cc}]/gu;

/**
 * The name a certificate's PDF is saved under: the course's title, with each character a file name may not hold
 * replaced by a hyphen, its spaces made one and no dot at its end; `certificate.pdf` when nothing of it is left.
 */
export function certificateFileName(courseTitle: string): string {
  const name = courseTitle
    .replace(/\s+/g, ' ')
    .replace(forbidden, '-')
    .trim()
    .replace(/[.\s]+$/, '');
  return `${name === '' ? 'certificate' : name}.pdf`;
}
