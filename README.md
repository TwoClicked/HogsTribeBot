# 🐗 HogsTribeBot

A Discord bot for managing guild/clan operations in **Rise of Kingdoms**, built for the **HOGS** community. Handles member registration, farm management, donation tracking, fines, delivery events, raid signups, reign queues, title systems, and more — all backed by Google Sheets as the database.

---

## 🏗️ Architecture

```
HogsTribeBot/
├── TribeBot.Bot/          # Discord.Net bot — commands, handlers, UI
├── TribeBot.Core/         # Entities, enums, interfaces
├── TribeBot.Services/     # Business logic layer
├── TribeBot.Data/         # Google Sheets data store
└── ocr-service/           # Python OCR microservice (RapidOCR)
```

**Two services deployed on Railway:**
- `hogstribebot` — the main .NET 9 bot
- `ocr-service` — Python microservice for screenshot OCR, accessible internally at `ocr-service.railway.internal:23333`

---

## 🛠️ Tech Stack

| Layer | Technology |
|---|---|
| Bot framework | [Discord.Net](https://github.com/discord-net/Discord.Net) |
| Runtime | .NET 9 |
| Database | Google Sheets API (service account) |
| OCR | RapidOCR (Python microservice) |
| Hosting | [Railway](https://railway.app) |

---

## ✨ Features

### 👤 Member Management
- `!register` — DM-based member registration flow
- `!myinfo` / `!viewinfo @user` — View member profile
- `!listmembers` / `!listnonregistered` — Member roster management
- `!removemember @user` — Remove a member and all associated data
- `/updateprofile` — Update profile via modal form

### 💰 Bank / Donations
- `!checkbank` — Check your weekly donation status
- `!bankunpaid` — List unpaid members (officer)
- `!payfor` — Pay donations on behalf of a member (officer)
- `!bankreminder` — Send donation reminders (officer)
- Automated weekly audit with fines for missed donations

### 📦 Delivery Events
- `!deliverystart` / `!deliveryend` — Start and end delivery events (officer)
- `!gold` / `!bracelet` — Submit contribution screenshots
- `!checkdelivery` — Check your completion status
- `!deliverystatus` — Show missing players (officer)
- OCR-validated screenshot submissions

### 💀 Fines
- `!myfines` — View your outstanding fines
- `!fineuser` / `!finereign` — Issue fines (officer)
- `!finelist` / `!unpaidfines` — View and manage fines (officer)
- `!verifiedpayment` — Mark fines as paid (officer)

### ⚔️ Reign
- `!applyreign` / `!leavereign` — Join or leave the Viking Reign queue
- `!listreign` — View applicants sorted by reign points
- Lock/unlock, exemptions, and point management (officer)

### 🌾 Farms & Farm Tribes
- `/farm add` / `/farm bulk` — Register farms
- `/farm list` / `/farm edit` / `/farm remove` — Manage your farms
- `/farmtribe register` — Create a farm tribe (officer)
- `/farmtribe assign` / `/farmtribe unassign` — Manage tribe assignments (officer)
- `/farmtribe overview` — Full player and farm count overview

### 🎩 Title System
- `/applytitle` / `/withdrawtitle` — Apply for Tycoon or Priest titles
- `/titlequeue` — View current title queues
- `/titlegrant` — Grant title and advance rotation (officer)

### ⚔️ Raid Signups
- `/raid create` — Create a raid post with Yes / No / Maybe buttons
- `/raid list` / `/raid delete` — Manage active raids (officer)

### 📊 Polls
- `!pollcreate` / `!pollremove` — Create and remove polls (officer)
- `!pollshow` — Display a poll
- `!vote` — Vote via DM

### 📅 Events
- `/hevent` — Schedule a tribe event
- `/helist` / `/heedit` / `/hedelete` — Manage scheduled events

### 🎥 Content Creator
- `!promote` — Post your YouTube video to the promotion channel

### 📣 KvK Announcer
- Automated KvK phase announcements with timed event scheduling

---

## ⚙️ Environment Variables

All secrets and configuration are injected via environment variables (no hardcoded credentials).

| Variable | Description |
|---|---|
| `DISCORD_TOKEN` | Discord bot token |
| `GOOGLE_CREDENTIALS_JSON` | Google service account credentials (full JSON string) |
| `SPREADSHEET_ID` | Target Google Sheets spreadsheet ID |
| `OCR_SERVICE_HOST` | OCR microservice host (default: `ocr-service.railway.internal`) |
| `OCR_SERVICE_PORT` | OCR microservice port (default: `23333`) |

---

## 🐍 OCR Microservice

Located in `ocr-service/`. Built with **RapidOCR** (chosen over PaddleOCR due to Linux/Railway compatibility).

Accepts image URLs from Discord attachment links, runs OCR, and returns extracted text + detected donation amounts and dates over a TCP socket.

> **Note:** PaddleOCR is not viable on Linux due to oneDNN/libGL dependencies. RapidOCR is the working alternative.

---

## 🚀 Local Development

1. Clone the repo
2. Create a Google service account and download the credentials JSON
3. Set up your environment variables (see above) — or use local fallbacks in `appsettings.json`
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

---

## 📄 License

Private repository — for internal HOGS guild use only.
