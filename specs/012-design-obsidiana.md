# 012 — Design: Obsidiana, dark and light

## Goal

Replace the stock shadcn look with the "Obsidiana" direction chosen from the five design
explorations: calm dark surfaces with glass panels, one violet accent, large display figures.
Every screen gets it, in a dark theme and a light one, and the person picks which, or follows
the system.

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | Where the look lives | CSS variables in `web/src/index.css`, one set on `:root` (light) and one on `.dark`, mapped into Tailwind's theme as today. Components keep using semantic classes (`bg-card`, `text-muted-foreground`); no page hard-codes a colour. |
| 2 | Palettes | **Dark (Obsidiana):** ground `#0B0C12` under a soft `#1B1F3B` glow, glass panels at 4.5% white with a 9% white edge, text `#F4F5F8`/`#9097A8`, accent `#8B73FF`, positive `#34D399`, negative `#FB7185`. **Light (Quartzo):** ground `#F1F2F7` under a `#DCD6FF` glow, frosted white panels, text `#0B0D12`/`#5A6172`, accent `#5B3FF0`, positive `#0A7552`, negative `#C42842`. Every text pair reaches 4.5:1. **(review)** |
| 3 | Theme choice | Three options: Claro, Escuro, Sistema (default). Stored in `localStorage` under `theme`, per device; not a server setting. A two-line script in `index.html` applies it before the first paint, so a dark page never flashes white. Unreadable storage behaves as Sistema. |
| 4 | Fonts | **Space Grotesk** for headings and large figures, **Manrope** for everything else, both with tabular figures. Self-hosted through `@fontsource-variable/*`: no request to Google on page load (ADR-013's direction on third parties). **(review)** |
| 5 | Shell | A top bar with the product name (still the `Finanças Pessoais` heading the 001 E2E expects), the theme selector and nothing else; on protected pages a glass sidebar with icon + label navigation, the signed-in user and Sair; on a phone the navigation becomes a horizontal strip. The health readout stays in the footer. |
| 6 | Shared parts | New `Card` (glass panel) and `PageHeader` (title, optional subtitle and actions) in `web/src/components`; `Amount` keeps its sign and tabular figures and colours arrivals with the positive token. Pages drop their own `max-w-5xl` containers and use these. |
| 7 | Dashboard | Same data and sections as 005, arranged as the exploration: the total balance as the hero figure with the month's result beside it, the monthly chart as a fading area, categories as a ring, the AI card and recent transactions as cards. The hero adds the invested total from `GET /api/investments/summary` when there are positions. **(review)** |
| 8 | Charts | Recharts stays. Series colours come from `--chart-income`, `--chart-expense` and `--chart-1..5`, which differ in lightness as well as hue in both themes; the tooltip style is shared. |
| 9 | What does not change | Routes, copy, `data-testid`s, native `<select>`s, table semantics and every API call except decision 7's. |

## Out of scope

- New features or copy changes beyond the dashboard hero (decision 7)
- A mobile-first redesign: pages must work at 390 px without horizontal scroll, not be rethought for it
- Animations beyond hover and focus transitions
- Storing the theme on the server

## Data model / API

None. Decision 7 reads an existing endpoint.

## Test plan

### Unit — `web`

1. The theme selector offers Claro, Escuro and Sistema; choosing Escuro puts `dark` on `<html>` and stores `dark`
2. Sistema follows `prefers-color-scheme` and stores `system`
3. An unreadable `localStorage` still renders the selector and applies Sistema
4. The shell keeps the `Finanças Pessoais` heading, the navigation links, `current-user` and Sair (existing tests keep passing)
5. `Amount` keeps the `amount` class and the printed sign; arrivals use `text-positive`
6. The dashboard shows the invested total when the summary has positions, and not otherwise

Existing tests that assert a class name the design replaces (`text-green-700`, `text-destructive`
on a card debt) are updated to the new token, not deleted.

### E2E — `web/e2e`

7. Choose Escuro, reload: still dark; choose Claro: light

All existing E2E tests keep passing unchanged (smoke, health and auth rely on the shell).

## End-to-end verification

```
bash scripts/verify.sh
bash scripts/verify-e2e.sh
```

Manual: open every page in both themes at 1440 px and at 390 px; nothing unreadable, clipped or
scrolling sideways; the dark theme loads without a white flash.

## Definition of done

- Both verify scripts green
- Tests 1–7 exist and pass
- Screenshots of the dashboard, transactions, import, investments and returns pages in both themes
