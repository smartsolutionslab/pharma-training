import { z } from 'zod';

// The edge answers index.html with a Content-Security-Policy whose script-src has no 'unsafe-eval' (docs/adr/0005). Zod
// would probe for eval with Function('') before its first parse, which the browser refuses and reports as a violation;
// without eval, Zod parses as it does after a failed probe, so nothing else changes. Imported first by main.ts.
z.config({ jitless: true });
