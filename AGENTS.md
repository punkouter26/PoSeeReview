# AGENTS.md

Working rules (NET_AGENTS). These govern how any coding agent operates in this repo, not how the
code is written — for architecture and conventions see [CLAUDE.md](CLAUDE.md) and
[README.md](README.md).

- **`master` only.** Do all work on `master`. Use another branch only when explicitly asked to.
- **Restart and verify after every code change.** Stop the app, start it again
  (`dotnet run --project src/PoSeeReview.Api --launch-profile https`, or the
  `start-api-clean` VS Code task), and confirm it actually came up before reporting done.
  Config/appsettings/Key Vault changes need a full restart — `dotnet watch` will not pick them up.
- **Look for a root `docs/` folder first, and fall back when it is not there.** If one exists, read
  it for the overall project summary before exploring the code. It does not exist right now — the
  generated reports were cleared out and are due to be rebuilt — so [README.md](README.md) (the PRD)
  and [CLAUDE.md](CLAUDE.md) are the authoritative overview, and `docs/index.html` is not worth
  hunting for.
- **No `dotnet user-secrets`.** Non-secret config goes in `appsettings*.json`; real secrets go in
  Key Vault `kv-poshared` under the `PoSeeReview--` prefix. The one existing exception is
  `Takedowns:ApiKey` for local dev — it is a live credential, so it must never land in an
  appsettings file that is committed.
- **Never push to remote unless asked.** Committing locally is fine; `git push` is not, until the
  user says so — or until they type "git sync".
- **On "git sync": stage everything, commit, push.** Commit *all* outstanding changes first — a
  sync leaves nothing dirty behind. Short American-slang message that reads like a human wrote it
  ("fixed the busted nav", "cleaned up that css mess"), then push.
- **Only run the tests that cover the change.** Pick the project and `--filter` that exercise what
  was touched; for a change with no test surface — a copy tweak, a CSS value — run none at all.
  Never reach for the full suite after a code change.
- **Run the commands yourself.** Don't hand the user a command to paste or a web portal to click
  through when the agent can do it; only ask when it genuinely needs their machine, credentials,
  or a decision.
- **Warnings are errors.** `TreatWarningsAsErrors` is on globally via `Directory.Build.props`; fix
  every warning at its cause. Never suppress one (`#pragma`, `NoWarn`) to get a build green.
- **Call out big deletions.** If a prompt's changes remove more than 100 lines of code overall
  (net), say so in the reply.
- **Screenshot UI changes, before and after.** When a change touches the UI, capture the old and
  new UI and annotate what changed, and show it in the reply.
- **TL;DR any answer over 100 words** with a ~20-word summary at the end.
