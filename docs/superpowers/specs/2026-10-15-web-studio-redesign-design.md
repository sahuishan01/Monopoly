# BoardEmpire Web "Studio" Redesign — Design

Date: 2026-10-15
Extends: `2026-10-14-web-client-design.md`

## Goal
Make the web client feel like a premium game: rich 3D and 2D graphics, motion, tactile
interaction, synthesized sound, multiple themes, and a first-class mobile layout.

## Non-goals
Changes to `Game.Core`, `Game.Net`, `Game.Protocol`, `Game.Server` or the wire protocol;
authored/glTF assets; audio asset files; new game modes.

## Constraints
- Views only animate; commands go through the existing session layer.
- Stack unchanged: Blazor WASM + three.js (version-pinned, vendored), Caddy static hosting.
- WebGL failure falls back to 2D. First load budget <= 25 MB.
- `dotnet test BoardEmpire.sln` stays green.

## 1. Themes
- Data files `wwwroot/themes/*.json`: CSS tokens, 3D palette (felt, tile materials, lights,
  fog, sky gradient), token/dice materials, sound pack id.
- Themes: Neon Noir (default), Classic Gold, Frost, Sakura.
- Applied with `data-theme` on `<html>` and `board3d.setTheme()`; persisted in localStorage;
  switchable live from Home and in-game. Adding a theme = adding one JSON file.

## 2. Graphics
### 3D (`wwwroot/js/3d/*.js`)
- `scene.js` board, tiles, tokens, buildings, ownership auras; `materials.js` PBR materials
  and procedural noise textures, procedural environment map; `fx.js` particles, bloom,
  vignette, money-flow trails; `camera.js` follow / overview / manual orbit with damping.
- Soft shadows, gradient skybox, extruded colour-group tiles with glow.
- Buildings spring in; tokens hop tile to tile; dice tumble and settle on the rolled faces.
- Mortgaged tiles dim; owned tiles show owner aura.
### 2D
- SVG board consuming the same theme tokens. Token hops, dice, property flip-ins, cash
  popups via CSS/Web Animations; canvas particle layer for confetti and payouts.
### Quality tiers
Auto / High / Low. Auto runs a short frame-time probe and defaults to Low on small or
low-power devices. Low disables post-processing and shadows and caps particles.

## 3. Interaction
- Tap/click tile opens flip-in property card; hover and long-press highlights.
- Drag-orbit, pinch-zoom, two-finger pan in 3D.
- Animated buy / auction / trade / build dialogs, turn banner, toast stack, event-log drawer.
- Desktop shortcuts: Space roll, B buy, E end turn, Esc close.
- `prefers-reduced-motion` and an in-app setting disable camera moves and heavy particles.

## 4. Sound
- `audio.js` WebAudio engine, synthesized per-theme SFX (dice, hop, cash, build, jail,
  auction hammer, turn chime) and a light generative ambient loop. No audio files.
- Master / SFX / music sliders and mute; context unlocked on first gesture; persisted.
- Driven by the same released events as the animations so sound and motion stay in step.

## 5. Mobile
- Board fills the viewport; HUD becomes a bottom sheet with thumb-reach actions.
- Touch targets >= 44 px, safe-area insets, portrait and landscape layouts.
- PWA manifest and icons for Add to Home Screen. No service-worker caching of API/WS.

## 6. Screens
- Home: animated 3D hero board behind Play-vs-bots / Create / Join cards and theme picker.
- Lobby: seat cards with avatars, ready pulses, rule summary.
- Play: board, HUD, sheets and dialogs as above.

## 7. Architecture
Data flow unchanged: `GameCommand -> RoomClient -> authority -> events -> replica ->
EventPacer -> view`.
New: `ThemeService`, `SettingsService` (quality, audio, motion, theme; localStorage) in
`Game.Web/Services`; JS modules above. `EventPacer` gains per-event timings and an
event->cue mapping (animation + sound) kept in `Game.WebClient` and unit-tested.
Views implement one JS API: `init`, `setTheme`, `setQuality`, `applyState`, `playEvent`,
`dispose`, plus click-intent callbacks to Blazor.

## 8. Testing
- Unit: pacer timing/cue mapping, settings persistence, theme JSON schema validation.
- Playwright: theme switch, 2D/3D toggle, non-blank canvas, mobile viewport run, mute/quality
  toggles.
- Manual: screenshots per theme at desktop and mobile sizes.
- `dotnet test BoardEmpire.sln` green.

## 9. Delivery
Existing deploy script; version bump and new tag per release rules; no Caddy changes.

## Risks
- Post-processing cost on weak mobile GPUs -> tiers, Low default on small devices.
- Vendoring EffectComposer/UnrealBloom alongside pinned three.js -> same version, licence file.
- Load size growth -> procedural assets only, lazy-load post-processing modules.
