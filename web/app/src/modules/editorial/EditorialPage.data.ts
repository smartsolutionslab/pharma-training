// The example data EditorialPage's axe check shows the page with, by hook of the generated client: in examples
// with data, in emptyExamples with nothing to show. A hook without an example answers as if its request were still on
// its way.

// The inbox's drafts (GET /editorial/course-versions?status=Draft): the last page of a longer list, so the pager shows.
export const examples: Record<string, unknown> = {
  useGetEditorialCourseVersions: {
    items: [
      {
        id: '0198c1a0-0000-7000-8000-0000000000c1',
        courseId: '0198c1a0-0000-7000-8000-0000000000d1',
        number: 2,
        title: 'Hygiene in der Apotheke',
        status: 'Draft',
      },
      {
        id: '0198c1a0-0000-7000-8000-0000000000c2',
        courseId: '0198c1a0-0000-7000-8000-0000000000d2',
        number: 1,
        title: 'Kennzeichnung von Rezepturen',
        status: 'Draft',
      },
      {
        id: '0198c1a0-0000-7000-8000-0000000000c3',
        courseId: '0198c1a0-0000-7000-8000-0000000000d3',
        number: 3,
        title: 'Lagerung von Arzneimitteln',
        status: 'Draft',
      },
    ],
    totalCount: 23,
    page: 2,
    pageSize: 20,
  },
};

export const emptyExamples: Record<string, unknown> = {
  useGetEditorialCourseVersions: { items: [], totalCount: 0, page: 1, pageSize: 20 },
};
