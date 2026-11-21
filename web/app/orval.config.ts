import { defineConfig } from 'orval';

// pnpm codegen turns openapi.json into src/api/generated.ts: TanStack Query hooks whose every call goes through apiFetch.
export default defineConfig({
  api: {
    input: './openapi.json',
    output: {
      target: './src/api/generated.ts',
      client: 'vue-query',
      httpClient: 'fetch',
      override: {
        mutator: { path: './src/api/baseApi.ts', name: 'apiFetch' },
        fetch: { includeHttpResponseReturnType: false },
      },
    },
  },
});
