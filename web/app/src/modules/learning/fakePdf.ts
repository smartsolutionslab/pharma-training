import { vi } from 'vitest';

// pdf.js as PdfChapter uses it, typed here so the tests need nothing of pdfjs-dist's own: getDocument(params) answers a
// loading task whose promise is the document; the document has numPages and getPage(n); a page gives its viewport and
// renders into a canvas, answering a render task.

export interface FakeViewport {
  width: number;
  height: number;
  scale: number;
}

export interface FakeRenderTask {
  promise: Promise<void>;
  cancel: () => void;
}

export interface FakePage {
  pageNumber: number;
  getViewport: (parameters: { scale: number }) => FakeViewport;
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

/** A document of the given number of pages, each recording that it was asked for and rendered. */
export function fakeDocument(numPages: number) {
  const rendered: number[] = [];
  const getPage = vi.fn(async (pageNumber: number): Promise<FakePage> => ({
    pageNumber,
    getViewport: ({ scale }) => ({ width: 595 * scale, height: 842 * scale, scale }),
    render: () => {
      rendered.push(pageNumber);
      return { promise: Promise.resolve(), cancel: () => undefined };
    },
  }));
  const document: FakeDocument = { numPages, getPage, destroy: async () => undefined };
  return { document, getPage, rendered };
}
