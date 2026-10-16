# Third-party materials and rights review

This is an inventory for the entrant's review, not a certification of ownership or a replacement for
upstream license notices. MatchShift's public repository need not adopt an open-source license merely
because the hackathon requires public source. The owner chooses the project's license; dependency
license obligations still apply.

## Materials observed

| Material | Provenance / observed license metadata | Remaining action |
|---|---|---|
| Football events, fictional clubs and players | `MatchShift.SyntheticData` seeded simulator and in-source rosters | Owner confirms original creation and no unlicensed earlier project derivation. |
| MatchShift logo, pitch, charts and UI | SVG/React/CSS in this repository | Owner confirms rights to contributions; inspect final screenshots/recordings. |
| Screenshots in `docs/img` | Local project images, presented as application captures | Confirm provenance and remove incidental personal data or unlicensed material in final submission media. |
| React and React DOM | npm lockfile metadata: MIT | Retain applicable copyright/license notices for the exact distributed packages. |
| Azure.Identity 1.21.0 | Installed NuGet manifest: MIT | Preserve applicable package notices; review transitive dependencies. |
| Azure.Monitor.OpenTelemetry.AspNetCore 1.6.0 | Installed NuGet manifest: MIT | Preserve applicable package notices; review transitive dependencies. |
| Parquet.Net 6.1.0 | Installed NuGet manifest: MIT | Preserve applicable package notices; review transitive dependencies. |
| TypeScript, Vite, plugins, linters and test packages | Versions in npm lockfile and project files | Review the installed versions' licenses and bundled dependencies before distributing source/builds. |
| Barlow Condensed, Inter, JetBrains Mono web fonts | Google Fonts CSS import in `src/MatchShift.Web/src/index.css` | Confirm each font's applicable license; retain notices if bundling/self-hosting. Current fonts are remotely loaded. |
| Foundry, Azure and GitHub services | Optional integrations and development/deployment tooling | Entrant confirms authorized accounts, applicable terms and any sponsor-support restriction. |
| Future demo voice, music, logos or footage | No final video supplied for review | Use original/authorized media and retain permissions; review before upload. |

The original simulator and analytics build substantial functionality on dependencies; they are not a
resubmission of a dependency alone. Repository presence and license metadata cannot establish the
entrant's contractual ownership, contributor consent, asset rights or compliance with every license.
Keep any permissions and private contributor records outside the public submission.
