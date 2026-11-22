# 0003: Per-pharmacy capabilities are checked in the handler

**Status:** Accepted

## Context

- AGENTS.md ("Authentication: Keycloak", "Authorization: by role") makes authorisation rules policies registered by the module, not role checks inside handlers. A module's policies (`<Module>Policies : IModulePolicies`) are built with `RequireRole(...)` over the caller's realm roles, and the architecture rule `OnlyModulePoliciesCheckRoles` fails the build on a role check anywhere else.
- What a member may do in a pharmacy is not a realm role. It follows from their membership in that one pharmacy: the same person manages one pharmacy and is an employee in another. The domain says so ("Rechte über Fähigkeiten, nicht über Rollennamen", `docs/domain/README.md`, line 32): a membership's role (`MembershipRole`) grants capabilities (`Capability`, `RoleCapabilities.Grants`), and endpoints ask for a capability, never a role name.
- A policy runs before the handler and sees the token, not the pharmacy. Deciding the capability there would mean loading the pharmacy twice, or putting every membership into the token, which goes stale the moment a role changes (#11).
- #11 (PR #128) checks it through `PharmacyAccess.Require` in each handler, and #9 and #10 are told to reuse it. The review asked for the choice to be recorded, so that a later module does not read it as licence for realm-role checks in a handler.

## Decision

- **A per-pharmacy capability is checked in the Application, in the handler, through `PharmacyAccess.Require(person, capability)`** on the pharmacy the handler loaded, before it changes anything. It answers the caller's own active membership when its role grants the capability; `organisation.pharmacy-not-found` (`404`) when the caller has no active membership there, so a non-member learns nothing about the pharmacy; and `organisation.capability-missing` (`403`) when their role lacks the capability.
- **A handler asks for a `Capability`, never a `MembershipRole`.** Which role grants what is `RoleCapabilities.Grants`, in the Domain, and nowhere else; a new role (QMS, Filialleitung) changes that table, not the handlers.
- **A realm role is always a module policy.** Anything decided by the caller's Keycloak roles, whatever the module, stays a policy in `<Module>Policies` named on the endpoint; `OnlyModulePoliciesCheckRoles` keeps holding. The endpoint of a per-pharmacy use case still carries `RequireAuthorization()`, so an anonymous caller gets `401` before the handler runs.

## Consequences

- This decision covers capabilities that come from the module's own records about the caller, here a membership in a pharmacy. It is no exception to "no role checks inside handlers" for realm roles: `IsInRole`, `ClaimTypes.Role` or `RequireRole` in a handler still fails the build.
- The check runs inside the use case, so it is unit-tested with the handler and answers through the module's `Result` and `ApiError`s like any other failure. A handler that forgets to call it is not caught by an architecture rule; each new Organisation use case that acts in a pharmacy gets tests for a member without the capability (`403`) and a non-member (`404`).
- Another module that needs a member's capability in a pharmacy (Compliance's "only Leitung assigns", #29) decides it from its own copy of the membership, kept current by Organisation's integration events, in the same way; it never calls Organisation or reads its tables.
