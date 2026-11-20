import type { UserManagerSettings } from 'oidc-client-ts';

export const oidcConfig = {
  authority: import.meta.env.VITE_OIDC_AUTHORITY ?? 'http://localhost:8080/realms/pharma-training',
  client_id: 'pharma-training-web',
  redirect_uri: window.location.origin,
  post_logout_redirect_uri: window.location.origin,
} satisfies UserManagerSettings;
