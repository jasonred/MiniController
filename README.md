# miniController

A local, no-cloud **home control panel** with a Star Trek **LCARS** interface, built on
.NET 10 Blazor Server. Designed to run always-on (e.g. on a Raspberry Pi with a
touchscreen) and control multiple systems from one screen.

The first system is a **mini-split heat pump** (MrCool DIY 4th-gen `DIY-12-HP-WMAH-115C25`,
and other Midea-built units). It's controlled locally through an **SLWF-01Pro ESPHome
dongle** — no cloud, no app-open requirement.

## Why this exists

The MrCool/Cielo apps act as the thermostat: they poll temperature and command the unit,
so when the app is closed the unit stops regulating and overshoots (20°+). This app moves
the control loop onto an **always-on background service** and lets the unit regulate off its
own thermostat — set a target and walk away.

## Features

- **Systems dashboard** — LCARS status tiles, one per system, live state at a glance.
- **Climate control** — power, mode, fan, and setpoint. Optimistic UI (instant response) with
  a debounced setpoint (tap ± freely; it sends once you stop). Setpoint steps in your unit
  (1 °F or 0.5 °C).
- **Scheduler** — time-of-day rules (per weekday) that turn the unit on/off and set
  mode/temp/fan. Runs in the background, so schedules fire with nothing connected.
- **°C / °F preference** — app-wide, set on the Setup page (defaults to °F).
- **Always-on poller** — keeps the dashboard live and underpins scheduling/regulation.

## Requirements

- **.NET 10 SDK** (the app targets `net10.0`).
- A **SLWF-01Pro** (or other ESPHome AC dongle) plugged into the mini-split, joined to your
  Wi-Fi, on the **same subnet** as the machine running this app.

## Run it

```bash
dotnet run --project src/MiniController.Web
```

Open the printed URL (default **http://localhost:5010**).

> Always launch via `dotnet run` (or a published build) — **don't** run the raw `.exe` from
> `bin/`, as that skips Blazor's static-web-asset wiring and breaks styling/interactivity.

## First-time setup

Open **Setup** (`/system/climate/setup`):

1. **Display units** — choose °F or °C.
2. **ESPHome dongle (recommended)** — enter the dongle's IP/hostname (e.g. `10.0.0.167`) and
   **Save & connect**. That's it — no token/key needed.
3. The dashboard tile and Climate panel go live within a poll cycle.

Find the dongle's IP from your router's client list (it runs an ESPHome web server), or browse
to it directly to confirm it's reporting climate state.

> **Legacy path:** a stock Midea token/key LAN dongle is also supported (collapsed under
> "Legacy" on the Setup page) — discover on the LAN + fetch token/key via a NetHome Plus
> account. Most 4th-gen MrCool dongles are cloud-only, which is why the ESPHome dongle is the
> recommended route.

## Configuration files

Created at runtime next to the app (git-ignored, machine-specific):

- `device.json` — the connection (ESPHome host, or legacy IP/token/key).
- `schedules.json` — scheduler entries.
- `prefs.json` — display preferences (temperature unit).

## Project layout

| Path | What it is |
| --- | --- |
| `src/MiniController.Core` | Midea protocol library: crypto, V2/V3 framing, token/key handshake, AC command/state codec, LAN discovery, NetHome Plus token retrieval. |
| `src/MiniController.Web`  | Blazor Server app: LCARS UI, system registry, transports, scheduler, background services. |
| `src/MiniController.Web/Systems` | `ISystemModule` registry — each controllable system + the dashboard/rail wiring. |
| `src/MiniController.Web/Services` | `IClimateTransport` (`EspHomeClimateTransport`, `MideaLanTransport`), `DeviceManager`, `AppPreferences`, polling service. |
| `src/MiniController.Web/Scheduling` | Schedule model, store, and background runner. |
| `_ref/` | Python (`msmart-ng`) source the protocol was ported from. Reference only; not compiled. |

## Architecture

- **`ISystemModule` + `SystemRegistry`** — every controllable system registers in DI; the rail,
  dashboard tiles, and poller all build themselves from the registry. Adding a system doesn't
  touch the layout.
- **`IClimateTransport`** — abstracts how the mini-split is reached. `EspHomeClimateTransport`
  reads the ESPHome `/events` stream and commands `POST /climate/.../set`;
  `MideaLanTransport` speaks the raw Midea LAN protocol. `DeviceManager` picks one by settings.
- **Background services** — `StatusPollingService` (live state) and `ScheduleRunner` (timed
  actions) run regardless of any connected browser.

### Adding a new system

1. Implement `ISystemModule` (id, name, route, accent, tile state, poll hook).
2. Register it in `Program.cs`: `builder.Services.AddSingleton<ISystemModule, MyModule>();`
3. Add its control page(s) under `Components/Pages/Systems`.

The rail entry, dashboard tile, and polling come for free.

## Deploying to a Raspberry Pi (always-on)

Build a self-contained ARM64 publish on your dev box (no .NET install needed on the Pi; use a
64-bit Raspberry Pi OS):

```bash
dotnet publish src/MiniController.Web -c Release -r linux-arm64 --self-contained
```

Copy the output to the Pi, run it as a **systemd** service bound to `http://0.0.0.0:5010`, and
(optionally) auto-launch **Chromium in kiosk mode** at that URL for a wall panel. The Pi must
be on the same subnet as the dongle.

## Notes / caveats

- Outdoor temperature reads a bogus high value while the unit is off (hidden in the UI when out
  of range).
- The scheduler uses the server's local time — set the Pi's timezone accordingly.
- Schedule entries fire **at** their time (set-point model), not as hold-until-next-entry
  setback blocks.
