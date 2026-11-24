import { expect, test } from 'vitest';
import { oidcSettings } from './oidcConfig';

test('the sign-in settings come from the runtime configuration', () => {
  const settings = oidcSettings({
    oidcAuthority: 'https://auth.example.test/realms/pharma-training',
    clientId: 'pharma-training-web',
  });

  expect(settings).toEqual({
    authority: 'https://auth.example.test/realms/pharma-training',
    client_id: 'pharma-training-web',
    redirect_uri: window.location.origin,
    post_logout_redirect_uri: window.location.origin,
  });
});

test('another environment signs in with its own identity provider and client', () => {
  const settings = oidcSettings({ oidcAuthority: 'http://localhost:8080/realms/other', clientId: 'other-web' });

  expect(settings).toMatchObject({ authority: 'http://localhost:8080/realms/other', client_id: 'other-web' });
});

// The app's sources as Vite reads them, tests left out: no build-time authority is left to fall back on.
const sources = import.meta.glob<string>(['/src/**/*.ts', '/src/**/*.vue', '!/src/**/*.test.ts'], {
  query: '?raw',
  import: 'default',
  eager: true,
});

test('no source reads the identity provider from the build any more', () => {
  const readers = Object.entries(sources)
    .filter(([, source]) => source.includes('VITE_OIDC_AUTHORITY'))
    .map(([path]) => path);

  expect(readers).toEqual([]);
});
