import { computed } from 'vue';
import {
  getGetEditorialSamplesQueryKey,
  type PagedResultOfEditorialSampleDto,
  useGetEditorialSamples,
  usePostEditorialSamples,
} from '@/api/generated';

/** The page of samples the module's page lists: the first, as many as the host gives by default. */
export const samplesPage = { page: 1, pageSize: 20 };

const samplesKey = getGetEditorialSamplesQueryKey(samplesPage);

let pending = 0;

/** The cached page with the new sample in it, by name as the host lists them, until the host's own answer replaces it. */
function withPending(page: PagedResultOfEditorialSampleDto, name: string): PagedResultOfEditorialSampleDto {
  pending += 1;
  const items = [...page.items, { id: `pending-${pending}`, name }].sort((first, second) =>
    first.name < second.name ? -1 : first.name > second.name ? 1 : 0,
  );
  return { ...page, items, totalCount: Number(page.totalCount) + 1 };
}

/** The module's samples, a page at a time, and the page they are of: undefined until the host answers. */
export function useEditorialSamples() {
  const { data, isLoading, isError } = useGetEditorialSamples(samplesPage);
  return { samples: computed(() => data.value?.items), page: data, isLoading, isError };
}

/**
 * Creates a sample; resolves to whether the host created it. The sample shows in the list at once: it is put into the
 * cached page as the request leaves, and the page as it was put back when the host refuses it. Either way the page is
 * asked for again, with the id the host gave a created one.
 */
export function useCreateEditorialSample() {
  const { mutateAsync, isPending } = usePostEditorialSamples({
    mutation: {
      onMutate: async ({ data }, { client }) => {
        await client.cancelQueries({ queryKey: samplesKey });
        const before = client.getQueryData<PagedResultOfEditorialSampleDto>(samplesKey);
        if (before !== undefined) {
          client.setQueryData(samplesKey, withPending(before, data.name));
        }
        return { before };
      },
      onError: (_error, _variables, saved, { client }) => {
        if (saved?.before !== undefined) {
          client.setQueryData(samplesKey, saved.before);
        }
      },
      onSettled: (_data, _error, _variables, _saved, { client }) => client.invalidateQueries({ queryKey: samplesKey }),
    },
  });
  const create = async (name: string) => {
    try {
      await mutateAsync({ data: { name } });
      return true;
    } catch {
      return false;
    }
  };
  return { create, isSaving: isPending };
}
