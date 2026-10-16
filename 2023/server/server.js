// Local Rec Room server. Stage 1: log every request so we can see what the
// client expects, and answer the ones we've implemented.
const https = require("https");
const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const PORT = 9443;
const BASE = `https://localhost:${PORT}`;
const TLS = {
  key: fs.readFileSync(path.join(__dirname, "certs", "localhost.key")),
  cert: fs.readFileSync(path.join(__dirname, "certs", "localhost.crt")),
};
const LOG = path.join(__dirname, "requests.jsonl");
// The request log rolls over to requests.1.jsonl (replacing the previous one) at this size.
const LOG_MAX_BYTES = 50 * 1024 * 1024;
let logBytes = fs.existsSync(LOG) ? fs.statSync(LOG).size : 0;
function logRequest(entry) {
  const line = JSON.stringify(entry) + "\n";
  try {
    if (logBytes + Buffer.byteLength(line) > LOG_MAX_BYTES) {
      logBytes = 0;
      fs.renameSync(LOG, LOG.replace(/\.jsonl$/, ".1.jsonl"));
    }
    fs.appendFileSync(LOG, line);
    logBytes += Buffer.byteLength(line);
  } catch (e) {
    console.log(`  request log: ${e.message}`);
  }
}

// Every RecNet service is served by this one process under its own path prefix,
// so the request log shows which service the client thinks it's talking to.
const SERVICES = [
  "Auth", "API", "WWW", "Notifications", "Images", "CDN", "Commerce", "Matchmaking",
  "Storage", "Chat", "Leaderboard", "Accounts", "Link", "Lists", "RoomComments", "Clubs",
  "Rooms", "PlatformNotifications", "Moderation", "DataCollection", "BugReporting",
  "Discovery", "PlayerSettings", "Studio", "GameLogs", "Strings", "Econ", "StringsCDN",
  "Data", "Cards",
];

// Implemented routes: "METHOD /path" -> handler(req, body, url) returning {status, json}
const routes = {
  // Name server (NMGFAGNOENB.NameServerResponse)
  // URLs use the Host the client connected with, so clients joining by IP stay on this server.
  "GET /": (req) => {
    const base = req.headers.host ? `https://${req.headers.host}` : BASE;
    const json = { RecNetStatus: null };
    for (const s of SERVICES) json[s] = `${base}/${s.toLowerCase()}`;
    return { status: 200, json };
  },

  // IFFMBLBHKGF: 0 = ValidForPlay, 1 = UpdateRequired. BCNMDIFJPOI: 0 = None ... 4 = Require
  "GET /api/api/versioncheck/v4": () => ({ status: 200, json: { VersionStatus: 0, UpdateNotificationStage: 0 } }),
  // Game configs (MFHJJGMGHBL: Key/Value/StartTime/EndTime), editable in data/gameconfigs.json.
  "GET /api/api/gameconfigs/v1/all": () => ({
    status: 200,
    json: Object.entries(gameConfigs()).map(([Key, Value]) => ({ Key, Value: String(Value), StartTime: null, EndTime: null })),
  }),
  // ConfigSettings (PIHMNIFANAG). Missing -> "Error initializing Core Systems" after PLAY.
  "GET /api/api/config/v2": () => ({ status: 200, json: CONFIG_SETTINGS }),

  // Analytics keys; dummy values (empty object deserializes to null).
  "GET /api/api/config/v1/amplitude": () => ({
    status: 200,
    json: { AmplitudeKey: "offline", RudderStackKey: "offline", StatSigKey: "" },
  }),

  // Analytics: accept and drop.
  "POST /datacollection/data/event": () => ({ status: 200, json: null }),

  "GET /econ/api/avatar/v1/defaultunlocked": () => ({ status: 200, json: [] }),

  // The logged-in account (identified by the bearer token).
  "GET /accounts/account/me": (req) => {
    const account = authedAccount(req);
    if (!account) return { status: 401, json: null };
    return {
      status: 200,
      json: {
        ...accountProfile(account),
        // Placeholder so the game skips its "Add your email" prompt; nothing is sent anywhere.
        email: `player${account.accountId}@localhost`,
        phone: null,
        birthday: "2000-01-01T00:00:00Z",
        availableUsernameChanges: 999,
        isPhoneVerified: false,
        isEmailVerified: true,
      },
    };
  },

  // Which of these platform (Steam friend) IDs have local accounts.
  "POST /auth/cachedlogin/forplatformids": (req, body) => {
    const ids = new URLSearchParams(body).getAll("id");
    return { status: 200, json: accounts.filter((a) => ids.includes(a.platformId)).map(cachedLogin) };
  },

  // Local accounts never have passwords.
  "GET /accounts/account/me/haspassword": () => ({ status: 200, json: false }),

  // OAuth token endpoint (form-encoded). Local accounts never need a password,
  // so any login grant for a known account succeeds.
  "POST /auth/connect/token": (req, body) => {
    const form = new URLSearchParams(body);
    let account;
    if (form.get("grant_type") === "refresh_token") {
      const claims = verifyToken(form.get("refresh_token"));
      account = claims && accounts.find((a) => a.accountId === Number(claims.sub));
    } else if (form.get("account_id")) {
      account = accounts.find((a) => a.accountId === Number(form.get("account_id")));
    } else if (form.get("platform_id")) {
      account = getOrCreateAccount(Number(form.get("platform") || 0), form.get("platform_id"));
    }
    if (!account) return { status: 400, json: { error: "invalid_grant" } };
    return { status: 200, json: issueTokens(account) };
  },

  // EasyAntiCheat challenge. EAC isn't running, so this is only to satisfy the client.
  "GET /auth/eac/challenge": () => ({ status: 200, json: "b2ZmbGluZQ==" }),

  // --- Post-login ---
  // Nobody is banned on a local server.
  "POST /api/api/PlayerReporting/v1/moderationBlockDetails": () => ({
    status: 200,
    json: { ReportCategory: 0, Duration: 0, GameSessionId: 0, IsHostKick: false, IsBan: false, Message: "" },
  }),
  "GET /api/api/playerReputation/v2/bulk": (req, body, url) => ({
    status: 200,
    json: idsParam(url).map((id) => ({
      AccountId: id, IsCheerful: false, SubscriberCount: 0, CheerCredit: 0,
      CheerGeneral: 0, CheerHelpful: 0, CheerCreative: 0, CheerGreatHost: 0, CheerSportsman: 0,
    })),
  }),
  "GET /api/api/players/v2/progression/bulk": (req, body, url) => ({
    status: 200,
    json: idsParam(url).map((id) => ({ PlayerId: id, Level: 1, XP: 0, ...store("progression").get(id) })),
  }),
  "GET /econ/api/objectives/v1/myprogress": () => ({ status: 200, json: { Objectives: [], ObjectiveGroups: [] } }),
  // ODJKDKINFJB; called when entering a room.
  "POST /econ/api/objectives/v1/cleargroup": (req, body) => {
    let group = 0;
    try { group = JSON.parse(body).Group || 0; } catch {}
    return { status: 200, json: { Group: group, IsCompleted: false, ClearedAt: new Date().toISOString() } };
  },
  // Rec Center photo slideshow (MNHLHHDOPEN.AOKOPOFJLBF).
  "GET /api/api/images/v1/slideshow": () => ({
    status: 200, json: { Images: [], ValidTill: new Date(Date.now() + 3600e3).toISOString() },
  }),
  "GET /api/api/customAvatarItems/v1/isCreationEnabled": () => ({ status: 200, json: true }),
  "GET /api/api/customAvatarItems/v1/isRenderingEnabled": () => ({ status: 200, json: true }),
  "GET /api/api/customAvatarItems/v1/isCreationAllowedForAccount": () => ({ status: 200, json: { IsAllowed: true } }),
  // Wrong shape here fails login with "code: doll". An object with no known
  // fields deserializes to null, so the map must be wrapped in its field.
  "POST /api/api/customAvatarItems/GetCustomAvatarItemCurrentSavesForLegacyAvatarItems": () => ({
    status: 200,
    json: { CustomAvatarItemSavesByAvatarItemDesc: {} },
  }),
  "GET /clubs/subscription/mine/member": () => ({ status: 200, json: [] }),
  // An object with no known fields deserializes to null, so include one.
  "GET /platformnotifications/crm/me/config/v2": () => ({ status: 200, json: { CRMID: "" } }),
  "POST /datacollection/data/heartbeat": () => ({ status: 200, json: null }),

  // Player settings, saved per account.
  "GET /playersettings/playersettings": (req) => {
    const account = authedAccount(req);
    if (!account) return { status: 401, json: null };
    const settings = { ...DEFAULT_SETTINGS, ...store("settings").get(account.accountId) };
    return { status: 200, json: Object.entries(settings).map(([Key, Value]) => ({ Key, Value })) };
  },
  // Client sends form fields key=...&value=... (lowercase); accept JSON Key/Value too.
  "PUT /playersettings/playersettings": (req, body) => {
    const account = authedAccount(req);
    if (!account) return { status: 401, json: null };
    let entry = Object.fromEntries(new URLSearchParams(body));
    try { if (body.trim().startsWith("{")) entry = JSON.parse(body); } catch {}
    const key = entry.key ?? entry.Key;
    if (key !== undefined) {
      const settings = store("settings").get(account.accountId) || {};
      settings[key] = entry.value ?? entry.Value;
      store("settings").set(account.accountId, settings);
    }
    return { status: 200, json: null };
  },

  // SignalR negotiate for the notifications hub (WebSocket handled in server.on("upgrade")).
  "POST /notifications/hub/v1/negotiate": () => ({
    status: 200,
    json: {
      connectionId: crypto.randomUUID(),
      availableTransports: [{ transport: "WebSockets", transferFormats: ["Text", "Binary"] }],
    },
  }),

  "GET /econ/api/checklist/v1/current": () => ({ status: 200, json: [] }),

  // Avatar outfit, saved per account in data/outfits.json.
  "GET /api/outfits/me": (req) => {
    const account = authedAccount(req);
    if (!account) return { status: 401, json: null };
    return { status: 200, json: outfitFor(account.accountId) };
  },
  "GET /api/outfits/bulk": (req, body, url) => ({
    status: 200,
    json: idsParam(url).map((id) => ({ AccountId: id, ...outfitFor(id) })),
  }),
  // Owned avatar items: the whole catalog is unlocked for everyone. Items the player
  // doesn't own are stripped from outfits as "trial items" (and that crashed avatar spawn).
  "GET /econ/api/avatar/v4/items": () => ({ status: 200, json: AVATAR_ITEMS }),
  "GET /econ/api/avatar/v1/defaultbaseavataritems": () => ({ status: 200, json: [] }),
  "POST /api/api/relationships/v1/bulkignoreplatformusers": () => ({ status: 200, json: null }),
  "GET /api/api/quickPlay/v1/getandclear": () => ({ status: 200, json: null }),
  // Cheat reports from the client's own checks: log and ignore.
  "POST /api/api/PlayerReporting/v1/hile": (req, body) => {
    console.log(`  hile report: ${decodeURIComponent(body).slice(0, 200)}`);
    return { status: 200, json: null };
  },
  "GET /econ/econ/customAvatarItems/v1/owned": () => ({ status: 200, json: { Results: [], TotalResults: 0 } }),

  // Chat threads (none yet).
  "GET /chat/thread": () => ({ status: 200, json: [] }),
  // Quitting: take the player out of their room so they stop counting toward its capacity.
  "POST /matchmaking/player/logout": (req) => {
    const account = authedAccount(req);
    if (account) currentInstance.delete(account.accountId);
    return { status: 200, json: null };
  },

  // Friends / relationships, saved per account in data/relationships.json.
  "GET /api/api/relationships/v2/get": (req) => {
    const account = authedAccount(req);
    if (!account) return { status: 401, json: null };
    return { status: 200, json: store("relationships").get(account.accountId) || [] };
  },

  // Presence for ?id=1&id=2...
  "GET /matchmaking/player": (req, body, url) => ({
    status: 200,
    json: idsParam(url).map((id) => accounts.find((a) => a.accountId === id)).filter(Boolean).map(playerStatus),
  }),

  // Matchmaking into the player's dorm (IPLDFHBCIFI). RoomInstanceType 2 = Dormroom.
  "POST /matchmaking/matchmake/dorm": (req) => {
    const account = authedAccount(req);
    if (!account) return { status: 401, json: null };
    const instance = dormInstance(account);
    currentInstance.set(account.accountId, instance);
    return { status: 200, json: { ErrorCode: 0, RoomInstance: instance } };
  },

  // Room data for ?id=1&id=2...
  "GET /rooms/rooms/bulk": (req, body, url) => ({
    status: 200, json: idsParam(url).map(getRoom).filter(Boolean),
  }),
  // Room by name (BPEOCGIOAKG), e.g. ?name=RecCenter.
  "GET /rooms/rooms": (req, body, url) => {
    const room = roomByName(url.searchParams.get("name") || "");
    return room ? { status: 200, json: room } : { status: 404, json: null };
  },

  // --- Room browser ---
  // Paged lists (CMNOOFJGFGG {Results, TotalResults}); only the Originals exist offline.
  "GET /rooms/rooms/hot": (req, body, url) => ({ status: 200, json: page(listedRooms(), url) }),
  "GET /rooms/rooms/search": (req, body, url) => {
    const q = (url.searchParams.get("query") || url.searchParams.get("name") || "").toLowerCase().replace(/^\^/, "");
    const all = [...listedRooms(), ...RRO_ROOMS.filter((r) => !LISTED_RRO.includes(r.Name)), ...Object.values(rooms)];
    const hits = all.filter((r) => r.Name.toLowerCase().includes(q) || (r.Description || "").toLowerCase().includes(q));
    return { status: 200, json: page(hits, url) };
  },
  "GET /rooms/rooms/rro_ids": () => ({ status: 200, json: RRO_ROOMS.map((r) => r.RoomId) }),
  "GET /rooms/rooms/visitedby/me": (req, body, url) => {
    const account = authedAccount(req);
    const ids = (account && store("visits").get(account.accountId)) || [];
    const list = ids.map(getRoom).filter(Boolean);
    const skip = Number(url.searchParams.get("skip") || 0), take = Number(url.searchParams.get("take") || 100);
    return { status: 200, json: list.slice(skip, skip + take) };
  },
  "GET /rooms/rooms/favoritedby/me": () => ({ status: 200, json: [] }),
  "GET /rooms/rooms/ownedby/me": (req) => {
    const account = authedAccount(req);
    return { status: 200, json: account ? [dormRoom(account)] : [] };
  },
  "GET /rooms/rooms/base": () => ({ status: 200, json: [] }),
  // KDEDNEJNLKP {SeedRoom, Rooms}[]
  "GET /rooms/rooms/recommendations": () => ({ status: 200, json: [] }),
  "GET /rooms/rooms/curated_playlists": () => ({ status: 200, json: [] }),
  // PHGMIKFHGOL {FeaturedRoomGroupId, Name, Rooms: [{RoomId, RoomName, ImageName}]}
  "GET /rooms/featuredrooms/current": () => ({
    status: 200,
    json: {
      FeaturedRoomGroupId: 1, Name: "Rec Room Originals",
      Rooms: listedRooms().map((r) => ({ RoomId: r.RoomId, RoomName: r.Name, ImageName: r.ImageName })),
    },
  }),
  // MMADAPFDHJC
  "GET /api/api/rooms/v1/filters": () => ({
    status: 200, json: { PinnedFilters: [], PopularFilters: [], TrendingFilters: [] },
  }),
  // Lists service: algorithmic lists (CLAOCBOFGIM {Type, Entities: [{Id, Context}]}).
  "GET /lists/curatedlists": (req, body, url) => ({
    status: 200,
    json: {
      ListId: 1, CreatorAccountId: 0, Name: url.searchParams.get("name") || "", Description: "",
      ImageName: "", Type: Number(url.searchParams.get("type") || 0), ItemIds: [], CreatedAt: "2023-12-07T00:00:00Z",
    },
  }),
  "GET /rooms/rooms/createdby/me": (req) => {
    const account = authedAccount(req);
    return { status: 200, json: Object.values(rooms).filter((r) => account && r.CreatorAccountId === account.accountId) };
  },

  // In-room services with nothing to show yet.
  "GET /econ/api/equipment/v2/getUnlocked": () => ({ status: 200, json: [] }),
  "GET /econ/api/roomkeys/v1/room": () => ({ status: 200, json: [] }),
  "GET /api/api/keepsakes/categories": () => ({ status: 200, json: [] }),
  "GET /api/api/keepsakes/globalconfig": () => ({
    status: 200, json: { KeepsakeFeatureEnabled: false, KeepsakeRoomLimit: 0, SocialXpBoostEnabled: false },
  }),
  // Photon room access (KLMLJLBJNNC).
  "GET /rooms/photon_access_token": (req, body, url) => {
    const account = authedAccount(req);
    const inst = account && currentInstance.get(account.accountId);
    return {
      status: 200,
      json: { RoomInstanceId: inst ? inst.RoomInstanceId : 0, PhotonAccessToken: "local", Permissions: [] },
    };
  },
  // Leaving to "no room" (IPLDFHBCIFI).
  "POST /matchmaking/matchmake/none": (req) => {
    const account = authedAccount(req);
    if (account) currentInstance.delete(account.accountId);
    return { status: 200, json: { ErrorCode: 0, RoomInstance: null } };
  },
  "GET /econ/api/roomcurrencies/v1/currencies": () => ({ status: 200, json: [] }),
  "GET /econ/api/roomcurrencies/v1/getAllBalances": () => ({ status: 200, json: [] }),
  "GET /econ/api/consumables/v2/getUnlocked": () => ({ status: 200, json: [] }),
  "GET /api/outfits/me/saved": () => ({ status: 200, json: [] }),
  "GET /api/api/images/v2/named": () => ({ status: 200, json: [] }),
  "GET /econ/api/avatar/v2/gifts": () => ({ status: 200, json: [] }),
  "GET /econ/api/gamerewards/v1/pending": () => ({ status: 200, json: [] }),
  "GET /api/api/playerevents/v1/all": () => ({ status: 200, json: [] }),
  "GET /econ/api/roomkeys/v1/mine": () => ({ status: 200, json: [] }),
  "GET /api/api/PlayerReporting/v1/voteToKickReasons": () => ({ status: 200, json: [] }),
  "GET /cards/iam/me/channels/Announcements": () => ({ status: 200, json: [] }),
  "GET /clubs/announcements/v2/mine/unread": () => ({ status: 200, json: [] }),
  "GET /clubs/announcements/v2/subscription/mine/unread": () => ({ status: 200, json: [] }),
  "PUT /matchmaking/player/statusvisibility": () => ({ status: 200, json: null }),
  "GET /api/api/communityboard/v2/current": () => ({ status: 200, json: null }),
  "GET /econ/api/subscriptionseasons/v1/seasons/current": () => ({ status: 200, json: null }),
  "POST /econ/api/CampusCard/v1/UpdateAndGetSubscription": () => ({ status: 200, json: null }),
  "GET /moderation/voice/config": () => ({ status: 200, json: null }),
  "GET /api/api/config/v1/azurespeech": () => ({ status: 200, json: null }),
  "GET /api/api/config/v1/backtrace": () => ({ status: 200, json: null }),

  "GET /api/api/messages/v2/get": () => ({ status: 200, json: [] }),
  "GET /econ/api/challenge/v2/getCurrent": () => ({ status: 200, json: null }),
  "GET /clubs/club/home/me": () => ({ status: 200, json: null }),
  "GET /cdn/config/LoadingScreenTipData": () => ({ status: 200, json: [] }),

  // Matchmaking presence.
  "POST /matchmaking/player/login": (req) => ({ status: 200, json: playerStatus(authedAccount(req)) }),
  "POST /matchmaking/player/heartbeat": (req) => ({ status: 200, json: playerStatus(authedAccount(req)) }),
  // Sent when pressing PLAY; a 404 here fails with "Unable to login (code: 5)".
  "POST /matchmaking/player/exclusivelogin": (req) => ({ status: 200, json: playerStatus(authedAccount(req)) }),
  // Photon settings (Matchmaking.PJLFHHCDDMD). Placeholders until a local Photon server exists.
  // Wrapped as {Success, Error, Value} (COLCODGGIFK<T>); a bare object reads as a failure.
  "GET /matchmaking/player/connection-info": (req, body, url) => ({
    status: 200,
    json: ok({
      PhotonRegion: "us",
      PhotonRoomId: url.searchParams.get("roomInstanceId") ? `room-${url.searchParams.get("roomInstanceId")}` : "",
      PhotonAuthToken: "local",
      // Our self-hosted Photon server ignores app ids, but the client needs them set.
      PhotonRealtimeAppId: "00000000-0000-0000-0000-000000000001",
      PhotonVoiceAppId: "00000000-0000-0000-0000-000000000002",
      PhotonChatAppId: "00000000-0000-0000-0000-000000000003",
      VoiceServerId: "local", VoiceConnectionInfo: "",
    }),
  }),
  // Regions to ping (Matchmaking.CHNJHGIJJAK {Id, Address}); an empty list logs
  // "Unable to retrieve any available enabled regions for ping".
  "GET /matchmaking/player/qos": (req) => ({
    status: 200,
    json: [{ Id: "us", Address: (req.headers.host || "127.0.0.1").split(":")[0].replace(/^localhost$/, "127.0.0.1") }],
  }),
  "PUT /matchmaking/player/photonregionpings": () => ({ status: 200, json: null }),
  "PUT /matchmaking/player/gameserverregionpings": () => ({ status: 200, json: null }),

  // Public profiles for ?id=1&id=2...
  "GET /accounts/account/bulk": (req, body, url) => {
    const ids = url.searchParams.getAll("id").map(Number);
    return { status: 200, json: accounts.filter((a) => ids.includes(a.accountId)).map(accountProfile) };
  },
};

// Routes with path parameters: [method, regex, handler(match, req, body, url)]
const patternRoutes = [
  ["GET", /^\/clubs\/subscription\/details\/(\d+)$/, () => ({ status: 200, json: [] })],
  ["GET", /^\/chat\/thread\/\w+$/, () => ({ status: 200, json: [] })],
  ["GET", /^\/econ\/api\/storefronts\/v3\/giftdropstore\/\d+$/, () => ({ status: 200, json: [] })],
  ["GET", /^\/econ\/api\/ugcPurchasables\/v1\/items\/room\/\d+$/, () => ({ status: 200, json: [] })],
  // In-room services (empty for now).
  ["GET", /^\/econ\/econ\/roomOffer\/room\/\d+\/purchaseCounts$/, () => ({ status: 200, json: ok([]) })],
  ["GET", /^\/econ\/econ\/(roomGiftDropShops|roomInventory|roomOffer)\/room\/\d+(\/[a-zA-Z]+)?$/, () => ({ status: 200, json: [] })],
  ["GET", /^\/rooms\/rooms\/\d+\/playerdata\/me$/, () => ({ status: 200, json: { Data: null } })],
  // Room XP settings (CHJMHJHCKHA), shown on the room page.
  ["GET", /^\/rooms\/rooms\/\d+\/experience$/, () => ({ status: 200, json: { Enabled: false, DailyLimit: 0 } })],
  ["GET", /^\/rooms\/rooms\/\d+\/experience\/player$/, () => ({
    status: 200, json: ok({ Experience: 0, RoomExperienceEnabled: false, ConcurrencyCode: null }),
  })],
  ["GET", /^\/api\/api\/keepsakes\/rooms\/\d+$/, () => ({ status: 200, json: [] })],
  ["GET", /^\/rooms\/rooms\/(\d+)$/, (m) => (getRoom(m[1]) ? { status: 200, json: getRoom(m[1]) } : { status: 404, json: null })],
  ["GET", /^\/rooms\/rooms\/ownedby\/(\d+)$/, (m) => {
    const owner = accounts.find((a) => a.accountId === Number(m[1]));
    return { status: 200, json: owner ? [dormRoom(owner)] : [] };
  }],
  // "More like this" on a room page (KDEDNEJNLKP {SeedRoom, Rooms}).
  ["GET", /^\/rooms\/rooms\/(\d+)\/similar$/, (m) => ({
    status: 200,
    json: { SeedRoom: getRoom(m[1]) || null, Rooms: listedRooms().filter((r) => r.RoomId !== Number(m[1])).slice(0, 8) },
  })],
  // LOGBPELGMFB
  ["GET", /^\/rooms\/rooms\/\d+\/interactionby\/me$/, () => ({
    status: 200, json: { Cheered: false, Favorited: false, LastVisitedAt: null },
  })],
  ["GET", /^\/lists\/algorithmiclists\/\w+$/, (m, req, body, url) => ({
    status: 200,
    json: { Type: Number(url.searchParams.get("type") || 1), Entities: listedRooms().map((r) => ({ Id: String(r.RoomId), Context: "" })) },
  })],
  // Rooms needing RR+ / developer access: none.
  ["GET", /^\/matchmaking\/rooms\/requiring\/\w+$/, () => ({ status: 200, json: [] })],
  // Instance state changes (quests mark themselves in progress, rooms go private, ...).
  ["PUT", /^\/matchmaking\/roominstance\/(\d+)\/(inprogress|markprivate|matchpolicy|roomCode)$/, (m, req, body) => {
    const form = new URLSearchParams(body);
    for (const inst of currentInstance.values()) {
      if (inst.RoomInstanceId !== Number(m[1])) continue;
      if (m[2] === "inprogress") inst.IsInProgress = form.get("inProgress") === "True";
      if (m[2] === "markprivate") inst.IsPrivate = true;
    }
    return { status: 200, json: null };
  }],
  // Leaderboards (BNDGBANMLMA), kept per room and stat channel in data/leaderboards.json.
  ["POST", /^\/leaderboard\/leaderboard\/CheckAndSetStat$/, (m, req, body) => {
    const account = authedAccount(req);
    const r = jsonBody(body);
    if (!account || r.StatValue == null) return { status: 200, json: 1 };  // InvalidStat
    const key = `${r.RoomId}:${r.StatChannel}`;
    const board = store("leaderboards").get(key) || {};
    board[account.accountId] = r.StatValue;
    store("leaderboards").set(key, board);
    return { status: 200, json: 0 };  // Success
  }],
  [/GET|POST/, /^\/leaderboard\/leaderboard\/(GetNearbyScores|GetPlayerRank|GetRanks)$/, (m, req, body, url) => {
    const r = { ...Object.fromEntries(url.searchParams), ...jsonBody(body) };
    const account = authedAccount(req);
    const roomId = r.RoomId || (account && currentInstance.get(account.accountId) || {}).RoomId;
    const board = store("leaderboards").get(`${roomId}:${r.StatChannel}`) || {};
    const asc = r.SortAscending === true || r.SortAscending === "true";
    const rows = Object.entries(board)
      .map(([id, score]) => ({ PlayerId: Number(id), Score: score }))
      .sort((a, b) => (asc ? a.Score - b.Score : b.Score - a.Score))
      .map((row, i) => ({ ...row, Rank: i + 1 }));
    const playerId = Number(r.PlayerId || (account && account.accountId));
    if (m[1] === "GetPlayerRank") {
      return { status: 200, json: rows.find((row) => row.PlayerId === playerId) || { PlayerId: playerId, Score: 0, Rank: 0 } };
    }
    if (m[1] === "GetNearbyScores") {
      const at = Math.max(0, rows.findIndex((row) => row.PlayerId === playerId));
      const half = Math.floor(Number(r.WindowSize || 10) / 2);
      return { status: 200, json: { Rows: rows.slice(Math.max(0, at - half), at + half + 1) } };
    }
    return { status: 200, json: { Rows: rows.slice(0, Number(r.Take || r.take || r.Count || 50)) } };
  }],
  // Going to a room (Matchmaking: matchmake/room/{roomId}[/{subRoomId}]).
  ["POST", /^\/matchmaking\/matchmake\/room\/(\d+)(?:\/(\d+))?$/, matchmakeRoom],
  // Joining a player where they are.
  ["POST", /^\/matchmaking\/matchmake\/player\/(\d+)$/, (m, req) => {
    const account = authedAccount(req);
    if (!account) return { status: 401, json: null };
    const target = currentInstance.get(Number(m[1]));
    return target ? enterInstance(account, target) : matchmakeError(2);
  }],
  ["POST", /^\/matchmaking\/matchmake\/instance\/(\d+)$/, (m, req) => {
    const account = authedAccount(req);
    if (!account) return { status: 401, json: null };
    const target = [...currentInstance.values()].find((i) => i.RoomInstanceId === Number(m[1]));
    if (target) return enterInstance(account, target);
    const room = RRO_ROOMS.find((r) => r.SubRooms.some((s) => s.SubRoomId === Number(m[1])));
    return room ? enterInstance(account, publicInstance(room, room.SubRooms.find((s) => s.SubRoomId === Number(m[1])))) : matchmakeError(1);
  }],
  ["POST", /^\/matchmaking\/roominstance\/\d+\/reportjoinresult$/, () => ({ status: 200, json: null })],
  ["GET", /^\/econ\/api\/roomconsumables\/v1\/roomConsumable\/room\/\d+(\/me)?$/, () => ({ status: 200, json: [] })],
  ["GET", /^\/econ\/api\/storefronts\/v4\/balance\/\d+$/, () => ({ status: 200, json: [] })],
  ["POST", /statsigUserProperties$/i, () => ({ status: 200, json: null })],
  // Third-party analytics/crash hosts redirected by patch_hosts.py: accept and drop.
  ["POST", /^\/analytics\/rudderstack\//, () => ({ status: 200, raw: "OK" })],
  ["POST", /^\/analytics\//, () => ({ status: 200, json: { code: 200 } })],
  ["GET", /^\/analytics\//, () => ({ status: 200, json: {} })],
  // Saved logins for a platform user (platform 0 = Steam). Every local account linked to
  // that user is listed, so "Switch Account" can pick between them.
  ["GET", /^\/auth\/cachedlogin\/forplatformid\/(\d+)\/(\w+)$/, (m) => {
    getOrCreateAccount(Number(m[1]), m[2]);
    const linked = accounts.filter((a) => a.platform === Number(m[1]) && a.platformId === m[2]);
    return { status: 200, json: linked.map(cachedLogin) };
  }],
];

// Players choose their own username / display name.
function changeName(field) {
  return (m, req, body) => {
    const account = authedAccount(req);
    if (!account) return { status: 401, json: null };
    const value = readField(req, body, field === "username" ? ["username", "name", "value"] : ["displayName", "displayname", "name", "value"]);
    const error = field === "username" ? validateUsername(value, account) : validateDisplayName(value);
    if (error) return { status: 400, json: { success: false, error, value: null } };
    account[field] = value;
    saveAccounts();
    return { status: 200, json: { success: true, error: null, value: accountProfile(account) } };
  };
}

function readField(req, body, names) {
  let data = {};
  try {
    data = (req.headers["content-type"] || "").includes("json") ? JSON.parse(body) : Object.fromEntries(new URLSearchParams(body));
  } catch {}
  if (typeof data === "string") return data.trim();
  for (const n of names) if (typeof data[n] === "string") return data[n].trim();
  return "";
}

function validateUsername(name, account) {
  if (!/^[A-Za-z0-9_]{3,20}$/.test(name)) return "Usernames must be 3-20 letters, numbers or underscores.";
  const taken = accounts.some((a) => a !== account && a.username.toLowerCase() === name.toLowerCase());
  return taken ? "That username is already taken." : null;
}
const validateDisplayName = (name) =>
  name.length >= 1 && name.length <= 32 ? null : "Display names must be 1-32 characters.";

for (const method of ["PUT", "POST"]) {
  patternRoutes.push([method, /^\/api\/outfits\/me$/, saveOutfit]);
  patternRoutes.push([method, /^\/accounts\/account\/me\/username$/i, changeName("username")]);
  patternRoutes.push([method, /^\/accounts\/account\/me\/displayname$/i, changeName("displayName")]);
}

// --- Local accounts (persisted to data/accounts.json) ---
const DATA_DIR = path.join(__dirname, "data");
const ACCOUNTS_FILE = path.join(DATA_DIR, "accounts.json");
fs.mkdirSync(DATA_DIR, { recursive: true });
const accounts = fs.existsSync(ACCOUNTS_FILE) ? JSON.parse(fs.readFileSync(ACCOUNTS_FILE, "utf8")) : [];
const saveAccounts = () => fs.writeFileSync(ACCOUNTS_FILE, JSON.stringify(accounts, null, 2));

function getOrCreateAccount(platform, platformId) {
  let account = accounts.find((a) => a.platform === platform && a.platformId === platformId);
  if (!account) {
    const accountId = accounts.length + 1;
    account = {
      accountId, platform, platformId,
      username: `Player${accountId}`, displayName: `Player ${accountId}`,
      createdAt: new Date().toISOString(),
    };
    accounts.push(account);
    saveAccounts();
  }
  return account;
}

// --- Simple per-account JSON stores: data/<name>.json = { [accountId]: value } ---
const stores = {};
function store(name) {
  if (stores[name]) return stores[name];
  const file = path.join(DATA_DIR, `${name}.json`);
  const data = fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, "utf8")) : {};
  return (stores[name] = {
    get: (id) => data[id],
    set: (id, value) => { data[id] = value; fs.writeFileSync(file, JSON.stringify(data, null, 2)); },
  });
}

// Outfit (PBNFHINDLJA, formatter GIFFBKONEGM). DataVersion 0 = legacy: the
// client then requires LegacyData (LEBNJGPEAGE) or fails "Failed to parse avatar data!".
const DEFAULT_OUTFIT = {
  Slot: 0,
  Name: "",
  Accessibility: 0,
  DataVersion: 0,
  LegacyData: { SelectionsV1: "", SelectionsV2: "", FaceFeatures: "", SkinColor: "", HairColor: "" },
  Selections: [],          // [{ CustomAvatarItemId, BodyPart, BakedUnityAssetFileName, AdditionalConfiguration }]
  CustomizationSettings: null,
  ThumbnailFileName: null,
};
const outfitFor = (id) => store("outfits").get(id) || DEFAULT_OUTFIT;

// Asset names -> display names: "BB_TMNT_Turtle_Don_Shirt" -> "TMNT Turtle Don Shirt",
// "Hat_Mobster" -> "Mobster Hat", "PulloverHoodie_Shirt" -> "Pullover Hoodie Shirt".
const ITEM_SLOTS = new Set(["Hat", "Hair", "Eye", "Eyes", "Glasses", "Mask", "Shirt", "Neck", "Wrist", "Belt", "Ear", "Shoulder", "Torso", "Legs", "Shoes"]);
function friendlyItemName(name) {
  let words = name.replace(/\)? Variant$/, "").replace(/[()]/g, "").replace(/_?INTERNALUSEONLY/i, "")
    .split("_").filter((w) => w && !/^(BB|FB)$/.test(w));
  if (words.length > 1 && ITEM_SLOTS.has(words[0][0].toUpperCase() + words[0].slice(1))) words.push(words.shift());
  words = words.map((w) => w[0].toUpperCase() + w.slice(1))
    .join(" ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .replace(/(\D)(\d+)$/, "$1 $2")
    .split(" ");
  // The slot suffix is redundant after a garment name: "Cat Hat Hat", "Clock Necklace Neck".
  const last = words[words.length - 1];
  if (words.length > 2 && (/^(Neck|Wrist|Ear|Shoulder|Leg|Legs|Eye|Belt)$/.test(last) || words.slice(0, -1).includes(last))) words.pop();
  return words.join(" ").trim() || name;
}

// Full avatar item catalog, extracted from the game by tools/extract_avatar_items.py.
// DTO NPDFEKIHHDP; AvatarItemType 0 = Outfit, Rarity 0 = Common.
const AVATAR_CATALOG_FILE = path.join(DATA_DIR, "avatar_catalog.json");
// RecNet display names recreated by tools/build_item_names.py; items missing there get a derived name.
const AVATAR_NAMES_FILE = path.join(DATA_DIR, "avatar_item_names.json");
const AVATAR_NAMES = fs.existsSync(AVATAR_NAMES_FILE) ? JSON.parse(fs.readFileSync(AVATAR_NAMES_FILE, "utf8")) : {};
const AVATAR_ITEMS = (fs.existsSync(AVATAR_CATALOG_FILE) ? JSON.parse(fs.readFileSync(AVATAR_CATALOG_FILE, "utf8")) : [])
  .map((item, i) => ({
    AvatarItemType: 0,
    AvatarItemDesc: item.AvatarItemDesc,
    FriendlyName: AVATAR_NAMES[item.AvatarItemDesc]?.FriendlyName ?? friendlyItemName(item.Name),
    Tooltip: "",
    Rarity: 0,
    TagList: "",
    AvatarItemId: i + 1,
    IsBaseAvatarItem: false,
  }));

// Saving an outfit: store exactly what the client sends (JSON), so it round-trips.
function saveOutfit(m, req, body) {
  const account = authedAccount(req);
  if (!account) return { status: 401, json: null };
  let outfit;
  try { outfit = JSON.parse(body); } catch {}
  if (!outfit || typeof outfit !== "object") {
    console.log(`  outfit save with unexpected body: ${body.slice(0, 300)}`);
    return { status: 400, json: null };
  }
  store("outfits").set(account.accountId, outfit);
  return { status: 200, json: outfit };
}

// --- Rooms, persisted in data/rooms.json (keyed by RoomId) ---
// Built-in scenes come from the game's AGRoomRuntimeConfig in resources.assets.
const SCENES = {
  DormRoom: "76d98498-60a1-430c-ab76-b54a29b7a163",
  RecCenter: "cbad71af-0831-44d8-b8ef-69edafa841f6",
};
const ROOMS_FILE = path.join(DATA_DIR, "rooms.json");
const rooms = fs.existsSync(ROOMS_FILE) ? JSON.parse(fs.readFileSync(ROOMS_FILE, "utf8")) : {};
const saveRooms = () => fs.writeFileSync(ROOMS_FILE, JSON.stringify(rooms, null, 2));

// Room stats (AMGNHJLDGCI). Null crashes the room UI in RoomModel.GetCheerCountText.
const EMPTY_ROOM_STATS = { CheerCount: 0, FavoriteCount: 0, VisitorCount: 0, VisitCount: 0 };
for (const room of Object.values(rooms)) room.Stats = room.Stats || { ...EMPTY_ROOM_STATS };

function makeRoom({ roomId, name, description, creatorId, scene, isDorm, maxPlayers }) {
  return {
    RoomId: roomId, Name: name, Description: description, CreatorAccountId: creatorId,
    ImageName: "", State: 0, Accessibility: isDorm ? 0 : 1, IsDorm: isDorm, IsRRO: !isDorm,
    IsDeveloperOwned: !isDorm, CloningAllowed: false, SupportsLevelVoting: false,
    SupportsScreens: true, SupportsMobile: true, SupportsQuest2: true, SupportsVRLow: true,
    SupportsWalkVR: true, SupportsTeleportVR: true, SupportsJuniors: true, MinLevel: 0,
    WarningMask: 0, CustomWarning: null, DisableMicAutoMute: false, DisableRoomComments: false,
    EncryptVoiceChat: false, ToxmodEnabled: false, LoadScreenLocked: false,
    MaxPlayerCalculationMode: 0, MaxPlayers: maxPlayers, CreatedAt: new Date().toISOString(),
    PersistenceVersion: 0, UgcVersion: 0, DataBlob: null, DataBlobHash: null, FlaggedAssets: [],
    RankingContext: null, CurrentRoomModerationState: 0, LastRoomModerationState: 0,
    ModeratorMessage: null, Stats: { ...EMPTY_ROOM_STATS },
    SubRooms: [{
      SubRoomId: roomId, RoomId: roomId, Name: "Home", UnitySceneId: scene, IsSandbox: isDorm,
      MaxPlayers: maxPlayers, Accessibility: isDorm ? 0 : 1, CurrentSave: null,
      LastModeratedSaveModerationState: 0,
    }],
    Roles: [{ AccountId: creatorId, Role: 255, InvitedRole: 0, LastChangedByAccountId: creatorId }],
    Tags: [], PromoImages: [], PromoExternalContent: [], LoadScreens: [],
  };
}

function dormRoom(account) {
  const id = dormRoomId(account);
  if (!rooms[id]) {
    rooms[id] = makeRoom({
      roomId: id, name: "DormRoom", description: "Your private room", creatorId: account.accountId,
      scene: SCENES.DormRoom, isDorm: true, maxPlayers: 4,
    });
    saveRooms();
  }
  return rooms[id];
}

// --- Rec Room Originals: built-in rooms from AGRoomRuntimeConfig (tools/extract_rooms.py) ---
// Room ids 1..N follow the config order; sub-room (and public instance) ids are
// 100000 + roomId * 100 + scene index, clear of dorm ids (1000 + accountId).
const BUILTIN_FILE = path.join(DATA_DIR, "builtin_rooms.json");
const BUILTIN = fs.existsSync(BUILTIN_FILE) ? JSON.parse(fs.readFileSync(BUILTIN_FILE, "utf8")) : { Rooms: [] };
// Shown in the room browser, in this order. The rest (setup scenes, single-map copies of
// multi-map rooms, VR variants) can still be reached by name.
const LISTED_RRO = [
  "RecCenter", "Paintball", "LaserTag", "RecRoyaleSquads", "RecRoyaleSolos", "StuntRunner",
  "RecRally", "IsleOfLostSkulls", "CrimsonCauldron", "GoldenTrophy", "TheRiseofJumbotron",
  "Dodgeball", "Paddleball", "Soccer", "DiscGolfLake", "DiscGolfPropulsion", "3DCharades",
  "Bowling", "Crescendo", "Lounge", "PerformanceHall", "Park", "MakerRoom",
];
const displayName = (name) => name.replace(/([a-z])([A-Z0-9])/g, "$1 $2").replace(/([0-9])([A-Z])/g, "$1 $2");
const RRO_ROOMS = BUILTIN.Rooms.map((r, i) => {
  const roomId = i + 1;
  const scenes = r.Scenes.map((s, j) => ({ ...s, SubRoomId: 100000 + roomId * 100 + j }));
  const room = makeRoom({
    roomId, name: r.Name, description: displayName(r.Name), creatorId: 1,
    scene: scenes[0].LocationId, isDorm: false, maxPlayers: Math.max(...scenes.map((s) => s.MaxPlayers)),
  });
  room.SubRooms = scenes.map((s) => ({
    ...room.SubRooms[0], SubRoomId: s.SubRoomId, Name: s.Name, UnitySceneId: s.LocationId, MaxPlayers: s.MaxPlayers,
  }));
  room.Roles = [];
  room.CreatorAccountId = 0;  // Rec Room itself, not a local player
  room.ReleaseStatus = r.ReleaseStatus;
  return room;
});
const RRO_BY_ID = new Map(RRO_ROOMS.map((r) => [r.RoomId, r]));
const listedRooms = () => LISTED_RRO.map((n) => RRO_ROOMS.find((r) => r.Name === n)).filter(Boolean);
const getRoom = (id) => RRO_BY_ID.get(Number(id)) || rooms[id];
const roomByName = (name) =>
  [...RRO_ROOMS, ...Object.values(rooms)].find((r) => r.Name.toLowerCase() === String(name).toLowerCase());
const page = (list, url) => {
  const skip = Number(url.searchParams.get("skip") || 0), take = Number(url.searchParams.get("take") || 100);
  return { Results: list.slice(skip, skip + take), TotalResults: list.length };
};

// Which instance each account is in (for presence heartbeats). Saved to data/presence.json so
// a server restart doesn't put clients "out of sync" and bounce them out of their room.
const PRESENCE_FILE = path.join(DATA_DIR, "presence.json");
const currentInstance = new Map(
  fs.existsSync(PRESENCE_FILE) ? JSON.parse(fs.readFileSync(PRESENCE_FILE, "utf8")).map(([id, inst]) => [Number(id), inst]) : [],
);
const savePresence = () => fs.writeFileSync(PRESENCE_FILE, JSON.stringify([...currentInstance]));
for (const method of ["set", "delete"]) {
  const original = currentInstance[method].bind(currentInstance);
  currentInstance[method] = (...args) => { const r = original(...args); savePresence(); return r; };
}

// When each account last made an authenticated request (heartbeats included). A game that
// crashed or was closed without logging out leaves its presence behind; once it has been
// silent this long it no longer counts toward a room's players or keeps it "in progress".
const PRESENCE_TIMEOUT_MS = 5 * 60 * 1000;
const lastSeen = new Map([...currentInstance.keys()].map((id) => [id, Date.now()]));  // grace period after a restart
const isActive = (accountId) => Date.now() - (lastSeen.get(accountId) || 0) < PRESENCE_TIMEOUT_MS;
const playersIn = (instanceId) =>
  [...currentInstance].filter(([id, inst]) => inst.RoomInstanceId === instanceId && isActive(id)).map(([, inst]) => inst);

// Public instances of a (room, sub-room): everyone going there lands together in the first one
// that isn't full or already running a game/quest; instance n has id subRoomId + n * 1e6.
function publicInstance(room, subRoom, instanceId) {
  if (instanceId === undefined) {
    for (let n = 0; ; n++) {
      const id = subRoom.SubRoomId + n * 1e6;
      const live = playersIn(id);
      if (live.length < subRoom.MaxPlayers && !live.some((i) => i.IsInProgress)) { instanceId = id; break; }
    }
  }
  const players = playersIn(instanceId).length;
  return {
    RoomInstanceId: instanceId, RoomId: room.RoomId, SubRoomId: subRoom.SubRoomId,
    RoomInstanceType: 0, Location: subRoom.UnitySceneId, Name: `^${room.Name}`,
    MaxCapacity: subRoom.MaxPlayers, IsFull: players >= subRoom.MaxPlayers, IsPrivate: false,
    IsInProgress: false, MatchmakingPolicy: 0, EventId: 0, ClubId: 0, RoomCode: null, EncryptVoiceChat: false,
  };
}

// Put the player in an instance and remember the visit; IPLDFHBCIFI {ErrorCode, RoomInstance}.
// ErrorCode 1 = NoSuchGame, 2 = PlayerNotOnline.
function enterInstance(account, instance) {
  currentInstance.set(account.accountId, instance);
  const visits = store("visits");
  const recent = [instance.RoomId, ...(visits.get(account.accountId) || []).filter((id) => id !== instance.RoomId)];
  visits.set(account.accountId, recent.slice(0, 50));
  return { status: 200, json: { ErrorCode: 0, RoomInstance: instance } };
}
const matchmakeError = (code) => ({ status: 200, json: { ErrorCode: code, RoomInstance: null } });

function matchmakeRoom(m, req) {
  const account = authedAccount(req);
  if (!account) return { status: 401, json: null };
  const room = getRoom(m[1]);
  if (!room) return matchmakeError(1);
  if (room.IsDorm) {
    // Someone's dorm: join its owner there if they're in it, else the dorm itself.
    const owner = accounts.find((a) => dormRoomId(a) === room.RoomId);
    return enterInstance(account, owner ? dormInstance(owner) : dormInstance(account));
  }
  const subRoom = room.SubRooms.find((s) => String(s.SubRoomId) === m[2]) || room.SubRooms[0];
  return enterInstance(account, publicInstance(room, subRoom));
}

// One private dorm instance per account. Room ids: dorm of account N = 1000 + N.
const dormRoomId = (account) => 1000 + account.accountId;
const dormInstance = (account) => ({
  RoomInstanceId: dormRoomId(account),
  RoomId: dormRoom(account).RoomId,
  SubRoomId: dormRoomId(account),
  RoomInstanceType: 2,
  Location: SCENES.DormRoom,  // must be a known scene location id (UnitySceneId)
  Name: `@${account.username}'s Dorm`,
  MaxCapacity: 4,
  IsFull: false,
  IsPrivate: true,
  IsInProgress: false,
  MatchmakingPolicy: 0,
  EventId: 0,      // non-nullable numbers in the client DTO
  ClubId: 0,
  RoomCode: null,
  EncryptVoiceChat: false,
});

// Settings every account starts with (overridden by anything the client saves).
const DEFAULT_SETTINGS = {
  HAS_COMPLETED_ORIENTATION: "True",
};

const CONFIG_SETTINGS = {
  ShareBaseUrl: BASE + "/www/",
  ServerMaintenance: { StartsInMinutes: 0 },
  LevelProgressionMaps: Array.from({ length: 50 }, (_, i) => ({
    Level: i + 1, RequiredXp: i === 0 ? 0 : 200 * i * i, GiftDropId: null,
  })),
  DailyObjectives: [],
  AutoMicMutingConfig: {
    MicVolumeSampleInterval: 0.25, MicVolumeSampleRollingWindowLength: 10, MicSpamVolumeThreshold: 0.9,
    MicSpamSamplePercentageForWarning: 0.9, MicSpamSamplePercentageForWarningToEnd: 0.5,
    MicSpamSamplePercentageForForceMute: 0.95, MicSpamSamplePercentageForForceMuteToEnd: 0.5,
    MicSpamWarningStateVolumeMultiplier: 0.5,
  },
  RoomCurrencyConfig: { MinPlayerLevelForGifting: 1 },
  RoomKeyConfig: { MaxKeysPerRoom: 100 },
  StorefrontConfig: { AwardCurrencyCooldownSeconds: 60 },
};

// Server-controlled game switches. AntiHile.DC = disconnect when the file
// integrity check fails; the check needs a signature from Rec Room's CDN, so
// offline it always fails and the game quits unless this is off.
const GAMECONFIGS_FILE = path.join(DATA_DIR, "gameconfigs.json");
if (!fs.existsSync(GAMECONFIGS_FILE)) {
  fs.writeFileSync(GAMECONFIGS_FILE, JSON.stringify({ "AntiHile.DC": false }, null, 2));
}
const gameConfigs = () => JSON.parse(fs.readFileSync(GAMECONFIGS_FILE, "utf8"));

// Result wrapper used by some endpoints (COLCODGGIFK<T>).
const ok = (Value) => ({ Success: true, Error: null, Value });

const idsParam = (url) => url.searchParams.getAll("id").map(Number);
const jsonBody = (body) => {
  try { const v = JSON.parse(body); return v && typeof v === "object" ? v : {}; } catch { return {}; }
};

const playerStatus = (a) => a && ({
  playerId: a.accountId,
  isOnline: true,
  statusVisibility: 0,
  deviceClass: 0,
  vrMovementMode: 0,
  roomInstance: currentInstance.get(a.accountId) || null,
  appVersion: "20231207",
});

// --- Tokens: HS256 JWTs signed with a per-install secret in data/secret.key ---
const SECRET_FILE = path.join(DATA_DIR, "secret.key");
if (!fs.existsSync(SECRET_FILE)) fs.writeFileSync(SECRET_FILE, crypto.randomBytes(32).toString("hex"));
const SECRET = fs.readFileSync(SECRET_FILE, "utf8");
const TOKEN_LIFETIME = 24 * 60 * 60;

const b64url = (buf) => Buffer.from(buf).toString("base64url");
function signToken(claims) {
  const head = b64url(JSON.stringify({ alg: "HS256", typ: "JWT" }));
  const payload = b64url(JSON.stringify(claims));
  const sig = b64url(crypto.createHmac("sha256", SECRET).update(`${head}.${payload}`).digest());
  return `${head}.${payload}.${sig}`;
}
function verifyToken(token) {
  const [head, payload, sig] = String(token || "").split(".");
  if (!sig) return null;
  const expected = b64url(crypto.createHmac("sha256", SECRET).update(`${head}.${payload}`).digest());
  if (sig !== expected) return null;
  const claims = JSON.parse(Buffer.from(payload, "base64url").toString("utf8"));
  return claims.exp > Date.now() / 1000 ? claims : null;
}
function authedAccount(req) {
  const claims = verifyToken((req.headers.authorization || "").replace(/^Bearer\s+/i, ""));
  const account = claims && accounts.find((a) => a.accountId === Number(claims.sub));
  if (account) lastSeen.set(account.accountId, Date.now());
  return account;
}
function issueTokens(account) {
  const now = Math.floor(Date.now() / 1000);
  const common = { iss: BASE + "/auth", client_id: "recroom", sub: String(account.accountId), iat: now, auth_time: now };
  return {
    access_token: signToken({ ...common, exp: now + TOKEN_LIFETIME, role: "gameClient", scope: ["recroom", "offline_access"] }),
    refresh_token: signToken({ ...common, exp: now + 30 * TOKEN_LIFETIME, typ: "refresh" }),
    expires_in: TOKEN_LIFETIME,
    token_type: "Bearer",
    // 32-byte request-signing key; the client sends X-RNSIG headers with it.
    // Without it the client's header hook throws and no Authorization is sent.
    key: crypto.randomBytes(32).toString("base64"),
    scope: "recroom offline_access",
  };
}

const accountProfile = (a) => ({
  accountId: a.accountId,
  username: a.username,
  displayName: a.displayName,
  profileImage: "",
  bannerImage: "",
  isJunior: false,
  platforms: 1,
  personalPronouns: 0,
  identityFlags: 0,
  createdAt: a.createdAt,
  isMetaPlatformBlocked: false,
});

const cachedLogin = (a) => ({
  Platform: a.platform,
  PlatformId: a.platformId,
  AccountId: a.accountId,
  LastLoginTime: new Date().toISOString(),
  RequirePassword: false,
});

const server = https.createServer(TLS, (req, res) => {
  const chunks = [];
  req.on("data", (c) => chunks.push(c));
  req.on("end", () => {
    const body = Buffer.concat(chunks).toString("utf8");
    let result = { status: 404, json: null };
    // A handler that throws answers 500 instead of taking the server down for everyone.
    try {
      const url = new URL(req.url, BASE);
      const key = `${req.method} ${url.pathname}`;
      if (routes[key]) {
        result = routes[key](req, body, url);
      } else {
        for (const [method, re, handler] of patternRoutes) {
          const m = (method instanceof RegExp ? method.test(req.method) : req.method === method) && url.pathname.match(re);
          if (m) { result = handler(m, req, body, url); break; }
        }
      }
    } catch (e) {
      console.log(`  error handling ${req.method} ${req.url}: ${e.stack}`);
      result = { status: 500, json: null };
    }

    // Tokens are left out of the logs so a log file can be shared safely.
    const loggedBody = body.replace(/(refresh_token=)[^&]+/, "$1(redacted)");
    logRequest({
      t: new Date().toISOString(),
      method: req.method,
      url: req.url,
      headers: { ...req.headers, authorization: req.headers.authorization && "(redacted)" },
      body: loggedBody,
      status: result.status,
    });
    console.log(`${result.status} ${req.method} ${req.url}${body ? "  body=" + loggedBody.slice(0, 200) : ""}`);

    if (result.raw !== undefined) {
      res.writeHead(result.status, { "Content-Type": "text/plain" });
      res.end(result.raw);
      return;
    }
    res.writeHead(result.status, { "Content-Type": "application/json" });
    res.end(result.json === null || result.json === undefined ? "" : JSON.stringify(result.json));
  });
});

// --- Minimal WebSocket server (RFC 6455) for the SignalR notifications hub ---
const RS = "\x1e"; // SignalR JSON record separator
const hubClients = new Set();

function wsSend(socket, text) {
  const payload = Buffer.from(text, "utf8");
  const len = payload.length;
  const head = len < 126 ? Buffer.from([0x81, len])
    : len < 65536 ? Buffer.from([0x81, 126, len >> 8, len & 255])
    : Buffer.concat([Buffer.from([0x81, 127]), (() => { const b = Buffer.alloc(8); b.writeBigUInt64BE(BigInt(len)); return b; })()]);
  socket.write(Buffer.concat([head, payload]));
}

// Parse client frames (always masked). Returns [messages, remainingBuffer].
function wsParse(buf) {
  const out = [];
  while (buf.length >= 2) {
    const opcode = buf[0] & 0x0f;
    let len = buf[1] & 0x7f, off = 2;
    if (len === 126) { if (buf.length < 4) break; len = buf.readUInt16BE(2); off = 4; }
    else if (len === 127) { if (buf.length < 10) break; len = Number(buf.readBigUInt64BE(2)); off = 10; }
    if (buf.length < off + 4 + len) break;
    const mask = buf.subarray(off, off + 4);
    const data = Buffer.from(buf.subarray(off + 4, off + 4 + len));
    for (let i = 0; i < data.length; i++) data[i] ^= mask[i & 3];
    out.push({ opcode, data });
    buf = buf.subarray(off + 4 + len);
  }
  return [out, buf];
}

server.on("upgrade", (req, socket) => {
  let pathname = null;
  try { pathname = new URL(req.url, BASE).pathname; } catch {}
  console.log(`WS ${pathname}`);
  if (pathname !== "/notifications/hub/v1") return socket.destroy();
  const accept = crypto.createHash("sha1")
    .update(req.headers["sec-websocket-key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").digest("base64");
  socket.write(`HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${accept}\r\n\r\n`);

  const client = { socket, accountId: null };
  hubClients.add(client);
  let pending = Buffer.alloc(0);
  const ping = setInterval(() => wsSend(socket, JSON.stringify({ type: 6 }) + RS), 15000);
  const close = () => { clearInterval(ping); hubClients.delete(client); };
  socket.on("close", close);
  socket.on("error", close);

  socket.on("data", (chunk) => {
    let frames;
    [frames, pending] = wsParse(Buffer.concat([pending, chunk]));
    for (const { opcode, data } of frames) {
      if (opcode === 0x8) { socket.end(); return; }          // close
      if (opcode === 0x9) continue;                         // ping (client pings are rare)
      for (const record of data.toString("utf8").split(RS).filter(Boolean)) {
        console.log(`  hub <- ${record.slice(0, 200)}`);
        let msg;
        try { msg = JSON.parse(record); } catch { console.log("  hub: ignoring malformed message"); continue; }
        if (!msg || typeof msg !== "object") continue;
        if (msg.protocol) wsSend(socket, "{}" + RS);         // handshake ack
        else if (msg.type === 1 && msg.invocationId) {       // invocation needing a completion
          wsSend(socket, JSON.stringify({ type: 3, invocationId: msg.invocationId, result: null }) + RS);
        }
      }
    }
  });
});

server.on("tlsClientError", (err) => console.log(`TLS error: ${err.message}`));
server.listen(PORT, () => console.log(`listening on ${BASE}, logging to ${LOG}`));
