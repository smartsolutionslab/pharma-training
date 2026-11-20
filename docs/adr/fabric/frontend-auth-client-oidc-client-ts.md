# Browser sign-in: oidc-client-ts

**Status:** Accepted (generated)

## Decision

The browser signs in with the authorization-code flow and PKCE through `oidc-client-ts`'s `UserManager`, read through a small hand-written composable, `useAuth()` (`src/auth/useAuth.ts`). No Vue OIDC library: none is maintained, and the composable needs only the user, the sign-in state and the two redirects. The whole app sits behind `SignedIn.vue`: an anonymous visitor is sent to the identity provider. Every API call carries the user's access token, added in `src/api/baseApi.ts`.

The identity provider's address arrives as `VITE_OIDC_AUTHORITY` (the AppHost sets it); the client id is the realm's `<project>-web` client.

**Showing and hiding by role.** With `backend.authorization` `[roles]`, `src/auth/` has the composable `useHasRole(role)`, a `ComputedRef<boolean>`, and the component `RequirePermission.vue` (`role`), which renders its default slot for a user who meets the requirement and its `#fallback` slot, nothing by default, for anyone else. They read the ID token's claims (`useAuth().user`'s `profile`): no request, no token parsing. The Keycloak realm puts the realm roles (`realm_access.roles`, realm roles only, not the API client's roles) into the ID token for this. They mirror React's hooks and component (decided by the repository owner, 2026-11-04); the Vue branch, with no catalog rule, was decided on the generator's issue #517 (2026-11-10).

Hiding is a convenience, never protection: the browser and its ID token are the user's to change, and the backend policy decides. A user who should not see a button gets a 403 from its endpoint whether or not the button is hidden. The ID token is read at sign-in, so a role or claim granted later shows once the user signs in again (or the token is renewed); the backend sees it on the next access token.

## Gates

`src/auth/SignedIn.test.ts` checks, against a fake `UserManager`, that an anonymous visitor is sent to the identity provider, that the way back completes the sign-in and drops its parameters from the address, that a failed sign-in, a failed silent renewal and a redirect that cannot start are shown (a failed sign-in is not retried), that an expired access token sends the user back to the identity provider, that signing out starts no sign-in on its way out, and that a signed-in user sees who they are and the app. `e2e/sign-in.spec.ts` signs a realm user in through the running AppHost. `src/auth/RequirePermission.test.ts` shows an element to `developer` and hides it from `visitor` (role `manager`), with `useAuth()` answering with the ID token claims of the realm's local users. It cannot see whether an endpoint behind a hidden element has a policy: that is the backend's gate.

## Consequences

- Tokens live in session storage: closing the tab signs the user out of the app, not out of the identity provider.
- Components read the user with `useAuth()`; nothing else talks to the identity provider.
