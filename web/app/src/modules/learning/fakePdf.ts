import { vi } from 'vitest';

// pdf.js as PdfChapter uses it, typed here so the tests need nothing of pdfjs-dist's own: getDocument(params) answers a
// loading task whose promise is the document; the document has numPages and getPage(n); a page gives its viewport, its
// text content, and renders into a canvas, answering a render task. TextLayer lays a page's text over the canvas.

export interface FakeViewport {
  width: number;
  height: number;
  scale: number;
}

export interface FakeRenderTask {
  promise: Promise<void>;
  cancel: () => void;
}

export interface FakeTextContent {
  items: { str: string }[];
  styles: Record<string, unknown>;
}

export interface FakePage {
  pageNumber: number;
  getViewport: (parameters: { scale: number }) => FakeViewport;
  getTextContent: () => Promise<FakeTextContent>;
  render: (parameters: Record<string, unknown>) => FakeRenderTask;
}

export interface FakeDocument {
  numPages: number;
  getPage: (pageNumber: number) => Promise<FakePage>;
  destroy: () => Promise<void>;
}

export interface FakeLoadingTask {
  promise: Promise<FakeDocument>;
  destroy: () => Promise<void>;
}

/** The text on a page of a fake document. */
export const textOfPage = (pageNumber: number) => `Text der Seite ${pageNumber}`;

export interface FakeDocumentOptions {
  /** Whether a page's drawing waits until the test calls draw(pageNumber); by default it ends at once. */
  holdDrawing?: boolean;
  /** Whether a page's text content waits until the test calls giveText(pageNumber); by default it comes at once. */
  holdText?: boolean;
}

/** A document of the given number of pages, each recording that it was asked for and rendered. */
export function fakeDocument(numPages: number, { holdDrawing = false, holdText = false }: FakeDocumentOptions = {}) {
  const rendered: number[] = [];
  const held = new Map<number, () => void>();
  const heldText = new Map<number, () => void>();
  const getPage = vi.fn(async (pageNumber: number): Promise<FakePage> => ({
    pageNumber,
    getViewport: ({ scale }) => ({ width: 595 * scale, height: 842 * scale, scale }),
    getTextContent: async () => {
      if (holdText) {
        await new Promise<void>((resolve) => heldText.set(pageNumber, resolve));
      }
      return { items: [{ str: textOfPage(pageNumber) }], styles: {} };
    },
    render: () => {
      rendered.push(pageNumber);
      if (!holdDrawing) {
        return { promise: Promise.resolve(), cancel: () => undefined };
      }
      const promise = new Promise<void>((resolve) => held.set(pageNumber, resolve));
      return { promise, cancel: () => undefined };
    },
  }));
  const document: FakeDocument = { numPages, getPage, destroy: async () => undefined };
  /** Ends the held drawing of the page. */
  const draw = (pageNumber: number) => held.get(pageNumber)?.();
  /** Whether the page's text content has been asked for and is held. */
  const textAsked = (pageNumber: number) => heldText.has(pageNumber);
  /** Gives the held text content of the page. */
  const giveText = (pageNumber: number) => heldText.get(pageNumber)?.();
  return { document, getPage, rendered, draw, textAsked, giveText };
}

/** pdf.js's TextLayer as far as PdfChapter uses it: render() puts a span per text item into the container. */
export class FakeTextLayer {
  private readonly source: FakeTextContent | Promise<FakeTextContent>;
  private readonly container: HTMLElement;
  private cancelled = false;

  constructor({
    textContentSource,
    container,
  }: {
    textContentSource: FakeTextContent | Promise<FakeTextContent>;
    container: HTMLElement;
    viewport: FakeViewport;
  }) {
    this.source = textContentSource;
    this.container = container;
  }

  async render(): Promise<void> {
    const content = await this.source;
    if (this.cancelled) {
      return;
    }
    for (const item of content.items) {
      const span = document.createElement('span');
      span.textContent = item.str;
      this.container.append(span);
    }
  }

  cancel(): void {
    this.cancelled = true;
  }
}
