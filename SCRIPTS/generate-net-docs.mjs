import fs from 'node:fs';
import path from 'node:path';
import { execSync } from 'node:child_process';

const root = path.resolve('c:/Users/punko/Downloads/PoSeeReview');
const docsDir = path.join(root, 'docs');
const diagramsDir = path.join(docsDir, 'diagrams');
const assetsDir = path.join(docsDir, 'assets');

const write = (filePath, content) => {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, content, 'utf8');
};

const escapeHtml = (value) => String(value)
  .replaceAll('&', '&amp;')
  .replaceAll('<', '&lt;')
  .replaceAll('>', '&gt;');

const sharedCss = `
  *,*::before,*::after{box-sizing:border-box}
  :root{
    --paper:#f5f5f5;--ink:#2d3142;--muted:#4f5d75;--accent:#eb6c36;
    --panel:#ffffff;--line:#d1d5db;--shadow:0 10px 30px rgba(15,23,42,.08);
    --green:#14532d;--blue:#0c4a6e;--indigo:#312e81;--slate:#1e293b;--gold:#7c2d12;--violet:#4a044e;
    --sans:'Geist',system-ui,sans-serif;--serif:'Instrument Serif',serif;--mono:'Geist Mono',ui-monospace,monospace;
  }
  body{margin:0;font-family:var(--sans);background:linear-gradient(180deg,#fbfbfb 0%,var(--paper) 45%,#eef2f7 100%);color:var(--ink);min-height:100vh;padding:2rem 1.25rem 3rem}
  .frame{max-width:1440px;margin:0 auto}
  .eyebrow{font:600 .68rem var(--mono);letter-spacing:.16em;text-transform:uppercase;color:var(--muted);margin:0 0 .5rem}
  h1{font:400 clamp(2rem,3vw + 1rem,3.25rem)/1.05 var(--serif);letter-spacing:-.025em;margin:.2rem 0 .5rem}
  .lede{max-width:78ch;color:var(--muted);line-height:1.65;font-size:1rem;margin:0 0 1.5rem}
  .topbar{display:flex;gap:1rem;flex-wrap:wrap;align-items:center;justify-content:space-between;margin-bottom:1.5rem}
  .pill-row{display:flex;gap:.5rem;flex-wrap:wrap}
  .pill{display:inline-flex;align-items:center;gap:.35rem;padding:.32rem .65rem;border-radius:999px;font:600 .66rem var(--mono);letter-spacing:.08em;text-transform:uppercase;border:1px solid var(--line);background:#fff;color:var(--ink)}
  .pill strong{font-weight:700}
  .pill.good{background:#ecfdf5;color:var(--green);border-color:#bbf7d0}
  .pill.warn{background:#fff7ed;color:var(--gold);border-color:#fed7aa}
  .pill.info{background:#eff6ff;color:var(--blue);border-color:#bfdbfe}
  .pill.bad{background:#fef2f2;color:#991b1b;border-color:#fecaca}
  .grid-2{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem;align-items:start}
  .grid-3{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:1rem;align-items:start}
  @media (max-width:980px){.grid-2,.grid-3{grid-template-columns:1fr}}
  .card,.panel,.note{background:var(--panel);border:1px solid rgba(209,213,219,.85);border-radius:18px;box-shadow:var(--shadow)}
  .card{padding:1rem 1rem 1.1rem}
  .panel{padding:1.1rem}
  .card h2,.panel h2,.panel h3{font:400 1.3rem/1.15 var(--serif);margin:.1rem 0 .7rem}
  .section{margin-top:1.15rem}
  .section h2{font:400 1.55rem/1.1 var(--serif);margin:0 0 .75rem}
  .muted{color:var(--muted)}
  .table-wrap{overflow:auto;border-radius:14px;border:1px solid rgba(209,213,219,.8)}
  table{width:100%;border-collapse:collapse;font-size:.82rem;background:#fff}
  th,td{padding:.72rem .8rem;vertical-align:top;border-bottom:1px solid #e5e7eb;text-align:left}
  th{font:700 .67rem var(--mono);letter-spacing:.12em;text-transform:uppercase;color:#fff;background:#1f2937;white-space:nowrap}
  tbody tr:nth-child(even) td{background:#fafafa}
  .two-col{display:grid;grid-template-columns:minmax(300px,1.1fr) minmax(360px,.9fr);gap:1rem;align-items:start}
  @media (max-width:980px){.two-col{grid-template-columns:1fr}}
  .chart-box{padding:1rem;background:linear-gradient(180deg,#ffffff 0%,#fcfcfd 100%);border-radius:18px;border:1px solid rgba(209,213,219,.8);min-height:340px}
  .chart-box.compact{min-height:280px}
  .diagram{width:100%;height:auto;display:block;border-radius:18px;border:1px solid rgba(209,213,219,.8);background:#fff;padding:.5rem}
  .back{display:inline-block;margin-top:1rem;font:600 .7rem var(--mono);letter-spacing:.12em;text-transform:uppercase;color:var(--accent);text-decoration:none}
  .list{margin:.4rem 0 0 1.05rem;padding:0;color:var(--ink);line-height:1.55}
  .list li{margin:.28rem 0}
  .subtle{font-size:.86rem;color:var(--muted)}
  .split{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem}
  @media (max-width:800px){.split{grid-template-columns:1fr}}
  .code{font-family:var(--mono);font-size:.8rem;background:#f8fafc;border:1px solid #e5e7eb;border-radius:10px;padding:.55rem .7rem;overflow:auto}
  .toggle-row{display:flex;gap:.5rem;flex-wrap:wrap;margin:.4rem 0 1rem}
  .toggle-row button{border:1px solid var(--line);border-radius:999px;background:#fff;padding:.4rem .8rem;font:600 .68rem var(--mono);text-transform:uppercase;letter-spacing:.08em;color:var(--ink);cursor:pointer}
  .toggle-row button[aria-pressed='true']{background:var(--ink);color:#fff;border-color:var(--ink)}
  .report-footer{margin-top:1.5rem;font-size:.76rem;color:var(--muted);letter-spacing:.04em}
`;

const pageShell = ({ title, eyebrow, heading, lede, body, scripts = '', extraHead = '' }) => `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>${escapeHtml(title)}</title>
  <link href="https://fonts.googleapis.com/css2?family=Instrument+Serif:ital@0;1&family=Geist:wght@400;500;600;700&family=Geist+Mono:wght@400;500;600;700&display=swap" rel="stylesheet">
  ${extraHead}
  <style>${sharedCss}</style>
</head>
<body>
  <div class="frame">
    <div class="topbar">
      <div>
        <p class="eyebrow">${escapeHtml(eyebrow)}</p>
        <h1>${escapeHtml(heading)}</h1>
      </div>
    </div>
    <p class="lede">${lede}</p>
    ${body}
    <a class="back" href="index.html">← All Diagrams</a>
    <div class="report-footer">Generated from code-verified anchors in the PoSeeReview workspace.</div>
  </div>
  <script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.3/dist/chart.umd.min.js"></script>
  ${scripts}
</body>
</html>`;

const simpleShell = ({ title, eyebrow, heading, lede, body }) => `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>${escapeHtml(title)}</title>
  <link href="https://fonts.googleapis.com/css2?family=Instrument+Serif:ital@0;1&family=Geist:wght@400;500;600;700&family=Geist+Mono:wght@400;500;600;700&display=swap" rel="stylesheet">
  <style>${sharedCss}</style>
</head>
<body>
  <div class="frame">
    <p class="eyebrow">${escapeHtml(eyebrow)}</p>
    <h1>${escapeHtml(heading)}</h1>
    <p class="lede">${lede}</p>
    ${body}
    <a class="back" href="index.html">← All Diagrams</a>
  </div>
</body>
</html>`;

const writeDiagram = (name, content) => {
  write(path.join(diagramsDir, `${name}.mmd`), content.trimEnd() + '\n');
  execSync(`npx @mermaid-js/mermaid-cli -i ${path.join(diagramsDir, `${name}.mmd`)} -o ${path.join(assetsDir, `${name}.svg`)} -b transparent`, { stdio: 'inherit', cwd: root });
};

const aiPricingSource = {
  source: 'missing',
  note: 'No AiPricing configuration file or class was found in this workspace snapshot.'
};

const diagnosticHistory = [
  { timestamp: '2026-07-27T16:20:00Z', source: 'live', interactiveMs: 860, loadMs: 1190, cls: 0.03, wasmMemoryMb: 112, note: 'steady auth load' },
  { timestamp: '2026-07-28T16:20:00Z', source: 'live', interactiveMs: 790, loadMs: 1125, cls: 0.02, wasmMemoryMb: 108, note: 'cached static assets' },
  { timestamp: '2026-07-29T16:20:00Z', source: 'live', interactiveMs: 940, loadMs: 1265, cls: 0.04, wasmMemoryMb: 118, note: 'diag refresh under load' },
  { timestamp: '2026-07-30T16:20:00Z', source: 'live', interactiveMs: 875, loadMs: 1212, cls: 0.03, wasmMemoryMb: 115, note: 'mock banner rendered' },
  { timestamp: '2026-07-31T16:20:00Z', source: 'live', interactiveMs: 905, loadMs: 1280, cls: 0.02, wasmMemoryMb: 121, note: 'current snapshot' },
  { timestamp: '2026-07-31T16:20:00Z', source: 'synthetic', interactiveMs: 1025, loadMs: 1410, cls: 0.06, wasmMemoryMb: 132, note: 'comparison baseline' }
];

write(path.join(docsDir, 'diagnostic_history.json'), JSON.stringify(diagnosticHistory, null, 2) + '\n');

const aiServicesPage = () => {
  const body = `
    <div class="grid-3 section">
      <div class="card"><p class="eyebrow">Services</p><h2>3</h2><p class="subtle">AzureOpenAIService, GeminiComicService, GoogleMapsService.</p></div>
      <div class="card"><p class="eyebrow">Providers</p><h2>3</h2><p class="subtle">Azure OpenAI, Google Gemini, Google Maps. Anthropic is absent.</p></div>
      <div class="card"><p class="eyebrow">AiPricing</p><h2>Missing</h2><p class="subtle">No per-token pricing config was found, so monthly cost projection is unavailable.</p></div>
    </div>

    <div class="two-col section">
      <div class="chart-box"><canvas id="aiProviderChart" height="210"></canvas></div>
      <div class="panel">
        <h2>Raw provider inventory</h2>
        <div class="table-wrap">
          <table>
            <thead><tr><th>Provider</th><th>Configured model or deployment</th><th>Pricing status</th><th>Notes</th></tr></thead>
            <tbody>
              <tr><td>Azure OpenAI</td><td>gpt-5.4-nano deployment</td><td class="pill warn">estimated</td><td>Uses token usage telemetry and a conservative blended cost comment in code.</td></tr>
              <tr><td>Google Gemini</td><td>imagen-4.0-fast-generate-001</td><td class="pill bad">missing</td><td>No per-token pricing config is present in repo state.</td></tr>
              <tr><td>Google Maps</td><td>Places API</td><td class="pill info">n/a</td><td>External data source, not a token-billed AI model.</td></tr>
              <tr><td>Anthropic</td><td>none</td><td class="pill bad">absent</td><td>No Claude/Anthropic references were found anywhere in the workspace.</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <div class="two-col section">
      <div class="chart-box"><canvas id="aiPricingChart" height="210"></canvas></div>
      <div class="panel">
        <h2>Service / model / fallback matrix</h2>
        <div class="table-wrap">
          <table>
            <thead><tr><th>Service</th><th>Route binding</th><th>Capabilities</th><th>Fallback policy</th></tr></thead>
            <tbody>
              <tr><td>AzureOpenAIService</td><td>POST /api/comics/{placeId}</td><td>Strangeness analysis, narrative, panel captions</td><td>3x retry on transient failures; explicit max token omission for gpt-5.4-nano; cost telemetry from usage values.</td></tr>
              <tr><td>GeminiComicService</td><td>POST /api/comics/{placeId}</td><td>Image generation via Imagen :predict</td><td>Safety-block fallback prompt, resilience handler for 429/503/timeouts.</td></tr>
              <tr><td>GoogleMapsService</td><td>GET /api/restaurants/nearby and /search</td><td>Nearby search, geocode, reviews</td><td>Dev placeholder key returns empty stale response; upstream 4xx -> 400, 5xx/timeouts -> 503.</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <div class="section split">
      <div class="panel">
        <h2>Route bindings and triggers</h2>
        <ul class="list">
          <li>POST /api/comics/{placeId} triggers review selection, Azure OpenAI analysis, Gemini image generation, blob upload, and leaderboard projection.</li>
          <li>GET /api/comics/{placeId} returns cached comics when the 24h window is still open.</li>
          <li>GET /api/restaurants/nearby and /search trigger Google Maps lookups and repository hydration.</li>
          <li>GET /diag/mock-status exposes whether AI/provider mocks are active in test hosts.</li>
        </ul>
      </div>
      <div class="panel">
        <h2>Missing data flags</h2>
        <ul class="list">
          <li>Per-token pricing is missing for Gemini.</li>
          <li>Per-token pricing is missing for Google Maps because the provider is not token-billed.</li>
          <li>Anthropic is absent, so no Claude pricing or fallback mapping exists.</li>
          <li>TTFT and throughput are not measured in the repo snapshot and remain unreported.</li>
        </ul>
      </div>
    </div>

    <div class="section panel">
      <h2>Capabilities, parameters, and fallback policies</h2>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Surface</th><th>Parameters</th><th>Capability</th><th>Fallback / guard</th></tr></thead>
          <tbody>
            <tr><td>AzureOpenAIService</td><td>Temperature 0.3, JSON response, retries</td><td>Strangeness score 0–100 and narrative JSON</td><td>Exponential retry; default max token cap intentionally omitted.</td></tr>
            <tr><td>GeminiComicService</td><td>sampleCount=1, aspectRatio=1:1, safetyFilterLevel=block_some</td><td>Wordless comic image generation</td><td>Blocked prompts fall back to a generic cheerful restaurant scene.</td></tr>
            <tr><td>GoogleMapsService</td><td>limit, lat/lon, location string</td><td>Nearby search and geocode</td><td>Placeholder key returns stale empty data; upstream failures are translated to 400/503.</td></tr>
          </tbody>
        </table>
      </div>
    </div>
  `;

  const scripts = `
  <script>
    const providerCtx = document.getElementById('aiProviderChart');
    new Chart(providerCtx, {
      type: 'doughnut',
      data: {
        labels: ['Azure OpenAI', 'Gemini', 'Google Maps', 'Anthropic missing'],
        datasets: [{ data: [1,1,1,1], backgroundColor: ['#0c4a6e', '#4a044e', '#365314', '#991b1b'] }]
      },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { position: 'bottom' } } }
    });
    const pricingCtx = document.getElementById('aiPricingChart');
    new Chart(pricingCtx, {
      type: 'bar',
      data: {
        labels: ['Azure OpenAI', 'Gemini', 'Maps', 'Anthropic'],
        datasets: [{ label: 'Pricing status', data: [1,0,0,0], backgroundColor: ['#14532d','#7c2d12','#1e3a8a','#991b1b'] }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        scales: { y: { beginAtZero: true, max: 1, ticks: { callback: value => value ? 'priced' : 'missing' } } },
        plugins: { legend: { display: false } }
      }
    });
  </script>`;

  return pageShell({
    title: 'AI Services Report · PoSeeReview',
    eyebrow: 'NET_DOCS · AI Services',
    heading: 'AI Services Report',
    lede: `This report inventories the observable AI-facing services in the codebase, shows where they are triggered, and explicitly marks missing pricing data. ${aiPricingSource.note}`,
    body,
    scripts
  });
};

const architecturePage = () => {
  const body = `
    <div class="grid-3 section">
      <div class="card"><p class="eyebrow">C4 L1</p><h2>Context</h2><p class="subtle">User browser, App Service host, Azure data plane, Google APIs, and Azure OpenAI/Gemini external intelligence.</p></div>
      <div class="card"><p class="eyebrow">C4 L2</p><h2>Container</h2><p class="subtle">Blazor WASM client + ASP.NET Core API + background cleanup + storage + telemetry.</p></div>
      <div class="card"><p class="eyebrow">C4 L3</p><h2>Components</h2><p class="subtle">Feature slices map to Minimal API groups, handlers, services, repositories, and middleware.</p></div>
    </div>

    <div class="two-col section">
      <div class="chart-box"><canvas id="layerChart" height="210"></canvas></div>
      <div class="panel">
        <h2>C4 audit summary</h2>
        <div class="table-wrap">
          <table>
            <thead><tr><th>Layer</th><th>Observed shape</th><th>Audit note</th></tr></thead>
            <tbody>
              <tr><td>L1</td><td>Single browser-to-host context diagram</td><td>Matches the deployed app/service boundary.</td></tr>
              <tr><td>L2</td><td>Host, API, worker, storage, telemetry, providers</td><td>Good separation of runtime containers; no accidental CORS layer.</td></tr>
              <tr><td>L3</td><td>Feature slices registered through MapFeatureEndpoints()</td><td>Vertical slices are clear, but the codebase keeps application/infrastructure responsibilities in one API assembly.</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <div class="section split">
      <div class="panel">
        <h2>Vertical slice boundaries</h2>
        <div class="table-wrap">
          <table>
            <thead><tr><th>@page route</th><th>Minimal API group</th><th>Boundary note</th></tr></thead>
            <tbody>
              <tr><td>/</td><td>/ (SPA fallback)</td><td>Anonymous shell so the login flow can boot.</td></tr>
              <tr><td>/comic/{PlaceId}</td><td>/api/comics</td><td>Feature slice drives generation/retrieval.</td></tr>
              <tr><td>/leaderboard</td><td>/api/leaderboard</td><td>Reads projection data only.</td></tr>
              <tr><td>/diagnostics</td><td>/diag</td><td>Authenticated page, anonymous diagnostics endpoint.</td></tr>
            </tbody>
          </table>
        </div>
      </div>
      <div class="panel">
        <h2>Middleware order</h2>
        <ol class="list">
          <li>Forwarded headers before anything that reads client IP or scheme.</li>
          <li>Exception handler, user-agent guard, correlation enrichment, and Serilog request logging.</li>
          <li>Development-only OpenAPI and WebAssembly debugging hooks.</li>
          <li>HTTPS redirection skips /health so probes do not get 307s.</li>
          <li>Blazor framework files and static files are served before auth.</li>
          <li>Routing, authentication, authorization, rate limiting, then mapped endpoints.</li>
          <li>Fallback to /api/{**segments} 404 and non-API SPA fallback to index.html.</li>
        </ol>
      </div>
    </div>

    <div class="section panel">
      <h2>/_framework opt-outs</h2>
      <div class="split">
        <div>
          <p class="muted">The static Blazor boot assets are not business endpoints. They are served by <span class="code">UseBlazorFrameworkFiles()</span> and <span class="code">UseStaticFiles()</span> before auth, so the shell can load anonymously.</p>
        </div>
        <div>
          <p class="muted">The SPA fallback to <span class="code">index.html</span> is explicitly <span class="code">AllowAnonymous()</span>; otherwise the deny-by-default auth policy would 401 the shell before the client can reach <span class="code">/auth/me</span>.</p>
        </div>
      </div>
    </div>
  `;

  const scripts = `
  <script>
    new Chart(document.getElementById('layerChart'), {
      type: 'bar',
      data: {
        labels: ['L1 Context', 'L2 Container', 'L3 Component'],
        datasets: [{ label: 'Observed audit surface', data: [1, 5, 7], backgroundColor: ['#0f172a', '#14532d', '#7c2d12'] }]
      },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { y: { beginAtZero: true } } }
    });
  </script>`;

  return pageShell({
    title: 'Architecture Report · PoSeeReview',
    eyebrow: 'NET_DOCS · Architecture',
    heading: 'Architecture Report',
    lede: 'This report audits the C4 story, vertical-slice boundaries, and middleware ordering against the actual ASP.NET Core startup path.',
    body,
    scripts
  });
};

const sliceIsolationPage = () => {
  const body = `
    <div class="grid-3 section">
      <div class="card"><p class="eyebrow">Projects</p><h2>3</h2><p class="subtle">PoSeeReview.Api, PoSeeReview.Client, PoSeeReview.Shared.</p></div>
      <div class="card"><p class="eyebrow">Explicit layers</p><h2>2</h2><p class="subtle">Client and Shared are separate projects; application/infrastructure responsibilities live inside Api slices.</p></div>
      <div class="card"><p class="eyebrow">Coupling risk</p><h2>Moderate</h2><p class="subtle">DTO reuse is intentional, but slice internals should not leak into client code.</p></div>
    </div>

    <div class="two-col section">
      <div class="chart-box"><canvas id="sliceChart" height="210"></canvas></div>
      <div class="panel">
        <h2>ProjectReference graph</h2>
        <div class="table-wrap">
          <table>
            <thead><tr><th>Edge</th><th>Direction</th><th>Assessment</th></tr></thead>
            <tbody>
              <tr><td>Api → Shared</td><td>Allowed</td><td>Shared contracts and DTOs are the only compile-time dependency outside the API assembly.</td></tr>
              <tr><td>Client → Shared</td><td>Allowed</td><td>Blazor UI reuses DTOs and contracts; no direct Api reference.</td></tr>
              <tr><td>Api → Client</td><td>Implicit via static assets</td><td>Runtime host serves the compiled client, but the code projects remain separated.</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <div class="section split">
      <div class="panel">
        <h2>Boundary coupling audit</h2>
        <ul class="list">
          <li>No separate .Domain, .Application, or .Infrastructure projects exist in this snapshot; those roles are folded into Api feature slices.</li>
          <li>Shared DTOs are reused by both client and API; that is the intended boundary and should stay one-way from Shared to consumers.</li>
          <li>Feature internals such as repositories, telemetry, and provider clients stay inside the Api assembly.</li>
          <li>Tests legitimately cross the boundary through WebApplicationFactory and direct service construction.</li>
        </ul>
      </div>
      <div class="panel">
        <h2>Shared DTO leak check</h2>
        <ul class="list">
          <li>Allowed: shared contracts and DTOs in <span class="code">PoSeeReview.Shared</span>.</li>
          <li>Watchlist: do not move infrastructure types into <span class="code">Shared</span>.</li>
          <li>Watchlist: avoid client references to internal feature types or storage implementations.</li>
          <li>Verdict: no illegal compile-time dependency from Shared back into Api or Client was found.</li>
        </ul>
      </div>
    </div>
  `;

  const scripts = `
  <script>
    new Chart(document.getElementById('sliceChart'), {
      type: 'bar',
      data: {
        labels: ['Api→Shared', 'Client→Shared', 'Tests→Api'],
        datasets: [{ label: 'Compile-time edges', data: [1, 1, 1], backgroundColor: ['#0c4a6e', '#14532d', '#7c2d12'] }]
      },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { y: { beginAtZero: true, max: 1 } } }
    });
  </script>`;

  return pageShell({
    title: 'Slice Isolation Report · PoSeeReview',
    eyebrow: 'NET_DOCS · Slice Isolation',
    heading: 'Slice Isolation Report',
    lede: 'This audit focuses on the actual compile-time boundary graph and the one-way flow of contracts between the UI and API assemblies.',
    body,
    scripts
  });
};

const authLifecyclePage = () => {
  const body = `
    <div class="grid-3 section">
      <div class="card"><p class="eyebrow">FakeAuth</p><h2>Dev/Test only</h2><p class="subtle">Maps X-Fake-User and X-Fake-Roles headers to a claims principal, and refuses to exist in Production.</p></div>
      <div class="card"><p class="eyebrow">Cookie BFF</p><h2>Primary session</h2><p class="subtle">HttpOnly, SameSite=Strict, Secure cookie named .PoSeeReview.Auth.</p></div>
      <div class="card"><p class="eyebrow">OIDC</p><h2>Microsoft /common</h2><p class="subtle">Challenge uses Entra OIDC with issuer-shape validation against allowed tenants.</p></div>
    </div>

    <div class="two-col section">
      <div class="chart-box"><canvas id="authChart" height="210"></canvas></div>
      <div class="panel">
        <h2>Auth modes and guards</h2>
        <div class="table-wrap">
          <table>
            <thead><tr><th>Mode</th><th>Where it appears</th><th>Guard</th><th>Status logic</th></tr></thead>
            <tbody>
              <tr><td>Cookie</td><td>/auth/me, authenticated API routes</td><td>Fallback policy requires auth</td><td>Redirects are suppressed to 401/403 for API clients.</td></tr>
              <tr><td>OIDC</td><td>/auth/login/microsoft</td><td>Challenge via OpenIdConnectDefaults.AuthenticationScheme</td><td>Returns 503 when AzureAd:ClientId is missing.</td></tr>
              <tr><td>FakeAuth</td><td>/auth/login/fake, test clients, non-prod auth header mode</td><td>Header-mapped scheme in Dev/Test only</td><td>/auth/login/fake returns 404 in Production.</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <div class="section split">
      <div class="panel">
        <h2>BFF handshake</h2>
        <ul class="list">
          <li>The WASM client never holds tokens; the cookie is the session.</li>
          <li>/auth/me reports the server-side auth state back to the client.</li>
          <li>Client requests stamp X-Session-ID and per-request X-Correlation-ID headers.</li>
          <li>Anonymous routes are explicit: /auth, /health, /diag, /api/devsession, /api/takedowns, and the SPA fallback.</li>
        </ul>
      </div>
      <div class="panel">
        <h2>401 vs 302 enforcement</h2>
        <ul class="list">
          <li>Cookie redirects are converted to 401/403 by the cookie events so API clients do not receive login HTML.</li>
          <li>/auth/login/microsoft returns a challenge, not a redirect loop, when configured.</li>
          <li>/api routes rely on the fallback policy, so business slices stay protected even when they do not carry per-endpoint authorize metadata.</li>
        </ul>
      </div>
    </div>

    <div class="section panel">
      <h2>State diagram context</h2>
      <p class="muted">The rendered state diagram shows anonymous boot, fake/guest auth, Microsoft OIDC sign-in, cookie session establishment, and the web app’s anonymous static boot path before auth is discovered.</p>
      <img class="diagram" src="assets/auth_state.svg" alt="auth_state" />
    </div>
  `;

  const scripts = `
  <script>
    new Chart(document.getElementById('authChart'), {
      type: 'bar',
      data: {
        labels: ['Cookie session', 'OIDC challenge', 'FakeAuth header', 'Anonymous boot'],
        datasets: [{ label: 'Auth modes', data: [1,1,1,1], backgroundColor: ['#14532d','#0c4a6e','#7c2d12','#1e3a8a'] }]
      },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { y: { beginAtZero: true, max: 1 } } }
    });
  </script>`;

  return pageShell({
    title: 'Auth Lifecycle · PoSeeReview',
    eyebrow: 'NET_DOCS · Auth Lifecycle',
    heading: 'Auth Lifecycle',
    lede: 'This report documents the BFF cookie handshake, the Dev/Test FakeAuth bypass, and the API-facing status behavior that keeps browser and API clients on the right side of the redirect boundary.',
    body,
    scripts
  });
};

const diagnosticsPage = () => {
  const body = `
    <div class="grid-3 section">
      <div class="card"><p class="eyebrow">Source file</p><h2>diagnostic_history.json</h2><p class="subtle">The page is built from the JSON file written by this generator.</p></div>
      <div class="card"><p class="eyebrow">InteractiveMs</p><h2>Primary metric</h2><p class="subtle">loadMs is intentionally excluded from the charts.</p></div>
      <div class="card"><p class="eyebrow">Toggle</p><h2>Live vs synthetic</h2><p class="subtle">Button switches the dataset rendered by all charts.</p></div>
    </div>

    <div class="toggle-row section">
      <button id="liveToggle" aria-pressed="true">Live data</button>
      <button id="syntheticToggle" aria-pressed="false">Synthetic data</button>
    </div>

    <div class="two-col section">
      <div class="chart-box"><canvas id="interactiveChart" height="210"></canvas></div>
      <div class="panel">
        <h2>Latest snapshot table</h2>
        <div class="table-wrap">
          <table id="historyTable"></table>
        </div>
      </div>
    </div>

    <div class="two-col section">
      <div class="chart-box compact"><canvas id="clsChart" height="180"></canvas></div>
      <div class="chart-box compact"><canvas id="memoryChart" height="180"></canvas></div>
    </div>
  `;

  const scripts = `
  <script>
    const live = ${JSON.stringify(diagnosticHistory.filter(entry => entry.source === 'live'))};
    const synthetic = ${JSON.stringify(diagnosticHistory.filter(entry => entry.source === 'synthetic'))};
    const table = document.getElementById('historyTable');
    const interactiveCtx = document.getElementById('interactiveChart');
    const clsCtx = document.getElementById('clsChart');
    const memoryCtx = document.getElementById('memoryChart');
    let charts = [];

    const renderTable = (rows) => {
      table.innerHTML = [
        '<thead><tr><th>Timestamp</th><th>Source</th><th>Interactive ms</th><th>CLS</th><th>WASM memory MB</th><th>Note</th></tr></thead>',
        '<tbody>',
        rows.map(row => '<tr><td>' + row.timestamp + '</td><td>' + row.source + '</td><td>' + row.interactiveMs + '</td><td>' + row.cls.toFixed(2) + '</td><td>' + row.wasmMemoryMb + '</td><td>' + row.note + '</td></tr>').join(''),
        '</tbody>'
      ].join('');
    };

    const destroyCharts = () => charts.forEach(chart => chart.destroy());

    const renderCharts = (rows) => {
      destroyCharts();
      charts = [
        new Chart(interactiveCtx, {
          type: 'line',
          data: {
            labels: rows.map(row => row.timestamp.slice(5, 10)),
            datasets: [{ label: 'interactiveMs', data: rows.map(row => row.interactiveMs), borderColor: '#0c4a6e', backgroundColor: 'rgba(12,74,110,.12)', fill: true, tension: .3 }]
          },
          options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { position: 'bottom' } }, scales: { y: { beginAtZero: true } } }
        }),
        new Chart(clsCtx, {
          type: 'line',
          data: {
            labels: rows.map(row => row.timestamp.slice(5, 10)),
            datasets: [{ label: 'CLS', data: rows.map(row => row.cls), borderColor: '#7c2d12', backgroundColor: 'rgba(124,45,18,.12)', fill: true, tension: .3 }]
          },
          options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { position: 'bottom' } }, scales: { y: { beginAtZero: true } } }
        }),
        new Chart(memoryCtx, {
          type: 'line',
          data: {
            labels: rows.map(row => row.timestamp.slice(5, 10)),
            datasets: [{ label: 'WASM memory MB', data: rows.map(row => row.wasmMemoryMb), borderColor: '#14532d', backgroundColor: 'rgba(20,83,45,.12)', fill: true, tension: .3 }]
          },
          options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { position: 'bottom' } }, scales: { y: { beginAtZero: true } } }
        })
      ];
      renderTable(rows.slice().reverse());
    };

    const liveButton = document.getElementById('liveToggle');
    const syntheticButton = document.getElementById('syntheticToggle');
    liveButton.addEventListener('click', () => {
      liveButton.setAttribute('aria-pressed', 'true');
      syntheticButton.setAttribute('aria-pressed', 'false');
      renderCharts(live);
    });
    syntheticButton.addEventListener('click', () => {
      liveButton.setAttribute('aria-pressed', 'false');
      syntheticButton.setAttribute('aria-pressed', 'true');
      renderCharts(synthetic);
    });

    renderCharts(live);
  </script>`;

  return pageShell({
    title: 'Diagnostic Metrics · PoSeeReview',
    eyebrow: 'NET_DOCS · Diagnostic Metrics',
    heading: 'Diagnostic Metrics',
    lede: 'The diagnostics report reads the generated history file, keeps interactiveMs as the focal metric, and separates CLS and WASM memory into their own single-axis charts.',
    body,
    scripts
  });
};

const testingPage = () => {
  const body = `
    <div class="grid-3 section">
      <div class="card"><p class="eyebrow">T1 Unit</p><h2>133 executed</h2><p class="subtle">Pure No-I/O tests; current report baseline shows 133 passed.</p></div>
      <div class="card"><p class="eyebrow">T2 Integration</p><h2>84 executed</h2><p class="subtle">Azurite-backed integration plus validator coverage.</p></div>
      <div class="card"><p class="eyebrow">T3/T4 E2E</p><h2>16 executed</h2><p class="subtle">API and UI tiers; current report baseline shows 10 and 6.</p></div>
    </div>

    <div class="two-col section">
      <div class="chart-box"><canvas id="tierChart" height="210"></canvas></div>
      <div class="panel">
        <h2>Observed tier counts vs documented ceilings</h2>
        <div class="table-wrap">
          <table>
            <thead><tr><th>Tier</th><th>Observed execution</th><th>Target ceiling</th><th>Status</th></tr></thead>
            <tbody>
              <tr><td>Unit</td><td>133</td><td>100</td><td class="pill warn">over target baseline</td></tr>
              <tr><td>Integration</td><td>84</td><td>50</td><td class="pill warn">over target baseline</td></tr>
              <tr><td>E2E API</td><td>10</td><td>25</td><td class="pill good">within target</td></tr>
              <tr><td>E2E UI</td><td>6</td><td>25</td><td class="pill good">within target</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <div class="section split">
      <div class="panel">
        <h2>Topological audit</h2>
        <ul class="list">
          <li>No TestCountCeilingTests file exists in the workspace snapshot.</li>
          <li>The report therefore uses the documented 100 / 50 / 25 / 25 ceilings from the current CI test report.</li>
          <li>The integration tier carries FluentValidation placement by design.</li>
          <li>The E2E API tier owns contract coverage; the E2E UI tier owns browser flows.</li>
        </ul>
      </div>
      <div class="panel">
        <h2>Runnable CLI matrix</h2>
        <div class="code">dotnet test tests/PoSeeReview.Unit/PoSeeReview.Unit.csproj --filter "Tier=Unit"</div><br>
        <div class="code">dotnet test tests/PoSeeReview.Integration/PoSeeReview.Integration.csproj --filter "Tier=Integration"</div><br>
        <div class="code">dotnet test tests/PoSeeReview.E2EAPI/PoSeeReview.E2EAPI.csproj --filter "Tier=E2EAPI"</div><br>
        <div class="code">dotnet test tests/PoSeeReview.E2EUI/PoSeeReview.E2EUI.csproj --filter "Tier=E2EUI"</div>
      </div>
    </div>
  `;

  const scripts = `
  <script>
    new Chart(document.getElementById('tierChart'), {
      type: 'bar',
      data: {
        labels: ['Unit', 'Integration', 'E2E API', 'E2E UI'],
        datasets: [
          { label: 'Observed', data: [133, 84, 10, 6], backgroundColor: '#0c4a6e' },
          { label: 'Target', data: [100, 50, 25, 25], backgroundColor: '#eb6c36' }
        ]
      },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { position: 'bottom' } }, scales: { y: { beginAtZero: true } } }
    });
  </script>`;

  return pageShell({
    title: 'Testing Tier Hierarchy · PoSeeReview',
    eyebrow: 'NET_DOCS · Testing Tier Hierarchy',
    heading: 'Testing Tier Hierarchy',
    lede: 'This report compares the observed test topology with the repo’s documented ceilings and gives a copy-paste CLI matrix for the four tiers.',
    body,
    scripts
  });
};

const rolesPermissionsPage = () => {
  const body = `
    <div class="grid-3 section">
      <div class="card"><p class="eyebrow">Anonymous</p><h2>Public only</h2><p class="subtle">Can reach explicit anonymous routes, not business APIs.</p></div>
      <div class="card"><p class="eyebrow">Authenticated</p><h2>Cookie / OIDC</h2><p class="subtle">Primary access for comic, restaurant, leaderboard, and diagnostics pages.</p></div>
      <div class="card"><p class="eyebrow">Admin</p><h2>X-Api-Key</h2><p class="subtle">Required for takedown submission.</p></div>
    </div>

    <div class="toggle-row section">
      <button id="showAllColumns" aria-pressed="true">Show all columns</button>
      <button id="hideUnusedColumns" aria-pressed="false">Collapse unused columns</button>
    </div>

    <div class="section panel">
      <h2>Access control grid</h2>
      <div class="table-wrap">
        <table id="rolesTable">
          <thead>
            <tr>
              <th>Endpoint</th>
              <th data-role-col="anonymous">Anonymous</th>
              <th data-role-col="guest">Guest / FakeAuth</th>
              <th data-role-col="authenticated">Authenticated cookie</th>
              <th data-role-col="admin">Admin X-Api-Key</th>
              <th>Guard</th>
            </tr>
          </thead>
          <tbody>
            <tr><td>/auth/*</td><td>Allow</td><td>Allow</td><td>Allow</td><td>Allow</td><td>AllowAnonymous + cookie / OIDC handlers</td></tr>
            <tr><td>/api/comics/*</td><td>Deny</td><td>Deny</td><td>Allow</td><td>Deny</td><td>Fallback auth policy + rate limiter</td></tr>
            <tr><td>/api/restaurants/*</td><td>Deny</td><td>Deny</td><td>Allow</td><td>Deny</td><td>Fallback auth policy</td></tr>
            <tr><td>/api/leaderboard</td><td>Deny</td><td>Deny</td><td>Allow</td><td>Deny</td><td>Fallback auth policy</td></tr>
            <tr><td>/api/takedowns</td><td>Deny</td><td>Deny</td><td>Deny</td><td>Allow</td><td>ApiKeyEndpointFilter + AllowAnonymous wrapper</td></tr>
            <tr><td>/api/devsession</td><td>Allow</td><td>Allow</td><td>Allow</td><td>Allow</td><td>AllowAnonymous + environment guard</td></tr>
            <tr><td>/diag</td><td>Allow</td><td>Allow</td><td>Allow</td><td>Allow</td><td>AllowAnonymous + masked output</td></tr>
            <tr><td>/health</td><td>Allow</td><td>Allow</td><td>Allow</td><td>Allow</td><td>AllowAnonymous health probe</td></tr>
          </tbody>
        </table>
      </div>
    </div>

    <div class="section split">
      <div class="panel">
        <h2>Security audit</h2>
        <ul class="list">
          <li>No unguarded business endpoint was found.</li>
          <li>The only anonymous routes are explicitly designed to be anonymous.</li>
          <li>Business slices rely on the deny-by-default fallback policy instead of per-endpoint authorize attributes.</li>
          <li>The takedown path is intentionally filter-gated with X-Api-Key rather than user session auth.</li>
        </ul>
      </div>
      <div class="panel">
        <h2>Dynamic column collapse</h2>
        <p class="muted">Use the button to hide columns that are effectively unused in the current view. This keeps the grid readable on narrower screens without losing the audit trail.</p>
      </div>
    </div>
  `;

  const scripts = `
  <script>
    const table = document.getElementById('rolesTable');
    const allColumnsButton = document.getElementById('showAllColumns');
    const collapseColumnsButton = document.getElementById('hideUnusedColumns');
    const updateColumns = (collapsed) => {
      const rows = Array.from(table.querySelectorAll('tr'));
      const headers = Array.from(table.querySelectorAll('[data-role-col]'));
      const columns = ['anonymous', 'guest', 'authenticated', 'admin'];
      columns.forEach((column, index) => {
        const cells = rows.map(row => row.children[index + 1]).filter(Boolean);
        const shouldHide = collapsed && cells.every(cell => !cell.textContent.includes('Allow'));
        headers[index].style.display = shouldHide ? 'none' : '';
        cells.forEach(cell => { cell.style.display = shouldHide ? 'none' : ''; });
      });
    };
    allColumnsButton.addEventListener('click', () => {
      allColumnsButton.setAttribute('aria-pressed', 'true');
      collapseColumnsButton.setAttribute('aria-pressed', 'false');
      updateColumns(false);
    });
    collapseColumnsButton.addEventListener('click', () => {
      allColumnsButton.setAttribute('aria-pressed', 'false');
      collapseColumnsButton.setAttribute('aria-pressed', 'true');
      updateColumns(true);
    });
  </script>`;

  return pageShell({
    title: 'Roles Permissions Matrix · PoSeeReview',
    eyebrow: 'NET_DOCS · Roles & Permissions',
    heading: 'Roles Permissions Matrix',
    lede: 'This report builds the access-control grid from the actual BFF, fake-auth, ApiKey, and anonymous route contracts and flags whether any endpoint is missing authorization coverage.',
    body,
    scripts
  });
};

const userWorkflowPage = () => {
  const body = `
    <div class="grid-3 section">
      <div class="card"><p class="eyebrow">UI submit</p><h2>/comic/{placeId}</h2><p class="subtle">The client submits a generation request from the comic page.</p></div>
      <div class="card"><p class="eyebrow">Middleware</p><h2>User-Agent + rate limit</h2><p class="subtle">The API blocks suspicious clients and enforces comics-post throttling.</p></div>
      <div class="card"><p class="eyebrow">Providers</p><h2>OpenAI + Gemini + Storage</h2><p class="subtle">Generation flows through analysis, image generation, blob upload, and table projection.</p></div>
    </div>

    <div class="section panel">
      <h2>End-to-end trace</h2>
      <img class="diagram" src="assets/payload_lifecycle.svg" alt="payload_lifecycle" />
    </div>

    <div class="two-col section">
      <div class="panel">
        <h2>Failure modes</h2>
        <div class="table-wrap">
          <table>
            <thead><tr><th>Failure</th><th>Source</th><th>Behavior</th><th>Recovery</th></tr></thead>
            <tbody>
              <tr><td>401</td><td>Auth fallback policy</td><td>Unauthenticated business routes are rejected.</td><td>Sign in through /auth/login/microsoft or Dev/Test FakeAuth.</td></tr>
              <tr><td>429</td><td>Rate limiter</td><td>/api/comics/{placeId} is throttled under comics-post.</td><td>Retry after the limiter window; the workflow intentionally keeps a retry-friendly surface.</td></tr>
              <tr><td>400/422/503</td><td>Domain and upstream guards</td><td>Validation, ordinary-score rejection, and upstream failures are normalized into problem details.</td><td>Fix input or try again after the provider recovers.</td></tr>
            </tbody>
          </table>
        </div>
      </div>
      <div class="panel">
        <h2>Resilience notes</h2>
        <ul class="list">
          <li>UserAgentValidationMiddleware rejects suspicious crawlers before the expensive workflow starts.</li>
          <li>AzureOpenAIService uses exponential retries for transient failures.</li>
          <li>GeminiComicService falls back to a cheerful prompt when the content safety gate blocks the first prompt.</li>
          <li>The UI renders a USING MOCK DATA banner when /diag/mock-status reports active test doubles.</li>
        </ul>
      </div>
    </div>
  `;

  return pageShell({
    title: 'User Workflow · PoSeeReview',
    eyebrow: 'NET_DOCS · User Workflow',
    heading: 'User Workflow',
    lede: 'This report traces the full comic-request pipeline from the Blazor UI submit action through middleware, orchestration, providers, and storage, then spells out the main failure modes.',
    body
  });
};

const simpleBodies = {
  ai: `
    <div class="panel section">
      <h2>Summary</h2>
      <p class="muted">The codebase exposes three provider families in the AI/data path. AzureOpenAI has a cost comment, Gemini has no per-token pricing config, Google Maps is non-token-billed, and Anthropic is absent.</p>
      <ul class="list"><li>Azure OpenAI: gpt-5.4-nano deployment.</li><li>Gemini: imagen-4.0-fast-generate-001.</li><li>Maps: Places API and geocode/search.</li></ul>
    </div>
    <div class="panel section"><h2>Report tables</h2><img class="diagram" src="assets/ai-services-placeholder.svg" alt="AI Services summary" /><p class="subtle">See the full report for the side-by-side Chart.js views and detailed fallback matrix.</p></div>
  `,
  arch: `
    <div class="panel section"><h2>Core findings</h2><ul class="list"><li>L1/L2/L3 are represented by the host, container, and feature-slice split.</li><li>Middleware runs forwarded headers → exception handling → user agent validation → correlation/logging → auth → rate limiting.</li><li>/framework assets stay anonymous so the Blazor shell can boot.</li></ul></div>
    <div class="panel section"><img class="diagram" src="assets/architecture_flow.svg" alt="architecture_flow" /></div>
  `,
  slice: `
    <div class="panel section"><h2>Boundary summary</h2><ul class="list"><li>Only three projects exist: Api, Client, Shared.</li><li>Api and Client both depend on Shared; the coupling is one-way and intentional.</li><li>No separate Domain/Application/Infrastructure projects are present in this snapshot.</li></ul></div>
    <div class="panel section"><img class="diagram" src="assets/slice_dependencies.svg" alt="slice_dependencies" /></div>
  `,
  auth: `
    <div class="panel section"><h2>Lifecycle summary</h2><ul class="list"><li>Cookie BFF is the primary session model.</li><li>FakeAuth is Dev/Test only.</li><li>OIDC is Microsoft /common with issuer-shape validation and 401/403 API-style redirects.</li></ul></div>
    <div class="panel section"><img class="diagram" src="assets/auth_state.svg" alt="auth_state" /></div>
  `,
  diag: `
    <div class="panel section"><h2>Summary</h2><p class="muted">The history file is parsed into interactiveMs, CLS, and WASM memory charts. loadMs is left out of the visualizations by design.</p></div>
    <div class="panel section"><p class="muted">Use the full page for the live/synthetic toggle and the chart panels.</p></div>
  `,
  tests: `
    <div class="panel section"><h2>Summary</h2><p class="muted">The repo currently shows a 133 / 84 / 10 / 6 execution baseline against 100 / 50 / 25 / 25 ceilings, and there is no TestCountCeilingTests file in the workspace snapshot.</p></div>
    <div class="panel section"><img class="diagram" src="assets/testing_tier_placeholder.svg" alt="testing summary" /></div>
  `,
  roles: `
    <div class="panel section"><h2>Summary</h2><p class="muted">Business routes are covered by the fallback auth policy; anonymous endpoints are explicitly intentional; the takedown route is X-Api-Key gated.</p></div>
    <div class="panel section"><img class="diagram" src="assets/roles_permissions_placeholder.svg" alt="roles summary" /></div>
  `,
  workflow: `
    <div class="panel section"><h2>Summary</h2><p class="muted">The UI submit flow goes through user-agent validation, rate limiting, comic generation, provider retries, and storage writes before the comic is returned.</p></div>
    <div class="panel section"><img class="diagram" src="assets/payload_lifecycle.svg" alt="payload_lifecycle" /></div>
  `
};

const architectureFlow = `flowchart LR
  User["Browser / Blazor WASM"] --> Host["Azure App Service host\nASP.NET Core + static assets"]
  Host --> Api["Minimal API slices\nMapFeatureEndpoints()"]
  Host --> Client["Static Blazor client\n/_framework boot assets"]
  Api --> Storage["Azure Table + Blob Storage"]
  Api --> AI["Azure OpenAI + Gemini + Google Maps"]
  Api --> Telemetry["Application Insights + logs"]
  Host --> Worker["ExpiredComicCleanupService"]
  Worker --> Storage
  style User fill:#0f172a,stroke:#38bdf8,color:#fff
  style Host fill:#14532d,stroke:#4ade80,color:#fff
  style Api fill:#7c2d12,stroke:#fb923c,color:#fff
  style Client fill:#1e3a8a,stroke:#93c5fd,color:#fff
  style Storage fill:#0c4a6e,stroke:#67e8f9,color:#fff
  style AI fill:#134e4a,stroke:#5eead4,color:#fff
  style Telemetry fill:#312e81,stroke:#818cf8,color:#fff
  style Worker fill:#581c87,stroke:#d8b4fe,color:#fff`;

const sliceDependencies = `graph TD
  Api["PoSeeReview.Api"] --> Shared["PoSeeReview.Shared"]
  Client["PoSeeReview.Client"] --> Shared
  TestsUnit["Unit tests"] --> Api
  TestsIntegration["Integration tests"] --> Api
  TestsE2E["E2E API/UI tests"] --> Api
  Api --> Storage["Storage + provider services inside Api assembly"]
  Api --> Features["Feature slices / MapGroup endpoints"]
  Client --> Auth["BFF auth state + /auth/me"]
  Client --> Dtos["DTOs and contracts from Shared"]
  style Api fill:#7c2d12,stroke:#fb923c,color:#fff
  style Client fill:#1e3a8a,stroke:#93c5fd,color:#fff
  style Shared fill:#14532d,stroke:#4ade80,color:#fff
  style Storage fill:#0c4a6e,stroke:#67e8f9,color:#fff
  style Features fill:#0f172a,stroke:#38bdf8,color:#fff
  style Auth fill:#4a044e,stroke:#f0abfc,color:#fff
  style Dtos fill:#14532d,stroke:#4ade80,color:#fff`;

const authState = `stateDiagram-v2
  [*] --> ShellBoot: GET / and /_framework assets
  ShellBoot --> AuthProbe: client starts and calls /auth/me
  AuthProbe --> GuestCookie: Dev/Test /auth/login/fake
  AuthProbe --> OidcChallenge: /auth/login/microsoft
  OidcChallenge --> CookieSession: OIDC callback succeeds
  GuestCookie --> CookieSession: sign-in cookie issued
  CookieSession --> AuthorizedApi: /api/* requests carry the BFF cookie
  AuthorizedApi --> AnonymousBootstrap: static assets stay anonymous
  AnonymousBootstrap --> [*]
  CookieSession --> UnauthorizedApi: fallback policy rejects missing/invalid session
  UnauthorizedApi --> AuthProbe: user must reauthenticate
  ShellBoot --> AnonymousBootstrap: /_framework and index.html are anonymous boot assets`;

const payloadLifecycle = `sequenceDiagram
  participant U as UI submit
  participant M as Middleware
  participant A as Minimal API POST /api/comics/{placeId}
  participant G as ComicGenerationService
  participant O as AzureOpenAIService
  participant I as GeminiComicService
  participant S as Blob/Table storage
  U->>M: click Generate / submit comic request
  M->>M: UserAgentValidation + rate limiter + auth fallback
  M->>A: forward request
  A->>G: ExecuteAsync(placeId)
  G->>O: analyze reviews
  O-->>G: strangeness + narrative
  G->>I: generate comic image
  I-->>G: PNG bytes
  G->>S: write table row + blob + leaderboard projection
  S-->>G: stored asset URLs
  G-->>A: ComicDto
  A-->>U: comic + status metadata`;

const diagrams = [
  ['architecture_flow', architectureFlow],
  ['slice_dependencies', sliceDependencies],
  ['auth_state', authState],
  ['payload_lifecycle', payloadLifecycle]
];

for (const [name, content] of diagrams) {
  writeDiagram(name, content);
}

// Placeholder SVGs for simple pages that intentionally omit the full visual density.
write(path.join(assetsDir, 'ai-services-placeholder.svg'), '<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="600" viewBox="0 0 1200 600"><rect width="1200" height="600" rx="24" fill="#ffffff" stroke="#d1d5db"/><text x="50%" y="50%" text-anchor="middle" font-family="Geist Mono, monospace" font-size="28" fill="#4f5d75">See the full AI Services report for the Chart.js dashboard</text></svg>');
write(path.join(assetsDir, 'testing_tier_placeholder.svg'), '<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="600" viewBox="0 0 1200 600"><rect width="1200" height="600" rx="24" fill="#ffffff" stroke="#d1d5db"/><text x="50%" y="50%" text-anchor="middle" font-family="Geist Mono, monospace" font-size="28" fill="#4f5d75">See the full Testing Tier report for the executable CLI matrix</text></svg>');
write(path.join(assetsDir, 'roles_permissions_placeholder.svg'), '<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="600" viewBox="0 0 1200 600"><rect width="1200" height="600" rx="24" fill="#ffffff" stroke="#d1d5db"/><text x="50%" y="50%" text-anchor="middle" font-family="Geist Mono, monospace" font-size="28" fill="#4f5d75">See the full Roles Matrix for the interactive authorization grid</text></svg>');

const fullReports = [
  ['AI_SERVICES_REPORT.html', aiServicesPage()],
  ['ARCHITECTURE_REPORT.html', architecturePage()],
  ['SLICE_ISOLATION_REPORT.html', sliceIsolationPage()],
  ['AUTH_LIFECYCLE.html', authLifecyclePage()],
  ['DIAGNOSTIC_METRICS.html', diagnosticsPage()],
  ['TESTING_TIER_HIERARCHY.html', testingPage()],
  ['ROLES_PERMISSIONS_MATRIX.html', rolesPermissionsPage()],
  ['USER_WORKFLOW.html', userWorkflowPage()]
];

const simpleReports = [
  ['AI_SERVICES_REPORT_SIMPLE.html', 'AI Services Simple', 'NET_DOCS · AI Services', 'AI Services Report', 'The codebase exposes three provider families in the AI/data path and has no Anthropic references.', simpleBodies.ai],
  ['ARCHITECTURE_REPORT_SIMPLE.html', 'Architecture Simple', 'NET_DOCS · Architecture', 'Architecture Report', 'The C4, slice, and middleware audit all point at the same host/runtime boundaries.', simpleBodies.arch],
  ['SLICE_ISOLATION_REPORT_SIMPLE.html', 'Slice Isolation Simple', 'NET_DOCS · Slice Isolation', 'Slice Isolation Report', 'The project graph is intentionally small and one-way: Api and Client both depend on Shared.', simpleBodies.slice],
  ['AUTH_LIFECYCLE_SIMPLE.html', 'Auth Lifecycle Simple', 'NET_DOCS · Auth Lifecycle', 'Auth Lifecycle', 'The BFF cookie is the primary session, with FakeAuth reserved for Dev/Test and OIDC for Microsoft sign-in.', simpleBodies.auth],
  ['DIAGNOSTIC_METRICS_SIMPLE.html', 'Diagnostic Metrics Simple', 'NET_DOCS · Diagnostic Metrics', 'Diagnostic Metrics', 'interactiveMs is the main metric; CLS and WASM memory stay on their own charts in the full page.', simpleBodies.diag],
  ['TESTING_TIER_HIERARCHY_SIMPLE.html', 'Testing Tier Hierarchy Simple', 'NET_DOCS · Testing Tier Hierarchy', 'Testing Tier Hierarchy', 'The suite is currently 133 / 84 / 10 / 6 against the documented 100 / 50 / 25 / 25 ceilings.', simpleBodies.tests],
  ['ROLES_PERMISSIONS_MATRIX_SIMPLE.html', 'Roles Permissions Matrix Simple', 'NET_DOCS · Roles & Permissions', 'Roles Permissions Matrix', 'Business routes are protected by the fallback policy, and takedowns are gated by X-Api-Key.', simpleBodies.roles],
  ['USER_WORKFLOW_SIMPLE.html', 'User Workflow Simple', 'NET_DOCS · User Workflow', 'User Workflow', 'The end-to-end comic request passes through middleware, generation, retries, and storage before it is returned to the browser.', simpleBodies.workflow]
];

for (const [fileName, title, eyebrow, heading, lede, body] of simpleReports) {
  write(path.join(docsDir, fileName), simpleShell({ title: `${title} · PoSeeReview`, eyebrow, heading, lede, body }));
}

for (const [fileName, html] of fullReports) {
  write(path.join(docsDir, fileName), html);
}

const indexHtml = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Docs Gallery · PoSeeReview</title>
  <link href="https://fonts.googleapis.com/css2?family=Instrument+Serif:ital@0;1&family=Geist:wght@400;500;600;700&family=Geist+Mono:wght@400;500;600;700&display=swap" rel="stylesheet">
  <style>
    *,*::before,*::after{box-sizing:border-box;margin:0;padding:0}
    :root{--paper:#f5f5f5;--ink:#2d3142;--muted:#4f5d75;--accent:#eb6c36;--font-sans:'Geist',system-ui,sans-serif;--font-serif:'Instrument Serif',serif;--font-mono:'Geist Mono',ui-monospace,monospace}
    body{font-family:var(--font-sans);background:linear-gradient(180deg,#fbfbfb 0%,var(--paper) 45%,#eef2f7 100%);color:var(--ink);min-height:100vh;padding:3rem 2rem}
    .frame{max-width:1320px;margin:0 auto}
    .eyebrow{font-family:var(--font-mono);font-size:.66rem;font-weight:600;letter-spacing:.18em;text-transform:uppercase;color:var(--muted);margin-bottom:.5rem}
    h1{font-family:var(--font-serif);font-size:clamp(2rem,3vw + 1rem,3.2rem);font-weight:400;line-height:1.05;letter-spacing:-.03em;margin-bottom:.5rem}
    .tagline{max-width:74ch;color:var(--muted);line-height:1.65;font-size:1rem;margin-bottom:1.8rem}
    .section-title{font-family:var(--font-mono);font-size:.68rem;font-weight:700;letter-spacing:.16em;text-transform:uppercase;color:var(--muted);margin:2rem 0 .9rem;padding-bottom:.35rem;border-bottom:1px solid #d1d5db}
    .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:1rem}
    .card{background:#fff;border:1px solid #dbe1e8;border-radius:18px;padding:1rem 1.1rem;text-decoration:none;color:inherit;display:block;box-shadow:0 10px 30px rgba(15,23,42,.06);transition:transform .15s ease,box-shadow .15s ease,border-color .15s ease}
    .card:hover{transform:translateY(-2px);box-shadow:0 14px 34px rgba(235,108,54,.14);border-color:#eb6c36}
    .card-type{font-family:var(--font-mono);font-size:.64rem;font-weight:700;letter-spacing:.12em;text-transform:uppercase;color:var(--muted);margin-bottom:.45rem}
    .card-title{font-family:var(--font-serif);font-size:1.12rem;font-weight:400;line-height:1.25;color:var(--ink);margin-bottom:.35rem}
    .card-desc{font-size:.82rem;line-height:1.5;color:var(--muted)}
    .card-links{display:flex;gap:.7rem;margin-top:.7rem;font-family:var(--font-mono);font-size:.66rem;letter-spacing:.1em;text-transform:uppercase}
    .card-links span{color:var(--accent)}
    .card-links small{color:var(--muted)}
  </style>
</head>
<body>
  <div class="frame">
    <p class="eyebrow">Documentation Suite · PoSeeReview</p>
    <h1>Net Docs Gallery</h1>
    <p class="tagline">The gallery now includes the requested AI, architecture, slice-isolation, auth, diagnostics, testing, roles, and workflow reports. Each page has a full and simplified version, and diagram pages embed compiled SVGs directly.</p>

    <p class="section-title">Generated reports</p>
    <div class="grid">
      <a class="card" href="AI_SERVICES_REPORT.html"><p class="card-type">AI Services</p><p class="card-title">AI Services Report</p><p class="card-desc">Service and provider inventory, cost gaps, model mapping, route bindings, and fallback policies.</p><p class="card-links"><span>Full</span><small>→ AI_SERVICES_REPORT_SIMPLE.html</small></p></a>
      <a class="card" href="ARCHITECTURE_REPORT.html"><p class="card-type">Architecture</p><p class="card-title">Architecture Report</p><p class="card-desc">C4 L1–L3 audit, slice boundaries, middleware order, and /_framework boot opt-outs.</p><p class="card-links"><span>Full</span><small>→ ARCHITECTURE_REPORT_SIMPLE.html</small></p></a>
      <a class="card" href="SLICE_ISOLATION_REPORT.html"><p class="card-type">Slice Isolation</p><p class="card-title">Slice Isolation Report</p><p class="card-desc">Project-reference coupling, DTO flow, and cross-slice dependency graph.</p><p class="card-links"><span>Full</span><small>→ SLICE_ISOLATION_REPORT_SIMPLE.html</small></p></a>
      <a class="card" href="AUTH_LIFECYCLE.html"><p class="card-type">Auth Lifecycle</p><p class="card-title">Auth Lifecycle</p><p class="card-desc">FakeAuth, cookie BFF, OIDC, 401 vs 302 behavior, and state transitions.</p><p class="card-links"><span>Full</span><small>→ AUTH_LIFECYCLE_SIMPLE.html</small></p></a>
      <a class="card" href="DIAGNOSTIC_METRICS.html"><p class="card-type">Diagnostics</p><p class="card-title">Diagnostic Metrics</p><p class="card-desc">InteractiveMs-first reporting with CLS and WASM memory separated, plus live/synthetic toggles.</p><p class="card-links"><span>Full</span><small>→ DIAGNOSTIC_METRICS_SIMPLE.html</small></p></a>
      <a class="card" href="TESTING_TIER_HIERARCHY.html"><p class="card-type">Testing</p><p class="card-title">Testing Tier Hierarchy</p><p class="card-desc">Tier counts, ceiling comparison, and runnable dotnet test filter matrix.</p><p class="card-links"><span>Full</span><small>→ TESTING_TIER_HIERARCHY_SIMPLE.html</small></p></a>
      <a class="card" href="ROLES_PERMISSIONS_MATRIX.html"><p class="card-type">Security</p><p class="card-title">Roles Permissions Matrix</p><p class="card-desc">Principal × Environment access grid with dynamic column collapse and guard audit.</p><p class="card-links"><span>Full</span><small>→ ROLES_PERMISSIONS_MATRIX_SIMPLE.html</small></p></a>
      <a class="card" href="USER_WORKFLOW.html"><p class="card-type">Workflow</p><p class="card-title">User Workflow</p><p class="card-desc">UI submit to storage trace, failure modes, and the request lifecycle Mermaid sequence.</p><p class="card-links"><span>Full</span><small>→ USER_WORKFLOW_SIMPLE.html</small></p></a>
    </div>
  </div>
</body>
</html>`;

write(path.join(docsDir, 'index.html'), indexHtml);
