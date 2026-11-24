import { z } from 'zod';
import { getConfigJson } from '@/api/generated';

/**
 * What the edge's GET /config.json tells the browser before it signs in: the realm it signs in with and its client
 * there. Read at runtime, so one build serves every environment; checked like any untrusted input.
 */
export const runtimeConfigSchema = z.object({
  oidcAuthority: z.url({ protocol: /^https?$/ }),
  clientId: z.string().min(1),
});

export type RuntimeConfig = z.infer<typeof runtimeConfigSchema>;

let loaded: RuntimeConfig | undefined;

/** Asks the edge once, through the generated client, and keeps a valid answer for the page's lifetime; rejects otherwise. */
export async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  const config = runtimeConfigSchema.parse(await getConfigJson());
  loaded = config;
  return config;
}

/** The loaded configuration; src/bootstrap.ts loads it before the app mounts. */
export function runtimeConfig(): RuntimeConfig {
  if (loaded === undefined) {
    throw new Error('The runtime configuration is not loaded yet: await loadRuntimeConfig() before the app mounts.');
  }
  return loaded;
}

/** The loaded configuration, or undefined before loadRuntimeConfig() has succeeded. */
export function loadedRuntimeConfig(): RuntimeConfig | undefined {
  return loaded;
}
