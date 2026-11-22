// The example data AcceptInvitationPage's axe check shows the page with, by hook of the generated client: in examples
// with data, in emptyExamples with nothing to show. The page calls no query hook of the generated client (it posts the
// invitation's token, a mutation, and reads the memberships and the pharmacy only after that, through the query
// client), so it has no example: the axe check renders it without a token (the link to open again) and while the
// invitation is accepted.
export const examples: Record<string, unknown> = {};

export const emptyExamples: Record<string, unknown> = {};
