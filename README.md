# 🐗 HogsTribeBot

A Discord bot for managing tribe operations in **Viking Rise**, built for the **HOGS** community. Handles member registration, bank donations, fines, delivery events, farms and farm tribes, KvK scheduling, raid signups, reign queues, title rotations, and more — all backed by Google Sheets as the database.

---

## 🏗️ Architecture

```
HogsTribeBot/
├── TribeBot.Bot/          # Discord.Net bot — handlers, modals, UI, hosted workers
│   ├── Handlers/          # Text (!) and slash (/) command handlers
│   ├── Modals/            # Discord modal definitions
│   ├── Hosting/           # Background workers (KvK announcer)
│   └── UI/                # Embed helpers, help pages, raid components
├── TribeBot.Core/         # Entities, DTOs, enums, flows, interfaces
├── TribeBot.Services/     # Business logic layer (incl. scheduler + OCR client)
├── TribeBot.Data/         # Google Sheets data store
├── TribeBot.Common/       # Shared helpers (placeholder)
├── ocr-service/           # Python OCR microservice (Flask + RapidOCR)
└── Dockerfile             # Bot container (.NET 9)
```

**Two services deployed on Railway:**
- `hogstribebot` — the main .NET 9 bot (root `Dockerfile`)
- `ocr-service` — Python OCR microservice (`ocr-service/Dockerfile`), reachable internally at `ocr-service.railway.internal:23333`

---

## 🛠️ Tech Stack

| Layer | Technology |
|---|---|
| Bot framework | [Discord.Net](https://github.com/discord-net/Discord.Net) 3.18.0 (interaction framework + text commands) |
| Runtime | .NET 9 |
| Database | Google Sheets API (service account) |
| OCR | RapidOCR (`rapidocr-onnxruntime`) behind a Flask HTTP API |
| Hosting | [Railway](https://railway.app) (Docker) |

---

## ✨ Features

> Commands marked *(officer)* are restricted to officers (and farm managers where noted). Use `!help` in Discord for the in-bot help pages.

### 👤 Member Management
- `!register` — DM-based member registration flow
- `/updateprofile` — Update your profile via modal form
- `!myinfo` / `!viewinfo @user` — View a member profile
- `!listmembers` — List all registered members
- `!listnonregistered` / `!registerreminder` — Find and remind unregistered members *(officer)*
- `!removemember @user` — Remove a member and all associated data *(officer)*
- Leave notifications posted to the officer log when a user leaves the server

### 💰 Bank / Donations
- `!checkbank` — Check your weekly donation status
- `!bankunpaid` — List unpaid members *(officer)*
- `!payfor` — Pay donations on behalf of a member *(officer)*
- `!bankreminder` — Send donation reminders manually *(officer)*
- **Automatic bank reminders** — Mon / Wed / Fri at 18:00 UTC
- **Automatic weekly audit** — Sunday 18:00 UTC; unpaid, non-exempt members are fined and DM'd, results logged to the officer channel

### 📦 Delivery Events
- `!gold` / `!bracelet` — Submit contribution screenshots (OCR-validated)
- `!donatefor` — Submit on behalf of someone else
- `!checkdelivery` — Check your completion status
- `!deliverystart` / `!deliveryend` — Start an event / end it and issue fines *(officer)*
- `!deliverystatus` / `!deliveryreminder` — Show and remind missing players *(officer)*

### 💀 Fines
- `!myfines` — View your outstanding fines
- `!fineuser` / `!finereign` — Issue fines *(officer)*
- `!finelist` / `!unpaidfines` — View all / unpaid fines *(officer)*
- `!removefine` — Remove a fine by ID *(officer)*
- `!finereminder` — Remind members of unpaid fines *(officer)*
- `!verifiedpayment` — Mark fines as paid *(officer)*

### 👑 Viking Reign
*(Currently hidden from `!help`, but the commands are still active.)*
- `!applyreign` / `!leavereign` — Join or leave the reign queue
- `!listreign` — View applicants sorted by reign points
- `!lockreign` / `!unlockreign` / `!clearreign` — Queue control *(officer)*
- `!setreignpoints` / `!setreignplayer` / `!removereign` — Manage entries *(officer)*
- `!exempt` / `!unexempt` — Toggle a member's weekly-donation exemption *(officer)*

### 🌾 Farms
- `/farm add` / `/farm bulk` — Register one or many farms
- `/farm list` — Receive your farms via DM
- `/farm edit` / `/farm remove` — Manage your farms
- `/farm status` — View your farm and farm tribe status
- `/farm track` — Look up a farm by ID and see who owns it
- `/farm addfor` / `/farm bulkfor` / `/farm listfor` — Manage farms for another player *(officer / farm manager)*
- `/farm inactive` / `/farm inactivebulk` — Notify an owner that one or more farms look offline
- **GRV farm cap alert** — officers are alerted when a GRV member reaches 10 farms

### 🏘️ Farm Tribes
- `/farmtribe list` — List all farm tribes
- `/farmtribe check` — Show farm counts for players in a specific tribe
- `/farmtribe overview` — Full player, farm count, and tribe assignment overview
- `/farmtribe research` / `/farmtribe goldmine` — Notify officers that research finished / the gold mine expired
- `/farmtribe register` / `/farmtribe edit` / `/farmtribe delete` — Manage tribes *(officer)*
- `/farmtribe assign` / `/farmtribe unassign` — Manage assignments, logged to the officer channel *(officer)*
- Tribe capacity is enforced automatically on registration

### ⚔️ KvK
- `/kvk status` — Show the active KvK and its scheduled events
- `/kvk list` — List all KvKs, active and past
- `/kvk create` / `/kvk end` — Start or end a KvK *(officer)*
- `/kvkevent add-event` — Schedule a KvK event via modal (free-text type + description) *(officer)*
- **KvK announcer** — background worker checks every 5 minutes and posts upcoming events as **plain text** (not embeds), so role/`@everyone` mentions in the description actually ping

### 🛡️ Raid Signups
- `/raid create` — Create a raid post via modal (free-text type + description)
- Yes / Maybe / No signup buttons, plus a **Show roster** button

### 🎩 Title System
- `/applytitle` / `/withdrawtitle` — Join or leave the Tycoon / Priest queue
- `/titlequeue` — View the current queues
- `/currenttitles` — Current holders, queue, and rotation countdown
- `/titlegrant` — Confirm a grant and advance the rotation *(officer)*
- Automatic pre-announcement and overdue alerts for the rotation (**Tycoon rotation is currently paused**)

### 📅 Events & Notifications
- `/hevent` — Schedule a tribe event with a reminder
- `/helist` / `/heedit` / `/hedelete` — Manage scheduled events
- `/eventnotifications` — Post the opt-in button for DM event notifications
- `/hesworn` / `/heswornfinal` — Announce the next / final Sworn Vengeance level

### 📉 Might Cut
- `/mightcut` — Send a private might-cut DM to a player via modal *(officer)*

### 📊 Polls
- `!pollshow` / `!polllist` — Show polls
- `!vote` — Vote via DM
- `!pollcreate` / `!pollremove` / `!pollofficer` — Manage polls *(officer)*

### 🎥 Content Creator
- `!promote <YouTube link>` — Post your video to the promotion channel (requires the **Content Creator** role)

---

## ⚙️ Environment Variables

### Bot (`hogstribebot`)

| Variable | Description | Fallback if unset |
|---|---|---|
| `DISCORD_TOKEN` | Discord bot token | **Required** — bot exits without it |
| `GOOGLE_CREDENTIALS_JSON` | Google service account credentials (full JSON string) | Reads a local `credentials.json` (path in `Program.cs`) |
| `SHEETS_SPREADSHEET_ID` | Target Google Sheets spreadsheet ID | The HOGS production spreadsheet ID |
| `OCR_HOST` | OCR service host — set to `ocr-service.railway.internal` on Railway | `127.0.0.1` |
| `OCR_PORT` | OCR service port | `23333` |

### OCR service (`ocr-service`)

| Variable | Description | Fallback if unset |
|---|---|---|
| `PORT` | Port Flask listens on | `23333` |

---

## 🐍 OCR Microservice

Located in `ocr-service/`. A small **Flask** app wrapping **RapidOCR** (chosen over PaddleOCR because PaddleOCR's oneDNN/libGL dependencies aren't viable on Linux/Railway).

| Endpoint | Description |
|---|---|
| `GET /health` | Health check |
| `POST /ocr` | Body: `{ "image_url": "<discord attachment url>" }` → returns `{ "data": [ { "text", "confidence", "box" } ] }` |

The service only returns raw text blocks. Donation amounts and dates are extracted on the C# side (`PaddleOcrServerService` in `TribeBot.Services`, name kept from the PaddleOCR era).

---

## 🚀 Local Development

1. Clone the repo
2. Create a Google service account, download the credentials JSON, and share the spreadsheet with it
3. Set the environment variables above (at minimum `DISCORD_TOKEN`, plus `GOOGLE_CREDENTIALS_JSON` unless using the local file fallback)
4. Run the OCR microservice:
   ```bash
   cd ocr-service
   pip install -r requirements.txt
   python server.py
   ```
5. Run the bot:
   ```bash
   dotnet run --project TribeBot.Bot
   ```

---

## 📝 Notes

- Google Sheets rows are **1-indexed** with row 1 as the header — all data starts at row 2
- Row deletions must always be done in **descending order** and batched into a single `BatchUpdate` call to avoid index shifting
- RapidOCR returns **float** bounding box coordinates — use `GetDouble()` when parsing its JSON in C#
- OCR date output may merge date and time without a separator; regex parsing handles formats like `05/2613:21:58`
- Announcements that need to ping roles must be sent as plain message content — Discord doesn't fire mention notifications from embeds
- Bank reminder / audit "already ran" guards are in-memory, so a restart within the trigger window can re-run them

---

## 📄 License

Private project — for internal HOGS tribe use only.
