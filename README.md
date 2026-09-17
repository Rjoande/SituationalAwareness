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
- **Weather** (needs [EVE volumetric clouds](https://github.com/LGhassen/EnvironmentalVisualEnhancements)): a weather section under the dial — icon plus state — reading the sky as CLEAR / CLOUDY / FOG / RAIN / SNOW / THUNDERSTORM / DUST STORM. It classifies on what the clouds physically *are* — optical depth, EVE's own particle-render gate, particle fall speed and count, whether the layer can produce lightning — never on layer names, so it works with any volumetrics pack instead of only the ones it was written against. Without EVE installed there is simply no section. Per-body names and icons come from `WeatherFlavor.cfg` (Eve's rain is EXPLODIUM RAIN, a gas giant's electrified deck is a CONVECTIVE STORM; the night sky shows the moons that body actually has), and both are ModuleManager-patchable by planet packs.
- **Weather forecast**: a line under the state — "rain ends within 1h 20m", "dust storm risk in 1d 4h", "dust storm possible for 1d 4h", "stable for 3d+", or just "changeable" — from the cloud layers' own on/off clocks. It promises only what the clock can back: *when* a weather system switches is exact, *whether* its clouds land on you is not, hence "risk" and "possible". Where a weather system is always on (Kerbin), nothing can be timed and the line honestly says so. A body whose weather never switches (Jool) shows no line. Note that EVE's weather cycles are anchored to the save's universal time, not to local time or the body's day: they are the same in every save, and time warp advances them like everything else.
- **Science gate** (Science/Career, on by default, switchable in Difficulty Settings): the panel does not show a number you have not measured yet. EXT TEMP, PRESSURE, live GRAVITY and WEATHER read "???" / UNKNOWN on a body until the matching experiment — thermometer, barometer, gravimeter, atmosphere analysis (or Bluedog's orbital weather observation) — has been credited there, in any situation or biome. Which experiment opens which readout is a config (`ScienceGate.cfg`), so a pack with its own instruments can patch it.
- **Weather Report** (optional, off by default): when SA's weather does not match what you see, one press writes a local sample you can send to the author, so the classifier can be tuned on real skies. See [Weather Report](#weather-report) below — nothing is ever sent automatically.
- **Localization**: English and Italian.
- **Collapsible strip mode** for a compact, always-visible readout — including the weather icon and external temperature when there is one to show — and a toolbar button (stock toolbar or Blizzy's, via ToolbarControl) to open/close the panel. Double click on the titlebar to collapse/expand.
- **Cyclable units** everywhere it makes sense: °C/K, kPa/atm, g/m·s², decimal/DMS coordinates, km/degrees for terminator distance. Click the value or label to switch.
- **Adjustable panel scale** (set in Difficulty Settings, but global: one value for every save), independent of the game's own UI Scale.

## Requirements

- Kerbal Space Program 1.12.5
- [ToolbarControl](https://github.com/linuxgurugamer/ToolbarControl)

Optional:

- [Environmental Visual Enhancements](https://github.com/LGhassen/EnvironmentalVisualEnhancements) (volumetric clouds) — enables the weather section. Everything else works without it.

## Installation

Copy the contents of this repository into your `GameData` folder, so you end up with `GameData/SituationalAwareness/...`.
Make sure ToolbarControl is installed alongside it.

## Weather Report

The weather classifier was tuned on one installation (the author's, JNSQ-Reborn + GEP with EVE volumetric clouds). If what SA shows does not match what you see out of the window, a report from your game is the most useful thing you can send. It is entirely opt-in and nothing leaves your computer unless you upload it yourself.

**How to enable it**

1. In game, open *Difficulty Settings → Situational Awareness* and tick **Enable weather report**.
2. The next time you are in flight (or as soon as you apply the settings while flying) a disclaimer appears, explaining exactly what gets written. Press **Accept** to continue, or **Decline** to switch the option back off. Consent is remembered once for all your saves; declining stores nothing, so ticking the option again shows the disclaimer again.
3. A small ✎ button appears in the corner of the weather section. It only exists while the report is enabled and accepted, so you can always tell at a glance whether the report is active. While the report is on, the weather section stays on screen even where SA has nothing to say (it then reads UNKNOWN, on an airless moon for instance), because "SA shows nothing here" is a report worth sending too.

**How to report**

1. When the sky disagrees with SA, click ✎. The Weather Report window shows what SA currently reads.
2. Optionally type a note (planet pack, what you see, anything useful), then press the label that matches the real sky: Clear, Cloud, Fog, Rain, Storm, Snow, Dust or Other. A short message at the top of the screen confirms the sample was written.
3. That is one sample. Report as many as you like, over as many sessions as you like; they all go to the same file.

Each sample records the raw values SA read from EVE's cloud layers, SA's own classification, your vessel and camera position, altitude, universal time, body, biome, sun elevation and solar flux, the cloud transmittance reported by WeatherDrivenSolarPanel if installed, plus your label and note. Once per session, at the first report, the plugin also writes the list of loaded plugins (names and versions only), because the same sample means different things under different visual packs. No personal information, account name, file path or system detail is collected.

**Where the files are**

```
GameData/SituationalAwareness/PluginData/WeatherReport/
    weather-report.csv        (and weather-report-2.csv, -3… after an update changed the columns)
    installed-mods.txt
```

**How to send them**

Upload both files here — no account needed:

**https://www.dropbox.com/request/9khup21i0c1xm1bk4w8o**

Dropbox will ask for a name and an email address before uploading. That is Dropbox's own requirement, not the plugin's: a nickname is fine, and Dropbox does not share the email address with the author — it is only used to send you an upload confirmation. The author sees the name you typed, the files, and nothing else. If you have several CSV files, upload them all together with `installed-mods.txt`, so they stay grouped.

You can switch the report off at any time from the same settings page; SA keeps working exactly as before, only the ✎ button goes away (and the weather section hides itself again where there is no weather to show). The files stay on your disk until you delete them.

## Future plans

- **Weather forecast, phase B.** The current forecast reads only the weather systems' clocks. The next step samples each system's cloud map along its drift for the coming hours, so that "stable for the next 2h" or "rain in 40m" can be said on bodies like Kerbin where a system is always on.
- **Proper multi-star solar flux.** Currently uses the game's raw `vessel.solarFlux`, which is always computed against the system's root star: for a body orbiting a secondary star in a multi-star system that may be a systematic error.
- **Selectable Sol-counting mode.** Alongside the current universal Sol 1 at UT 0, an optional per-body "milestone" mode (Sol 0 at the first landing ever on that body) and a JPL-style per-vessel mode (Sol 0 at that specific vessel's own first landing, mirroring real Mars rover mission-day counts).
- **Smaller polish**: correct marker direction on the orbit dial for retrograde orbits, an optional SCANsat map overlay for timezones, a visual cue distinguishing a timer's local-time base from its UT-based one, and a couple of cosmetic touches (rounded status-chip corners, a subtle LED glow).
- Switchable alternate skins.
- **RPM/MAS** support with a dedicated MFD screen.

## License

[MIT](LICENSE).

## Credits

Author: Rjoande. Built with the help of Claude Code.