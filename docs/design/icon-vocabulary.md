# Icon vocabulary

A shared concept → [lucide-svelte](https://lucide.dev) icon map so sections and actions across the
app reuse the same iconography instead of reinventing it. Part of the app-wide section-consistency
work (#30); the section shell that consumes these is `SectionCard.svelte` (#29).

## Sections

| Concept | Icon | Notes |
|---|---|---|
| Price history / trends | `TrendingUp` | aliased `TrendIcon` in some files |
| Store URLs / links | `Link` | |
| Price alerts | `Bell` | `BellPlus` for the "create alert" action |
| Scrape history / activity log | `Activity` | |
| Custom fields / attributes | `ListTree` | |
| Comparison group | `Scale` | "compare" |
| Danger zone / destructive | `TriangleAlert` | pair with `accent="danger"` on `SectionCard` |
| Settings / configuration | `Settings` | |
| Notifications | `Bell` | |
| Health / status | `Activity` | scrape-health dashboards |

## Common actions

| Concept | Icon |
|---|---|
| Add / create | `Plus` |
| Edit | `Pencil` |
| Delete / remove | `Trash2` |
| Retry / refresh | `RotateCcw` |
| Pause / resume | `Pause` / `Play` |
| External link | `ExternalLink` |
| Expand / collapse | `ChevronDown` (rotates) / `ChevronRight` |
| Back | `ArrowLeft` |
| Loading | `LoaderCircle` (with `animate-spin`) |

## Entities

| Concept | Icon |
|---|---|
| Product | `Package` |
| Tag | `Tag` |
| Store | `Store` |
| Out of stock | `PackageX` |
| Anomaly / warning | `TriangleAlert` |

## Conventions

- Section-header icons render at `size={20}`; inline/action icons at `size={16}`.
- Don't introduce a second icon for a concept already in this table — add the concept here first if
  it's genuinely new.
- Section cards get their icon via the `icon` prop on `SectionCard` (`icon={TrendingUp}`), not
  hand-placed inside the heading.
