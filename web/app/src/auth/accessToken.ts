import { User } from 'oidc-client-ts';
import { loadedRuntimeConfig } from './runtimeConfig';

/**
 * The signed-in user's access token, read where oidc-client-ts stores it for the realm and client of the runtime
 * configuration (code outside components needs it). None before that configuration is loaded: the call that loads it
 * goes out without one.
 */
export function accessToken(): string | undefined {
  const config = loadedRuntimeConfig();
  if (config === undefined) {
    return undefined;
  }
  const stored = sessionStorage.getItem(`oidc.user:${config.oidcAuthority}:${config.clientId}`);
  return stored ? User.fromStorageString(stored).access_token : undefined;
}
