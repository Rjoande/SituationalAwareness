# Changelog

## [0.1.0] — First public release

### Added

- **Three modes with dedicated dials**: Surface (local time with real timezones, day/night dial, sun elevation/azimuth), Orbit (eclipse/sunlit
  cycle, orbital period, countdown to the next transition), Tidal Lock (day-side/night-side, terminator distance) — selected automatically from
  the vessel's actual situation, including a suborbital/atmospheric extension of Surface mode with a configurable altitude threshold.
- **Local time harmonized with the game's own clock**: stock 6h days, Kronometer-modified days, RSS 24h — no hardcoded assumptions, mean-time
  calibrated against the equation of time on every body, not just home.
- **SOLAR TIME**: true apparent solar time at your exact position, cyclable to the equation of time on that body's own local time scale.
- **Hull temperature**: thermal-mass-weighted average across every part, colored by the single worst part's ratio to its own max temperature —
  catches a localized overheat even when the average stays cool. External temperature, pressure, solar flux, and live-sensed or fixed-reference
  gravity round out the environmental picture.
- **Multi-star aware**: resolves the relevant star for any body, including secondary stars in multi-star systems, with
  dedicated handling for flying through a star's own atmosphere or orbiting a star directly.
- **Near-pole handling**: local time, timezone and dial fall back to a "midnight sun" state instead of chasing an unstable longitude right at the
  pole.
- **CommNet signal strength** as a stock-styled colored LED, and a Sol counter for the local calendar day.
- **Collapsible strip mode** for a compact, always-visible readout, and a toolbar button (stock toolbar or Blizzy's, via ToolbarControl).
- **Cyclable units** everywhere it makes sense: °C/K, kPa/atm, g/m·s², decimal/DMS coordinates, km/degrees for terminator distance.
- **Adjustable panel scale** (Difficulty Settings), independent of the game's own UI Scale.
- **Localization**: English and Italian, full parity.