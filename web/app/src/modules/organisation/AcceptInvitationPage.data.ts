// The example data AcceptInvitationPage's axe check shows the page with, by hook of the generated client: in examples
// with data, in emptyExamples with nothing to show. The page calls no query hook of the generated client (it posts the
// invitation's token, a mutation, and reads the memberships and the pharmacy only after that, through the query
// client), so it has no example: the axe check renders it without a token (the link to open again), ready to accept a
// kept token, and while the invitation is accepted, once its button is pressed.
export const examples: Record<string, unknown> = {};

export const emptyExamples: Record<string, unknown> = {};

// The refusals the axe check shows the page with besides: how the host answers POST /organisation/invitations/accept
// for an expired invitation and for a link forwarded to someone signed in with another address.
export const refusals: Record<string, { status: number; body: Record<string, unknown> }> = {
  expired: {
    status: 410,
    body: {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.11',
      title: 'Die Einladung ist abgelaufen.',
      status: 410,
      code: 'organisation.invitation-expired',
    },
  },
  anotherAddress: {
    status: 403,
    body: {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.4',
      title: 'Die Einladung gilt für eine andere E-Mail-Adresse.',
      status: 403,
      code: 'organisation.invitation-for-another-address',
    },
  },
};
