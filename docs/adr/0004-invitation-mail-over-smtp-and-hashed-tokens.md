# 0004: The invitation mail goes over SMTP, and only the token's hash is stored

**Status:** Accepted

## Context

- Issue #9 has a pharmacy's management invite an address, renew the invitation or revoke it. The invited person needs a link to accept the invitation, and the link must carry a secret only they receive.
- `docs/domain/README.md` said "Keine E-Mails in MVP 1", and the invitation was planned to go "per Mail über den IdP". Keycloak sends only its own mails (verification, reset password, required actions), not an arbitrary text with a link into the web app.
- On 2026-11-21 the owner chose answer (a) on #9: Organisation sends the invitation mail itself over SMTP. That is Mailpit locally. Deployed, it is a connection string `mail` and a parameter `mail-from`, both Aspire parameters, and the connection string is secret because it may hold credentials. The text is our own, in German, with the link `{frontend}/invitations/accept#token=…`. Self-registration in the realm belongs to #13.
- Before #9 the aggregate stored the raw token in `organisation.memberships.token`, compared raw tokens on accept, and `InvitationToken.ToString()` returned the secret.

## Decision

- **SMTP from the service, through a building block.** `src/BuildingBlocks/Mail` has `IMailSender` (`SmtpMailSender`, MailKit 4.18.2) and `IFrontendLinks` (`FrontendLinks`). `AddModuleInfrastructure` registers both for every module (`AddModuleMail`), as it does blob storage (docs/adr/0002). Only `BuildingBlocks.csproj` references MailKit. Registering reads nothing. A missing setting fails at the first send or link and names the setting: `ConnectionStrings:mail`, `Mail:From` or `Frontend:BaseUrl`. No error names a password.
- **The settings come from the AppHost**, one line per setting in the services' loop:
  - `mail`: in run mode, Mailpit's allocated SMTP endpoint (`smtp://localhost:<port>`, no credentials). In publish mode it is `AddConnectionString("mail")`, a secret in the deployment's store. Mailpit exists only in run mode.
  - `mail-from`: a parameter, `noreply@example.com` by default.
  - `Frontend__BaseUrl`: the web app's endpoint in run mode, and the parameter `frontend-url` in publish mode. That parameter is the deployed web app's public URL, which no endpoint reference knows.
- **The connection string** is `smtp://[user[:password]@]host[:port]` or `smtps://…`:
  - `smtps` connects with TLS, on port 465 by default.
  - `smtp` with credentials requires STARTTLS, so a password never travels in clear.
  - `smtp` without credentials (Mailpit) uses STARTTLS when the server offers it. Port 25 is the default.
  - Each send has a 10 s timeout. MailKit's default is two minutes, and the send runs inside the request.
- **The mail is German plain text.** `InvitationMails.Compose` sets To (the invited address) and Subject (`Einladung zu {Apotheke} bei PharmaTraining`). The text says when the link expires in UTC (`dd.MM.yyyy, HH:mm`, invariant culture), and says that a renewed invitation leaves only the newest link working. There is no HTML part, and MimeKit sets every header, so no pharmacy name or address can add one.
- **The token travels in the link's fragment.** The link is `/invitations/accept#token=…` at the web app's root. Browsers never send a fragment to a server, so the token appears in no access log or `Referer`. #13 builds that page and posts the token in the body of `POST /organisation/invitations/accept` (#10).
- **The token is 256 random bits, and only its SHA-256 is stored.**
  - `InvitationTokens.Next()` makes 43 URL-safe characters (`Base64Url` of 32 bytes from `RandomNumberGenerator`).
  - `Membership.TokenHash` (`InvitationTokenHash`, 64 lower-case hex characters) replaces `Membership.Token`. The column `token_hash` has a unique index, and the column `token` is dropped.
  - Accept looks the invitation up by `InvitationTokenHash.Of(token)`. Renew stores the new token's hash, so the old link matches nothing. Accept and revoke clear the hash.
  - There is no salt and no slow hash: the input is random, not a password, and a lookup by hash needs no timing-sensitive comparison.
  - `InvitationToken.ToString()` prints no value.
- **A domain-event reaction, not an integration event.** `SendInvitationMail` reacts to `MemberInvited` after the commit, during the request. `MemberInvited` carries the raw token and the pharmacy's name for it. That is safe because a domain event of this module lives in memory only: it is neither stored nor published. An integration event would put the raw token, in plain text, in Wolverine's envelope tables and, after its last retry, in `wolverine.wolverine_dead_letters` indefinitely. That would undo the hashing. It would also put the token in `src/Contracts`, which every module sees. No other module needs the invitation.
- **Uniqueness is backed twice.** `Pharmacy.Invite` refuses an address that is invited or active (409 `organisation.already-member-or-invited`). A partial unique index on `(pharmacy_id, email)` where `status IN ('Invited', 'Active')` refuses the row from a writer past that check (409 `already-exists`). The index excludes revoked rows, so a revoked address may be invited again.

## Consequences

- A failed send is logged at Error by the dispatcher, by type name, and is not retried. The invitation stays, and the API has already answered 201. The manager recovers with "Neu senden" (renew), which sends a new link and leaves the old one dead.
- SMTP latency is part of the invite and renew requests, bounded at 10 s. A slow server makes those requests slow, not wrong.
- Deploying needs `ConnectionStrings:mail` in the secret store (with credentials, over STARTTLS or smtps), `mail-from` set to an address the server may send from, and `frontend-url` set to the web app's public URL. Publish mode was not exercised by a test.
- Two renews in a row are fine: the later link works, and the earlier mail's link is dead.
- Self-registration in the realm (`registrationAllowed`, `verifyEmail`) is #13's, per the owner.
- "E-Mail-Erinnerungen nach MVP 1" can reuse `IMailSender`. No architecture rule yet keeps MailKit out of the modules; a follow-up can add one like `BlobStorageSdkStaysInBuildingBlocks`.
- `fabric upgrade` regenerates files this decision changed and may conflict on them: `src/AppHost/AppHost.cs`, `src/BuildingBlocks/Modules/ModuleInfrastructure.cs`, `src/BuildingBlocks/BuildingBlocks.csproj` and `Directory.Packages.props`. Keep this decision's changes when resolving them.
