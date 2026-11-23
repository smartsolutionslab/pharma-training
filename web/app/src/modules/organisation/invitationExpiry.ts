/**
 * Whether an invitation has expired: its expiry lies before now. The domain compares strictly
 * (Pharmacy.IssueInvitationLink and AcceptInvitation: expiresAt < now), so at the very instant it expires the
 * invitation is still open.
 */
export function isExpired(expiresAt: string, now: Date): boolean {
  return new Date(expiresAt).getTime() < now.getTime();
}
