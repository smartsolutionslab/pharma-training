import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, test, vi } from 'vitest';
import type { EditorialChapterDto } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import ChapterList from './ChapterList.vue';

// A draft's chapters, reordered two ways that both end in one onReorder with every chapter's id in the new order
// (PUT .../chapters/order takes the whole list): move up and down buttons, named per chapter, for keyboard, touch and
// screen reader, and native drag and drop of a chapter by its handle for a mouse. A visually hidden status region says
// where the chapter now is. The order shown is the chapters prop's: the page writes it (optimistically) and passes it in.
const onReorder = vi.fn<(order: string[]) => void>();
const onEdit = vi.fn<(chapter: EditorialChapterDto) => void>();
const onRemove = vi.fn<(chapter: EditorialChapterDto) => void>();

beforeEach(() => {
  onReorder.mockReset();
  onEdit.mockReset();
  onRemove.mockReset();
});

/** A text chapter at the position. */
const chapter = (id: string, position: number, title: string): EditorialChapterDto => ({
  id,
  position,
  title,
  kind: 'Text',
  text: `# ${title}`,
  file: null,
  durationSeconds: null,
  pdfText: null,
});
const a = chapter('0198c1a0-0000-7000-8000-0000000000e1', 1, 'A');
const b = chapter('0198c1a0-0000-7000-8000-0000000000e2', 2, 'B');
const c = chapter('0198c1a0-0000-7000-8000-0000000000e3', 3, 'C');

function renderList(chapters: EditorialChapterDto[] = [a, b, c], editable = true) {
  return render(ChapterList, { props: { chapters, editable, onReorder, onEdit, onRemove } });
}

const up = (each: EditorialChapterDto) =>
  screen.getByRole('button', { name: t('editorial.chapters.moveUp', { title: each.title }) });
const down = (each: EditorialChapterDto) =>
  screen.getByRole('button', { name: t('editorial.chapters.moveDown', { title: each.title }) });
const item = (each: EditorialChapterDto) => {
  const found = screen.getAllByRole('listitem').find((listItem) => within(listItem).queryByText(each.title) !== null);
  if (found === undefined) {
    throw new Error(`No list item for ${each.title}.`);
  }
  return found;
};
const handle = (each: EditorialChapterDto) => within(item(each)).getByTitle(t('editorial.chapters.dragHint'));

/** The status region's announcement, once it says something. */
async function announcement() {
  const status = await waitFor(() => {
    const region = screen.getAllByRole('status').find((each) => each.textContent.trim() !== '');
    expect(region).toBeDefined();
    return region as HTMLElement;
  });
  return status.textContent.trim();
}

/** A DataTransfer as a browser hands it to drag events; jsdom has none. */
function dataTransfer() {
  const data = new Map<string, string>();
  return {
    dropEffect: 'none',
    effectAllowed: 'all',
    types: [] as string[],
    setData: (format: string, value: string) => data.set(format, value),
    getData: (format: string) => data.get(format) ?? '',
    clearData: () => data.clear(),
    setDragImage: () => undefined,
  };
}

/** Drags the chapter by its handle onto the other chapter's list item, as a mouse does. */
async function drag(dragged: EditorialChapterDto, onto: EditorialChapterDto) {
  const transfer = dataTransfer();
  await fireEvent.dragStart(handle(dragged), { dataTransfer: transfer });
  await fireEvent.dragEnter(item(onto), { dataTransfer: transfer });
  // Dispatched by hand: Testing Library's fireEvent does not say whether the event was cancelled.
  const over = new Event('dragover', { bubbles: true, cancelable: true });
  Object.defineProperty(over, 'dataTransfer', { value: transfer });
  item(onto).dispatchEvent(over);
  await fireEvent.drop(item(onto), { dataTransfer: transfer });
  await fireEvent.dragEnd(handle(dragged), { dataTransfer: transfer });
  return { dropAllowed: over.defaultPrevented };
}

test('the chapters are an ordered list in the order given', () => {
  renderList([b, c, a]);

  const list = screen.getByRole('list');
  expect(list.tagName).toBe('OL');
  expect(
    within(list)
      .getAllByRole('listitem')
      .map((listItem) => within(listItem).getByText(/^[ABC]$/).textContent),
  ).toEqual(['B', 'C', 'A']);
});

test('moving B up reorders to B, A, C and says where B is now', async () => {
  const user = userEvent.setup();
  renderList();

  await user.click(up(b));

  expect(onReorder).toHaveBeenCalledOnce();
  expect(onReorder).toHaveBeenCalledWith([b.id, a.id, c.id]);
  expect(await announcement()).toBe(t('editorial.chapters.moved', { title: 'B', position: 1, count: 3 }));
});

test('moving A down reorders to B, A, C and says where A is now', async () => {
  const user = userEvent.setup();
  renderList();

  await user.click(down(a));

  expect(onReorder).toHaveBeenCalledWith([b.id, a.id, c.id]);
  expect(await announcement()).toBe(t('editorial.chapters.moved', { title: 'A', position: 2, count: 3 }));
});

test('the buttons move by keyboard', async () => {
  const user = userEvent.setup();
  renderList();

  down(b).focus();
  await user.keyboard('{Enter}');
  up(c).focus();
  await user.keyboard(' ');

  expect(onReorder.mock.calls).toEqual([[[a.id, c.id, b.id]], [[a.id, c.id, b.id]]]);
});

test('the first cannot move up and the last cannot move down', () => {
  renderList();

  expect(up(a)).toBeDisabled();
  expect(down(a)).toBeEnabled();
  expect(up(b)).toBeEnabled();
  expect(down(b)).toBeEnabled();
  expect(up(c)).toBeEnabled();
  expect(down(c)).toBeDisabled();
});

test('the pressed button keeps the focus once the list shows the new order', async () => {
  const user = userEvent.setup();
  const { rerender } = renderList();
  const pressed = up(c);

  await user.click(pressed);
  await rerender({ chapters: [a, c, b] });

  expect(screen.getAllByRole('listitem').map((listItem) => within(listItem).getByText(/^[ABC]$/).textContent)).toEqual([
    'A',
    'C',
    'B',
  ]);
  expect(up(c)).toBe(pressed);
  expect(pressed).toHaveFocus();
});

test('the announcement speaks German', async () => {
  const user = userEvent.setup();
  renderList();

  await user.click(up(b));

  expect(await announcement()).toMatch(/^„B. steht jetzt an Position 1 von 3\.$/);
  expect(screen.getByRole('button', { name: /^„C. nach oben verschieben$/ })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: /^„C. nach unten verschieben$/ })).toBeInTheDocument();
});

test('switched to English, the buttons and the announcement speak English', async () => {
  const user = userEvent.setup();
  renderList();
  setLanguage('en-GB', { remember: false });

  await user.click(await screen.findByRole('button', { name: 'Move “B” up' }));

  expect(await announcement()).toBe('“B” is now at position 1 of 3.');
});

test('the drag handle is for the mouse alone: hidden from screen readers, titled for a pointer', () => {
  renderList();

  for (const each of [a, b, c]) {
    expect(handle(each)).toHaveAttribute('aria-hidden', 'true');
  }
  expect(within(item(a)).getByTitle('Zum Verschieben ziehen')).toBeInTheDocument();
});

test('dragging C by its handle onto A reorders to C, A, B and says where C is now', async () => {
  renderList();

  const { dropAllowed } = await drag(c, a);

  // A drop target allows the drop by cancelling dragover; otherwise the browser never fires drop.
  expect(dropAllowed).toBe(true);
  expect(onReorder).toHaveBeenCalledOnce();
  expect(onReorder).toHaveBeenCalledWith([c.id, a.id, b.id]);
  expect(await announcement()).toBe(t('editorial.chapters.moved', { title: 'C', position: 1, count: 3 }));
});

test('dragging A onto C reorders to B, C, A', async () => {
  renderList();

  await drag(a, c);

  expect(onReorder).toHaveBeenCalledWith([b.id, c.id, a.id]);
});

test('a chapter dropped onto itself changes nothing', async () => {
  renderList();

  await drag(b, b);

  expect(onReorder).not.toHaveBeenCalled();
});

test('a list that cannot be edited has no buttons and nothing to drag', () => {
  const { container } = renderList([a, b, c], false);

  expect(screen.getAllByRole('listitem')).toHaveLength(3);
  expect(screen.queryAllByRole('button', { name: /verschieben|bearbeiten|löschen/ })).toEqual([]);
  expect(screen.queryByTitle(t('editorial.chapters.dragHint'))).not.toBeInTheDocument();
  expect(container.querySelector('[draggable="true"]')).toBeNull();
});

test("a chapter's edit and delete buttons hand the chapter to the page", async () => {
  const user = userEvent.setup();
  renderList();

  await user.click(screen.getByRole('button', { name: t('editorial.chapters.edit', { title: 'B' }) }));
  await user.click(screen.getByRole('button', { name: t('editorial.chapters.remove', { title: 'C' }) }));

  expect(onEdit).toHaveBeenCalledWith(b);
  expect(onRemove).toHaveBeenCalledWith(c);
  expect(onReorder).not.toHaveBeenCalled();
});

// The focus goes back to the pressed move button only right after the move it made: a later change of the chapters
// (one added or deleted, or another order read in the background) leaves the focus where the editor has put it since.
const d = chapter('0198c1a0-0000-7000-8000-0000000000e4', 4, 'D');

test.each([
  ['a chapter added', [a, c, b, d]],
  ['a chapter deleted', [a, c]],
  ['another order read in the background', [c, a, b]],
])('after a move, %s does not take the focus back to the move button', async (_case, later) => {
  const user = userEvent.setup();
  const { rerender } = renderList();
  await user.click(up(c));
  await rerender({ chapters: [a, c, b] });
  expect(up(c)).toHaveFocus();
  const edit = screen.getByRole('button', { name: t('editorial.chapters.edit', { title: 'A' }) });
  edit.focus();

  await rerender({ chapters: later });

  expect(edit).toHaveFocus();
});
