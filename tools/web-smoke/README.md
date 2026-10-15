# Web client checks

Browser checks for `src/Game.Web` (Blazor WebAssembly) in real headless Chromium through Playwright.

## One command

```bash
cd tools/web-smoke && npm ci && cd ../..          # once
export CHROMIUM_PATH=~/.cache/ms-playwright/chromium-1234/chrome-linux/chrome   # if the bundled browser is missing
node tools/web-smoke/run-all.mjs --out=/tmp/web-checks
```

`run-all.mjs` does the following:

1. Runs the JS unit tests (`tests/js/*.test.mjs`) and `render-check`. Neither needs a server.
2. Starts its own stack through `harness.mjs`:
   - `dotnet publish src/Game.Web -c Release` into a temp dir. Skip this with `--serve=<published wwwroot>`.
   - `dotnet build src/Game.Server -c Release`.
   - Game.Server on a free `127.0.0.1` port, with a random `BoardEmpire__TokenSecret` and `BoardEmpire__BotDelaySeconds=0.3`.
   - A static server on a second free loopback port. It serves the wwwroot with SPA fallback and proxies `/api`, `/healthz` and WebSocket upgrades to the server.
3. Runs `smoke`, `e2e`, `home` and `mobile` against that stack.
4. Stops both servers, including on failure or Ctrl+C, and prints a summary. The exit code is 1 if anything failed.

Options:
- `--only=unit,render,smoke,e2e,home,mobile` or `--skip=...` picks which checks run.
- `--gl=egl|swiftshader` picks the GL backend.
- `--out=<dir>` sets where screenshots go: one folder per check, plus `server.log`.

Full run time on the 2-core arm64 dev host: about 20 minutes, most of it `mobile`.

Never point these scripts at the production Game.Server. The harness only ever starts and kills its own processes.

## GL on GPU-less hosts

WebGL runs on software GL. On arm64 the scripts default to Mesa llvmpipe through EGL: `--use-gl=angle --use-angle=gl-egl`. This needs `mesa-libEGL` and `mesa-dri-drivers`. Chromium's bundled SwiftShader crashes on aarch64 at the first draw, which shows up as blank canvases and "VALIDATE_STATUS false". Elsewhere the default is SwiftShader. Every script adds `--no-sandbox --disable-gpu-sandbox --ignore-gpu-blocklist`.

## The checks

| Script | Server | What it checks |
| --- | --- | --- |
| `render-check.mjs [out]` | none (imports `board3d.js` directly) | Every theme at Low/High renders a non-blank board. Camera framing fits the board at 900x640, 390x700, 360x560 and 740x330. Re-init after dispose. Console clean. |
| `smoke.mjs <base>` | yes | Guest -> Create room -> Add bot -> Ready -> Start -> Roll -> 2D/3D switch. |
| `e2e-check.mjs [out] --base=` | yes | Theme chip sets `data-theme` and persists. Hero context loss -> CSS hero. 3D canvas pixel sample is non-uniform. 3D and 2D tile click opens the property card. 2D/3D toggle both ways. Theme switch from the settings sheet during Play with no stuck colour transitions. Quality and mute are saved. `M` toggles mute. 3D context loss -> 2D plus a toast. Settings survive a reload. Phone viewport: theme, 2D tile tap, 3D render, no overflow. No console errors. |
| `home-check.mjs [out] --base=` | yes | Home/Lobby in 4 themes, desktop and phone. Hero mode policy. Touch targets. Keyboard tab order. Lifecycle Home -> Lobby -> Play x3 with no leaked WebGL contexts or loops. |
| `mobile-check.mjs [out] --base=` | yes | Phone profiles x 2D/3D: layout mode, board on screen, a turn played with taps, tile card as a bottom sheet, sheet drag, touch targets >= 44 px, PWA manifest. |
| `harness.mjs` | — | Library used by the scripts. Run it alone (`node tools/web-smoke/harness.mjs [--serve=<wwwroot>]`) to keep a stack up for manual poking. |
| `make-icons.mjs` | none | Regenerates the PWA icons. |

### Allowed console messages

Every check fails on console errors and warnings. The only exceptions are a few software-GL notices that say nothing about the app, listed in `BENIGN` in `e2e-check.mjs`:
- "GPU stall due to ReadPixels"
- "Automatic fallback to software WebGL"

### Rate limit

Game.Server limits guest logins to 30 per minute per IP. The full run stays under that because the checks run one after another. Running several in parallel against one server can get 429s.
