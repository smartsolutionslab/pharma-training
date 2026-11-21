import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, waitFor } from '@testing-library/vue';
import { afterEach, expect, test, vi } from 'vitest';
import { defineComponent } from 'vue';
import { useCreateComplianceSample, useComplianceSamples } from './useComplianceSamples';

// The host, as GET and POST /compliance/samples reach it through the API client.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** The method of a call, whether it came as a Request or as a URL with its options. */
function methodOf(input: RequestInfo | URL, init?: RequestInit) {
  return input instanceof Request ? input.method : init?.method;
}

const ada = { id: '0198c1a0-0000-7000-8000-000000000001', name: 'Ada' };
const bea = { id: '0198c1a0-0000-7000-8000-000000000002', name: 'Bea' };

function page(items: (typeof ada)[]) {
  return Response.json({ items, totalCount: items.length, page: 1, pageSize: 20 });
}

// A query client of its own per test, so no answer is cached from the test before; it does not retry, so a failure
// shows at once.
function renderSamples() {
  const used: {
    list?: ReturnType<typeof useComplianceSamples>;
    creation?: ReturnType<typeof useCreateComplianceSample>;
  } = {};
  const Samples = defineComponent({
    setup() {
      used.list = useComplianceSamples();
      used.creation = useCreateComplianceSample();
      return () => null;
    },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(Samples, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  const { list, creation } = used;
  if (list === undefined || creation === undefined) {
    throw new Error('The component did not set up.');
  }
  return { list, creation };
}

const names = (samples: { name: string }[] | undefined) => samples?.map((sample) => sample.name);

test('a created sample is in the list before the server answers and stays once it is created', async () => {
  let answer: (response: Response) => void = () => undefined;
  let created = false;
  host.mockImplementation(async (input, init) => {
    if (methodOf(input, init) === 'POST') {
      return new Promise<Response>((resolve) => {
        answer = resolve;
      });
    }
    return page(created ? [ada, bea] : [ada]);
  });
  const { list, creation } = renderSamples();
  await waitFor(() => expect(names(list.samples.value)).toEqual(['Ada']));

  const creating = creation.create('Bea');

  await waitFor(() => expect(names(list.samples.value)).toEqual(['Ada', 'Bea']));
  created = true;
  answer(Response.json(bea.id, { status: 201 }));
  await expect(creating).resolves.toBe(true);
  await waitFor(() => expect(list.samples.value?.map((sample) => sample.id)).toEqual([ada.id, bea.id]));
});

test('a sample the server refuses is taken out of the list again', async () => {
  host.mockImplementation(async (input, init) =>
    methodOf(input, init) === 'POST' ? Response.json({ title: 'No.' }, { status: 500 }) : page([ada]),
  );
  const { list, creation } = renderSamples();
  await waitFor(() => expect(names(list.samples.value)).toEqual(['Ada']));

  const creating = creation.create('Bea');

  await expect(creating).resolves.toBe(false);
  await waitFor(() => expect(names(list.samples.value)).toEqual(['Ada']));
});
