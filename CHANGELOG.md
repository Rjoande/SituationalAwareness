# Changelog

## v0.2.4

### Fixed

- SA now loads on installs without EVE volumetric clouds.

## v0.2.3

### Changed

- The amber "X" badge on the locked weather icon is gone too.
- Collapsed strip: the temperature now comes before the weather icon.

## v0.2.2

### Added

- The collapsed strip now shows the weather icon and external temperature too. Follows the exact same show/hide/science-gate rule as the extended widget.

### Changed

- Improved the `SA_weather_cloudy_night_bare` icon art.
- The extended panel's weather widget no longer shows a "?" badge when the science gate has not credited.

## v0.2.1

### Added

- Weather Report: a short on-screen message confirms every press ("Weather Report: \<TYPE\> recorded"), or says so if the sample could not be written.

### Fixed

- Weather Report: typing in the note field no longer reaches the game (staging, SAS, brakes and the rest are locked while the field has keyboard focus).
- The panel scale set in Difficulty Settings is now global: one value shared by every save, instead of a per-save setting that came up at 1.0 again in each new game. The slider stays where it was; the value is stored in `PluginData/settings.cfg` next to the unit choices and window position.

## v0.2.0

### Added

- **Weather**: a WEATHER row reading the actual state of the sky (clear, cloudy, fog, rain, snow, thunderstorm, dust storm) from [EVE Volumetrics](https://github.com/LGhassen/EnvironmentalVisualEnhancements) when installed. Classified on functional properties (optical depth, EVE's own particle-render gate, particle fall speed and count, lightning configuration), never on layer names, so it works with any volumetrics pack rather than only the ones it was written against. Purely additive: no EVE, no row.
- Per-body weather names and icons (`WeatherFlavor.cfg`): Eve's rain is EXPLODIUM RAIN with its own icon, Vall's geyser mist is GEYSER, and the night-sky Clear/Cloudy icons show the moons a body actually has (none, one, several, or a gas giant filling the sky); inside a gas giant's electrified deck (Jool, Lindor, Sirona) the state reads CONVECTIVE STORM.
- **Weather forecast line** under the weather state, from the cloud layers' own on/off clocks: "rain ends within 1h 20m", "dust storm risk in 1d 4h", "dust storm possible for 1d 4h", "stable for 3d+", or just "changeable". Only what the clock can back: WHEN a weather system switches is exact, WHETHER its clouds land on you is not, hence "risk" and "possible". Where a system is always on (Kerbin) nothing can be timed and the line says "changeable" rather than guessing. Bodies whose weather never switches (Jool) show no line at all. A phase B that samples the cloud maps along their drift, to time things on Kerbin too, is planned. **Note on timing**: EVE's weather cycles are anchored to the save's universal time (UT 0), not to local time, the body's day or the calendar. They are identical in every save, and time warp advances them like everything else. Forecast times use the same calendar units as the panel's other countdowns.
- **Weather Report** (optional, off by default): a small diagnostic companion for when SA's weather does not match what you see. Switch it on in Difficulty Settings, accept the disclaimer shown in flight, and a pencil button appears in the corner of the weather section (which then stays visible, as UNKNOWN, even where SA has no weather to show): press the label that matches the sky and one sample (EVE's raw layer values, SA's own call, position and time) is written to a local CSV, plus a one-per-session list of loaded plugins. Nothing is ever sent automatically; how to share the files is described in the README. Declining the disclaimer switches the option back off.
- **Science gate** (Science/Career only, on by default, switchable in Difficulty Settings): EXT TEMP, PRESSURE, live GRAVITY and WEATHER read "???" / UNKNOWN on a body until the matching experiment has been *credited* there — thermometer, barometer, gravimeter, atmosphere analysis (or BDB's orbital weather observation) — any situation, any biome. The ASL gravity reference stays visible. Config-driven (`ScienceGate.cfg`, ModuleManager-patchable), so other instruments can open a readout without touching SA.

## v0.1.0

### Added

- **Three modes with dedicated dials**: Surface (local time with real timezones, day/night dial, sun elevation/azimuth), Orbit (eclipse/sunlit cycle, orbital period, countdown to the next transition), Tidal Lock (day-side/night-side, terminator distance) — selected automatically from the vessel's actual situation, including a suborbital/atmospheric extension of Surface mode with a configurable altitude threshold.
- **Local time harmonized with the game's own clock**: stock 6h days, Kronometer-modified days, RSS 24h — no hardcoded assumptions, mean-time calibrated against the equation of time on every body, not just home.
- **SOLAR TIME**: true apparent solar time at your exact position, cyclable to the equation of time on that body's own local time scale.
- **Hull temperature**: thermal-mass-weighted average across every part, colored by the single worst part's ratio to its own max temperature — catches a localized overheat even when the average stays cool. External temperature, pressure, solar flux, and live-sensed or fixed-reference gravity round out the environmental picture.
- **Multi-star aware**: resolves the relevant star for any body, including secondary stars in multi-star systems, with dedicated handling for flying through a star's own atmosphere or orbiting a star directly.
- **Near-pole handling**: local time, timezone and dial fall back to a "midnight sun" state instead of chasing an unstable longitude right at the pole.
- **CommNet signal strength** as a stock-styled colored LED, and a Sol counter for the local calendar day.
- **Collapsible strip mode** for a compact, always-visible readout, and a toolbar button (stock toolbar or Blizzy's, via ToolbarControl).
- **Cyclable units** everywhere it makes sense: °C/K, kPa/atm, g/m·s², decimal/DMS coordinates, km/degrees for terminator distance.
- **Adjustable panel scale** (Difficulty Settings), independent of the game's own UI Scale.
- **IVA monitor support** via [MFD Extension](https://github.com/Rjoande/MFD-Extension) (bay A): if both mods are installed, SA gets a screen on any compatible multi-function display prop, reachable without leaving the cockpit. Purely additive: nothing changes if MFD Extension isn't installed. It is just a compatibility framework for now: no actual feature added yet.
- **Localization**: English and Italian.
