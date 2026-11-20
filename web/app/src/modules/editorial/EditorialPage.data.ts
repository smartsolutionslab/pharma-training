// The example data EditorialPage's axe check shows the page with, by hook of the generated client: in examples
// with data, in emptyExamples with nothing to show. A hook without an example answers as if its request were still on
// its way. Give a hook of your own screen its examples beside the samples'.
export const examples: Record<string, unknown> = {
  useGetEditorialSamples: {
    items: [
      { id: '0198c1a0-0000-7000-8000-000000000001', name: 'Ada' },
      { id: '0198c1a0-0000-7000-8000-000000000002', name: 'Bea' },
    ],
    totalCount: 2,
    page: 1,
    pageSize: 20,
  },
};

export const emptyExamples: Record<string, unknown> = {
  useGetEditorialSamples: { items: [], totalCount: 0, page: 1, pageSize: 20 },
};
