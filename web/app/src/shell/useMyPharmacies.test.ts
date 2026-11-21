import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, waitFor } from '@testing-library/vue';
import { afterEach, expect, test, vi } from 'vitest';
import { defineComponent } from 'vue';
import { useMyPharmacies } from './useMyPharmacies';

// The host, as GET /organisation/pharmacies/mine reaches it through the API client.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);
afterEach(() => host.mockReset());
afterEach(cleanup);

const markt = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  name: 'Apotheke am Markt',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
  role: 'Manager',
  jobFunction: 'Pharmacist',
  canManageMembers: true,
  canAssignCourses: true,
};

const urlOf = (input: RequestInfo | URL) => (input instanceof Request ? input.url : String(input));

function renderPharmacies() {
  let used: ReturnType<typeof useMyPharmacies> | undefined;
  const Pharmacies = defineComponent({
    setup() {
      used = useMyPharmacies();
      return () => null;
    },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(Pharmacies, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  if (used === undefined) {
    throw new Error('The component did not set up.');
  }
  return used;
}

// One page of the largest size the host allows (PageSize.Maximum): a user belongs to a handful of pharmacies.
test("the user's pharmacies are asked of the host in one page of 100", async () => {
  host.mockResolvedValue(Response.json({ items: [markt], totalCount: 1, page: 1, pageSize: 100 }));

  const pharmacies = renderPharmacies();

  await waitFor(() => expect(pharmacies.data.value?.items).toEqual([markt]));
  expect(host).toHaveBeenCalledOnce();
  const [input] = host.mock.calls[0] ?? ['/nowhere'];
  const url = new URL(urlOf(input), 'http://localhost');
  expect(url.pathname).toBe('/api/organisation/pharmacies/mine');
  expect(url.searchParams.get('pageSize')).toBe('100');
  expect(pharmacies.isPending.value).toBe(false);
});

test('a refused answer is an error, not an empty list', async () => {
  host.mockResolvedValue(new Response('No.', { status: 500 }));

  const pharmacies = renderPharmacies();

  await waitFor(() => expect(pharmacies.isError.value).toBe(true));
  expect(pharmacies.data.value).toBeUndefined();
});
