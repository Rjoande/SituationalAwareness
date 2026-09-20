Situational Awareness
=====================

A Kerbal Space Program mod that adds an avionics-style telemetry panel: local
time, day/night phase, sun position, atmosphere, weather and position, in
amber monospace to sit comfortably next to the stock UI.


What it does
------------

- Local time with real timezones, harmonized with whatever clock the game is
  using (stock 6h days, Kronometer-modified days, RSS 24h). A vector day/night
  dial shows the current phase at a glance.
- Three modes, each with its own dial and data set, selected automatically
  from the vessel's situation: Surface (local time, sun elevation/azimuth),
  Orbit (eclipse/sunlit cycle, orbital period, countdown to the next
  transition) and Tidal Lock (day side/night side, terminator distance).
  Surface mode extends into suborbital flight with a configurable altitude
  threshold.
- Solar time: true apparent solar time at your exact position, optionally
  cyclable to the equation of time.
- Hull temperature: thermal-mass-weighted average across every part, colored
  by the single worst part's ratio to its own maximum. Catches a localized
  overheat even when the average stays cool, which is what you want during
  reentry. External temperature, pressure, solar flux and gravity round out
  the picture.
- Multi-star aware: resolves the relevant star for any body, including
  secondary stars, with dedicated handling for flying through a star's
  atmosphere or orbiting one directly.
- CommNet signal strength as a stock-styled colored LED, and a Sol counter for
  the local calendar day.
- Weather (needs EVE volumetric clouds): icon plus state - clear, cloudy, fog,
  rain, snow, thunderstorm, dust storm - classified on what the clouds
  physically are (optical depth, particle rendering, fall speed, lightning),
  never on layer names, so it works with any volumetrics pack. Without EVE
  there is simply no weather section. Per-body names and icons live in
  WeatherFlavor.cfg and are ModuleManager-patchable by planet packs.
- Weather forecast: a line under the state ("rain ends within 1h 20m", "dust
  storm risk in 1d 4h", "stable for 3d+", "changeable") from the cloud layers'
  own on/off clocks. It promises only what the clock can back: when a weather
  system switches is exact, whether its clouds reach you is not, hence "risk"
  and "possible". EVE's weather cycles are anchored to the save's universal
  time, so they are the same in every save and time warp advances them
  normally.
- Science gate (Science/Career, on by default, switchable in Difficulty
  Settings): EXT TEMP, PRESSURE, live GRAVITY and WEATHER read "???" / UNKNOWN
  on a body until the matching experiment (thermometer, barometer, gravimeter,
  atmosphere analysis) has been credited there. Which experiment opens which
  readout is a config, ScienceGate.cfg.
- Weather Report (optional, off by default): when SA's weather does not match
  what you see, one press writes a local sample you can send to the author.
  Nothing is ever sent automatically. See below.
- Localization: English and Italian.
- Collapsible strip mode for a compact, always-visible readout (including
  weather icon and external temperature), and a toolbar button (stock toolbar
  or Blizzy's, via ToolbarControl). Double-click the title bar to
  collapse/expand.
- Cyclable units: C/K, kPa/atm, g/m.s^2, decimal/DMS coordinates, km/degrees.
  Click a value or its label to switch.
- Adjustable panel scale (set in Difficulty Settings, one value for every
  save), independent of the game's own UI scale.

Requirements
------------

- Kerbal Space Program 1.12.5
- ToolbarControl (https://github.com/linuxgurugamer/ToolbarControl)

Optional:

- Environmental Visual Enhancements, volumetric clouds
  (https://github.com/LGhassen/EnvironmentalVisualEnhancements) - enables the
  weather section. Everything else works without it.

Installation
------------

Extract this archive's contents into your GameData folder, so you end up with
GameData/SituationalAwareness/.... Make sure ToolbarControl is installed
alongside it.


Weather Report
--------------

The weather classifier was tuned on one installation (the author's: JNSQ-
Reborn + GEP with EVE volumetric clouds). If what SA shows does not match what
you see out of the window, a report from your game is the most useful thing
you can send. It is entirely opt-in and nothing leaves your computer unless
you upload it yourself.

How to enable it

1. In game, open Difficulty Settings > Situational Awareness and tick "Enable
   weather report".
2. The next time you are in flight (or as soon as you apply the settings while
   flying) a disclaimer explains exactly what gets written. Press Accept to
   continue, or Decline to switch the option back off. Consent is remembered
   once for all your saves; declining stores nothing.
3. A small pencil button appears in the corner of the weather section. It only
   exists while the report is enabled and accepted. While the report is on,
   the weather section stays on screen even where SA has nothing to say (it
   then reads UNKNOWN, on an airless moon for instance), because "SA shows
   nothing here" is a report worth sending too.

How to report

1. When the sky disagrees with SA, click the pencil. The Weather Report window
   shows what SA currently reads.
2. Optionally type a note (planet pack, what you see), then press the label
   that matches the real sky: Clear, Cloud, Fog, Rain, Storm, Snow, Dust or
   Other. A short message at the top of the screen confirms the sample was
   written.
3. That is one sample. Report as many as you like, over as many sessions as
   you like; they all go to the same file.

Each sample records the raw values SA read from EVE's cloud layers, SA's own
classification, your vessel and camera position, altitude, universal time,
body, biome, sun elevation and solar flux, the cloud transmittance reported by
WeatherDrivenSolarPanel if installed, plus your label and note. Once per
session, at the first report, the plugin also writes the list of loaded
plugins (names and versions only). No personal information, account name, file
path or system detail is collected.

Where the files are

  GameData/SituationalAwareness/PluginData/WeatherReport/
      weather-report.csv   (and weather-report-2.csv, -3... after an
                            update changed the columns)
      installed-mods.txt

How to send them

Upload both files here, no account needed:
https://www.dropbox.com/request/9khup21i0c1xm1bk4w8o

Dropbox will ask for a name and an email address before uploading. That is
Dropbox's own requirement, not the plugin's: a nickname is fine, and Dropbox
does not share the email address with the author. The author sees the name you
typed, the files, and nothing else. If you have several CSV files, upload them
all together with installed-mods.txt.

You can switch the report off at any time from the same settings page; SA
keeps working exactly as before. The files stay on your disk until you delete
them.


Future plans
------------

- Weather forecast, phase B: sample each weather system's cloud map along its
  drift, so "stable for the next 2h" or "rain in 40m" can be said on bodies
  like Kerbin where a system is always on.
- Proper multi-star solar flux.
- Selectable Sol-counting mode (per-body milestone, JPL-style per-vessel).
- Smaller polish: retrograde marker on the orbit dial, optional SCANsat
  timezone overlay, a few cosmetic touches.
- Switchable alternate skins.
- RPM/MAS support with a dedicated MFD screen.

License
-------

MIT. See LICENSE.txt.


Credits
-------

Author: Rjoande. Built with the help of Claude Code.
