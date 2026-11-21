// The example data RegisterPharmacyPage's axe check shows the page with, by hook of the generated client: in examples
// with data, in emptyExamples with nothing to show. The page calls no query hook of the generated client (it posts a
// registration, a mutation idle until the user submits, and reads the memberships only after that, through the query
// client), so it has no example: the axe check renders it as it first shows, and with both fields refused.
export const examples: Record<string, unknown> = {};

export const emptyExamples: Record<string, unknown> = {};
