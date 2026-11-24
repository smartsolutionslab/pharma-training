/** Where a course version opens in the chapter player: the one address the Start and Continue links use. */
export function coursePath(courseVersionId: string): string {
  return `/learning/courses/${encodeURIComponent(courseVersionId)}`;
}
