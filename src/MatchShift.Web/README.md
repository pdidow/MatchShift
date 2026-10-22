# MatchShift web client

React, TypeScript and Vite render the match, insights, WHY?, SHOW ME, Evidence Replay, Agents and Broadcast Preview. The production build is served by `MatchShift.Api` from `../MatchShift.Api/wwwroot`; the separate Pages build uses a `/MatchShift/` base path.

Requires Node 22.12+ and npm. In this directory:

```bash
npm ci
npm run dev          # local UI; proxies /api to localhost:5002
npm test             # Vitest web regression suite
npx tsc -b --noEmit  # type-check
npm run build        # production bundle into MatchShift.Api/wwwroot
npm run build:pages  # static Pages bundle into dist
```

Run the API locally with `dotnet run --project src/MatchShift.Api --launch-profile http` from the repository root. No secrets are needed for local offline templates or `?demo=judge`; the latter uses previously recorded real workflows and never starts a live Foundry run. Optional live backend setup is documented in [FOUNDRY.md](../../docs/FOUNDRY.md).
