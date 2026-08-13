# Situational Awareness

A Kerbal Space Program mod that adds an avionics-style telemetry panel: local time, day/night phase, sun position, atmosphere, and position.

## What it does

- **Local time with real timezones**, harmonized with whatever clock the game itself is using (stock 6h days, Kronometer-modified days, RSS 24h). A vector day/night dial shows the current phase at a glance.
- **Three modes, each with its own dial and data set**: *Surface* (local time, day/night dial, sun elevation/azimuth), *Orbit* (eclipse/sunlit cycle, orbital period, countdown to the next transition), and *Tidal Lock* (day-side/night-side, terminator distance) selected automatically from the vessel's actual situation, including a suborbital/atmospheric extension of Surface mode with a configurable altitude threshold.
- **Solar time**: true apparent solar time at your exact position (not timezone-quantized like local time), optionally cyclable to the equation of
  time (the real gap between apparent and mean solar time, expressed on that body's own local time scale).
- **Hull temperature**: thermal-mass-weighted average across every part (the temperature the vessel would settle at if perfectly conductive), colored by the single worst part's ratio to its own max temperature. Catches a localized overheat even when the average stays cool, exactly the readout you want during reentry when the external-temperature row goes uninformative. External temperature, pressure, solar flux, and both live-sensed and fixed-reference gravity round out the environmental picture.
- **Multi-star aware**: correctly resolves the relevant star for any body, including secondary stars in multi-star systems, with dedicated handling for the edge cases that come with it, like flying through a star's own atmosphere or orbiting a star directly (no eclipse geometry applies there).
- **CommNet signal strength** as a stock-styled colored LED, and a Sol counter for the local calendar day.
- **Localization**: English and Italian.
- **Collapsible strip mode** for a compact, always-visible readout, and a toolbar button (stock toolbar or Blizzy's, via ToolbarControl) to open/close the panel. Double click on the titlebar to collapse/expand.
- **Cyclable units** everywhere it makes sense: °C/K, kPa/atm, g/m·s², decimal/DMS coordinates, km/degrees for terminator distance. Click the value or label to switch.
- **Adjustable panel scale** (Difficulty Settings), independent of the game's own UI Scale.

## Requirements

- Kerbal Space Program 1.12.5
- [ToolbarControl](https://github.com/linuxgurugamer/ToolbarControl)

## Installation

Copy the contents of this repository into your `GameData` folder, so you end up with `GameData/SituationalAwareness/...`.
Make sure ToolbarControl is installed alongside it.

## Future plans

- **Proper multi-star solar flux.** Currently uses the game's raw `vessel.solarFlux`, which is always computed against the system's root star: for a body orbiting a secondary star in a multi-star system that may be a systematic error.
- **A weather readout.** Already scoped against EVE Volumetrics/StockVolumetricClouds' public API (cloud coverage, type, precipitation), sampling the layers around the vessel to show current conditions, not a full weather simulation.
- **Selectable Sol-counting mode.** Alongside the current universal Sol 1 at UT 0, an optional per-body "milestone" mode (Sol 0 at the first landing ever on that body) and a JPL-style per-vessel mode (Sol 0 at that specific vessel's own first landing, mirroring real Mars rover mission-day counts).
- **Smaller polish**: correct marker direction on the orbit dial for retrograde orbits, an optional SCANsat map overlay for timezones, a visual cue distinguishing a timer's local-time base from its UT-based one, and a couple of cosmetic touches (rounded status-chip corners, a subtle LED glow).
- Switchable alternate skins.
- **RPM/MAS** support with a dedicated MFD screen.

## License

[MIT](LICENSE).

## Credits

Author: Rjoande. Built with the help of Claude Code.
