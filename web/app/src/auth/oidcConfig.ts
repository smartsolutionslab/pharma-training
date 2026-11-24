import type { UserManagerSettings } from 'oidc-client-ts';
import type { RuntimeConfig } from './runtimeConfig';

/** oidc-client-ts's settings for the realm and client the edge named at runtime; the app's own origin is the way back. */
export function oidcSettings(config: RuntimeConfig) {
  return {
    authority: config.oidcAuthority,
    client_id: config.clientId,
    redirect_uri: window.location.origin,
    post_logout_redirect_uri: window.location.origin,
  } satisfies UserManagerSettings;
}
