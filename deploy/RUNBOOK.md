# Workit — deploy & release runbook

## The three repos

| Repo | What it is | How it ships |
|---|---|---|
| [workit](https://github.com/SkuliSkula/workit) | API + owner console | **Automatic** on merge to `main` |
| [WorkitEmployee](https://github.com/SkuliSkula/WorkitEmployee) | iOS app | By hand, via Xcode |
| [workit-site](https://github.com/SkuliSkula/workit-site) | workit.is + help.workit.is | By hand, via `wrangler` |

---

## Backend — normal workflow

Work on `dev`. `main` is protected and will refuse a direct push.

```bash
git checkout dev
git pull
# ...changes...
git commit -am "what changed"
git push
```

CI (build + full test suite) runs on every push to `dev`.

Ship to production:

```bash
gh pr create --base main --head dev
# wait for the "build-and-test" check to pass
gh pr merge <pr-number> --merge
```

Merging to `main` automatically:

1. Re-runs the tests — they gate everything below.
2. Builds `linux/arm64` images.
3. Pushes them to GHCR tagged with the commit SHA.
4. SSHes to Hetzner, pulls, restarts `workit-api` and `workit-owner`.

Roughly three minutes. Nothing deploys if the tests fail.

**Documentation-only merges do not deploy.** A merge whose every changed file
is markdown skips the deploy workflow entirely — no rebuild, no restart. A
commit touching both markdown and code still deploys as normal. If a deploy is
skipped and you wanted one, run *Deploy to production* by hand from the Actions
tab.

```bash
gh run list --repo SkuliSkula/workit
gh run watch <run-id> --repo SkuliSkula/workit
```

### Why the server never builds

The box is `aarch64` with 2 vCPU and 3.7 GB RAM, shared with Postgres and the
Blazor app — not somewhere you want a .NET build competing for memory.

It doesn't need to. Both Dockerfiles only `COPY` a framework-dependent publish,
which is portable IL, so an x86 GitHub runner produces output that runs
unchanged on ARM, and buildx assembles the arm64 image without ever executing
ARM code. No QEMU, no cross-compilation, no cost.

---

## Backend — rollback

Every build is tagged with its commit SHA and kept in GHCR, so you can return to
*any* past commit, not just the previous one.

```bash
# what is deployed right now
ssh workit-hetzner "grep IMAGE_TAG /opt/workit/deploy/.env"
```

```bash
ssh workit-hetzner
cd /opt/workit/deploy
sed -i 's/^IMAGE_TAG=.*/IMAGE_TAG=<full-40-char-sha>/' .env
docker compose pull workit-api workit-owner
docker compose up -d workit-api workit-owner
```

`IMAGE_TAG` is pinned in `.env` on every deploy, so a later manual
`docker compose up -d` reuses that exact build instead of drifting to `:latest`.

---

## Backend — emergency manual deploy

Only if GitHub is unavailable. From the repo root:

```bash
dotnet publish Workit.Api/Workit.Api.csproj -c Release -o Workit.Api/publish
dotnet publish Workit.OwnerApp/Workit.OwnerApp.csproj -c Release -o Workit.OwnerApp/publish
rsync -az --delete Workit.Api/publish/ workit-hetzner:/opt/workit/Workit.Api/publish/
rsync -az --delete Workit.OwnerApp/publish/ workit-hetzner:/opt/workit/Workit.OwnerApp/publish/
ssh workit-hetzner "cd /opt/workit/deploy && \
  docker compose -f docker-compose.yml -f docker-compose.build.yml build && \
  docker compose -f docker-compose.yml -f docker-compose.build.yml up -d"
```

---

## Marketing site (workit.is)

Not automated. From the `workit-site` folder:

```bash
npx wrangler deploy
git commit -am "..." && git push
```

Pages: `/` (Icelandic), `/en`, `/privacy`, `/support`, `/reset-password`.
`www.workit.is` 301-redirects to the apex.

`/reset-password` is where both password resets and new-account invites land, so
it is on the critical path for onboarding — deploy it before shipping an API
change that alters those links.

---

## iOS app

`project.yml` is the source of truth. After editing it, run `xcodegen generate`.

Release: **Xcode → Product → Archive → Distribute App → App Store Connect**

- Bundle id: `com.workit.employee`
- Team: `KUDHSU4SGU`

Debug builds point at `localhost:5200`; Release builds at `api.workit.is`. To
test against production, build Release.

---

## Onboarding a new customer

Selling a seat is one form. Everything after it is self-service.

1. **You create the owner login.** Sign in to <https://admin.workit.is> as the
   admin account, go to **Admin → + Create Owner**, and enter their name and
   email. No password is set: the account is created with a random one nobody
   holds, and they are emailed a one-time link to choose their own. The link
   lasts 7 days; **Resend invite** on their row issues a fresh one.
2. **They set up their own company.** Their first sign-in lands on `/onboarding`
   because the account has no company yet. They either fill in the details by
   hand (name, kennitala, email, phone, address, contact) or paste their Payday
   client ID/secret and have it imported. This creates the company, links them to
   it, and gives them an employee record of their own.
3. **They add their staff.** **Employees → New Employee** — again no password;
   each is emailed a setup link and pointers to the phone apps. (Employees are
   rejected by the owner web app on purpose.) **Send setup link** on a row
   re-sends it. For someone who cannot use email, **Set password manually**
   generates one and shows it on screen instead of mailing it.

Admins can rename an owner or change their login address from the same Admin
page (**Edit**), and remove a login entirely (**Delete**). Deleting removes only
the sign-in: the company and all its jobs, time entries, invoices and employees
survive, and the company stays reachable from the **All Companies** list. An
owner's own employee record follows their email when it is changed, so they keep
their timesheet.

Passwords are never emailed. Anyone signed in can change their own under
**My Account**, which revokes every other session. Forgotten passwords are
self-service from the **Forgot password?** link on the web login and in both
phone apps. Both the reset and the invite link land on `App:Url` — the marketing
site, which hosts `/reset-password` — **not** the owner app. An invite link adds
`&new=1`, which only changes the wording on that page; an older deploy of the
site that ignores the parameter still redeems the token correctly.

---

## Demo accounts

Filed with Apple App Review:

```
demo@workit.is            Owner
demo.employee@workit.is   Employee
Password: WorkitDemo2026!
```

**These passwords cannot be changed.** The API refuses resets on them by design
— see `DemoDataSeeder.IsProtectedAccount`. Apple keeps the password on file, and
a reset locks the review team out with no warning. This has happened once
already.

Re-seed the demo data. Wipes and rebuilds **only** the demo company; nothing
outside it is touched:

```bash
ssh workit-hetzner
cd /opt/workit/deploy
docker compose run --rm --no-deps -T --entrypoint dotnet \
  workit-api Workit.Api.dll --seed-demo
```

---

## Server

```bash
ssh workit-hetzner          # 37.27.219.127, root
cd /opt/workit/deploy       # compose lives here
```

Services: `workit-api`, `workit-owner`, `workit-db`, `cloudflared`.

```bash
docker compose ps
docker compose logs workit-api --since 15m
docker compose logs workit-api -f
```

> **Never run `docker compose down -v`.** It destroys the database volume and the
> `workit-api-keys` DataProtection volume. Losing the latter breaks every
> encrypted credential stored in the database.

### How the deploy reaches the server

The GitHub Actions deploy key is pinned to `deploy/deploy.sh` with a forced
command in `authorized_keys`, so a leaked CI secret cannot open a shell — the
only thing that key can do is deploy. The registry token travels on stdin, never
argv, so it never appears in `ps`, and is discarded on exit. No long-lived
registry credential is stored on the server.

`deploy.sh` pulls before touching anything, so a failed pull leaves the running
containers alone, and it only ever restarts the two app services.

---

## Gotchas

- `main` is protected. Direct pushes are refused, including for admins. Use a PR.
- The server never builds. It only pulls images from GHCR.
- Migrations run automatically at API startup, so deploying is just restarting.
- `local-packages/*.nupkg` is a patched PdfPig build committed on purpose,
  despite the `*.nupkg` gitignore rule. It is not on nuget.org — delete it and
  nothing restores.
- Running the API locally in Development sends **real** emails through Resend.
  Never test forgot-password against someone's real address.
- Two deploys never run concurrently; the second queues behind the first.
- The API is not published on a host port — it is reachable only through the
  Cloudflare tunnel — so you cannot `curl` it directly on the server.
