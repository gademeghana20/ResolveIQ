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
3. Check which .NET version the host runs. ResolveIQ needs **.NET 10**.
   - If the host offers .NET 10, build the normal way (step 4).
   - If it does not, build **self-contained** instead (the app carries its own runtime): add
     `--self-contained -r win-x64` to the command in step 4.
4. On your computer, from the project folder, build the API:
   ```
   dotnet publish backend/ResolveIQ.API -c Release -o publish
   ```
5. Upload everything inside the new `publish` folder to the website, using the upload method the panel offers
   (FTP, or its file manager). Replace what is there.
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
   - `Cors` / `App`: which website is allowed to talk to the API. Use the Vercel address from Part 2.
     You can come back and fix it after you know the address.
7. Start (or restart) the website from the panel, then open `https://yourname.runasp.net/health`.
   You should see `{"status":"Healthy"}`.
   - If you see an error page, open the host's log viewer. The usual causes are a wrong connection string
     or a missing `Jwt` key.

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

## What this setup does NOT do

- **No real email.** "Forgot password" is switched off for the demo accounts, and the app only writes emails to its log.
- **Free hosting is slow sometimes.** Expect a delay on the first visit after quiet periods, and small size limits.
- **Anyone can use the Admin demo.** That is why the demo accounts are protected and the data is made up.
  Never put real data in the public demo.
- **The demo password is public.** Do not reuse it anywhere else.

## Running it on your own computer (no internet needed)

Leave `apiBaseUrl` and `demoPassword` empty in `config.js` and follow the README.
