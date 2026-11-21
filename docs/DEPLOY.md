# Putting ResolveIQ on the internet (free)

This guide puts the **website on Vercel** and the **API and database on MonsterASP.NET's free plan**.
You do each step yourself in your own accounts. Nothing here needs a password or token to be shared with anyone.

> The free hosts change their screens and limits from time to time. The *ideas* below stay the same; if a button is
> called something slightly different, follow the host's own wording.

## How the pieces fit

```
 Visitor's browser
       │
       ├── opens the website ───────────►  Vercel          (the Angular app: just files)
       │
       └── the website calls the API ───►  MonsterASP.NET  (the .NET API) ──► SQL Server database (same host)
```

## Before you start

- Use your **personal** email for every account (not a work one).
- You need: a **GitHub** account (you have it), a **Vercel** account (you have it), a **MonsterASP.NET** account (new).
- Pick the password for the demo accounts now. **It will be public**: everyone who opens the demo will use it.
  Choose something you use nowhere else, at least 8 characters with an upper case letter, a lower case letter and a number,
  for example `Demo#Pass-2026`.
- Make a long random text for the API's signing key (at least 32 characters). It must stay secret.
  In PowerShell: `[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))`

## Part 1: the API and database on MonsterASP.NET

1. Create a free account at <https://www.monsterasp.net/> with your personal email.
2. In its control panel, create a **website** (the free plan has one) and a **SQL Server database** (the free plan has one).
   Write down the database's **connection string** (the panel shows it).
   Also write down the website's address, for example `https://yourname.runasp.net`.
3. Check which .NET version the host runs. ResolveIQ needs **.NET 10** (MonsterASP.NET's free plan offers it).
   If a host does not, build **self-contained** instead (the app carries its own runtime): add `--self-contained -r win-x64`.
4. On your computer, from the project folder, build the API the **portable** way (no `-r` option):
   ```
   dotnet publish backend/ResolveIQ.API -c Release -o publish/api
   ```
   Then delete the non-Windows folders inside `publish/api/runtimes` (keep the ones starting with `win`) to make it smaller,
   and zip the *contents* of `publish/api`.
   > We first tried a build made for one specific processor type (`-r win-x64`). On the host it failed to start with
   > "HTTP Error 500.30", with no useful message. The portable build above started straight away.
5. In the host's file manager, open `wwwroot`, upload the zip, and click **Unzip** on its row (answer yes to overwriting).
6. Create a file called `appsettings.Production.json` **on the server only** (never in the project, never in GitHub;
   the project already ignores that name). Put this in it, filling in the capital-letter parts:

   ```json
   {
     "ConnectionStrings": { "DefaultConnection": "PASTE THE CONNECTION STRING FROM THE PANEL" },
     "Jwt": { "Key": "PASTE YOUR LONG RANDOM TEXT" },

     "Seed": { "DemoUsers": true, "DemoPassword": "THE DEMO PASSWORD YOU CHOSE", "DemoData": true },
     "Demo": { "ProtectAccounts": true },
     "Registration": { "Enabled": false },

     "Hosting": { "TrustProxyHeaders": true },
     "Database": { "MigrateOnStartup": true },

     "Cors": { "AllowedOrigins": [ "https://YOUR-SITE.vercel.app" ] },
     "App": { "FrontendUrl": "https://YOUR-SITE.vercel.app" }
   }
   ```
   (If the panel has a place for "environment variables" or "application settings", you can use that instead;
   write nested names with two underscores, like `Seed__DemoPassword`.)

   What each part does:
   - `Seed:DemoUsers` / `DemoData`: creates the three demo accounts and 14 made-up incidents, only into an empty database.
   - `Demo:ProtectAccounts`: nobody can lock the demo accounts or change their password.
   - `Registration:Enabled: false`: strangers cannot create accounts. Visitors use the demo buttons.
   - `Hosting:TrustProxyHeaders`: makes the sign-in limit work per visitor instead of for everyone at once.
   - `Database:MigrateOnStartup`: the app builds its own tables the first time it starts.
   - `Cors` / `App`: which website is allowed to talk to the API. Use the Vercel address from Part 2 (exactly, with `https://` and no slash at the end).
     You can come back and fix it after you know the address.
7. **Turn on HTTPS.** In the panel open the website, then **HTTPS**, then **Enable HTTPS** (the free Let's Encrypt one).
   A new certificate can take **a few minutes** to start working; until then the address says "connection closed".
   (The Vercel site talks to the API over `https`, so the API needs it.)
8. Open `https://yourname.runasp.net/health`. You should see `{"status":"Healthy"}`. The first visit can be slow,
   because the app builds its tables and adds the demo data.
   - **"HTTP Error 500.30":** the app crashed while starting. Create a folder called `logs` inside `wwwroot` and set
     `stdoutLogEnabled="true"` in `web.config`, visit the address, and read the file that appears in `logs`.
     The usual causes are a wrong connection string, a missing `Jwt` key, or a wrong processor type (see step 4).
     Turn the log off again afterwards.
   - Uploading a new `web.config` makes the host restart the app, which is also how new settings take effect.

## Part 2: the website on Vercel

1. Open `frontend/public/config.js` in the project and fill in the two lines:
   ```js
   window.resolveiqConfig = {
     apiBaseUrl: 'https://yourname.runasp.net',   // your API address, with no /api at the end
     demoPassword: 'THE DEMO PASSWORD YOU CHOSE', // shows the Employee / Manager / Admin demo buttons
   };
   ```
   This file is public by design, so it only ever holds the API address and the **public** demo password.
2. Save, commit and push to GitHub.
3. On <https://vercel.com>: **Add New → Project**, pick the `ResolveIQ` repository.
   Set **Root Directory** to `frontend`. Vercel reads `frontend/vercel.json` for the rest. Click **Deploy**.
4. Vercel gives you an address like `https://resolveiq-xxxx.vercel.app`.
   Put it in the server's `Cors:AllowedOrigins` and `App:FrontendUrl` (Part 1, step 6), restart the API, and you are live.

## Check it works

1. Open your Vercel address. The sign-in page shows **"Just looking? Try the demo as: Employee / Manager / Admin"**.
2. Click **Manager**. You should see the incident list with 14 incidents.
3. Open the **Admin** account, then **Admin → Audit log**.
4. Try to change the demo account's password: it says it is a shared demo account (this is on purpose).

## Starting over (resetting the demo)

If visitors fill the demo with junk, empty the database in the host's panel (or delete and re-create it) and restart
the website. It builds the tables again and re-adds the demo accounts and incidents.

## Updating the live site later

When the app has new features:

1. **API:** build it the same portable way (Part 1, step 4), upload the zip to `wwwroot`, **Unzip** over the old files, and
   upload your `web.config` once more (that is what makes the host restart the app). Do **not** include or overwrite
   `appsettings.Production.json`: it holds your private settings and is not in the zip.
2. **Database:** nothing to do. Because `Database:MigrateOnStartup` is on, the app adds any new tables by itself the first
   time it starts (for example the `Attachments` table). The existing data stays.
3. **Website:** push to GitHub. Vercel rebuilds it by itself.

## File attachments on the live site

Attaching files is **switched off by default on a public demo** (it follows `Demo:ProtectAccounts`), so strangers cannot put
files on your site. Visitors can still see the feature in the README screenshots, and you can try it by running the project
on your own computer. To switch it on anyway, add this to the server's `appsettings.Production.json`:

```json
"Attachments": { "Enabled": true }
```

Files are stored inside the database, and the free database is only 1 GB, so keep the limits small if you do.

## What this setup does NOT do

- **No real email.** "Forgot password" is switched off for the demo accounts, and the app only writes emails to its log.
- **Free hosting is slow sometimes.** Expect a delay on the first visit after quiet periods, and small size limits.
- **Anyone can use the Admin demo.** That is why the demo accounts are protected and the data is made up.
  Never put real data in the public demo.
- **The demo password is public.** Do not reuse it anywhere else.

## Running it on your own computer (no internet needed)

Leave `apiBaseUrl` and `demoPassword` empty in `config.js` and follow the README.
