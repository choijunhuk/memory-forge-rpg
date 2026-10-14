-- Memory Forge v0.7 — account server schema (PostgreSQL 16).
-- Executed by tools/check_schema.sh against a throwaway cluster together with server/tests/schema_checks.sql.
-- One independent database per environment (dev / stage / prod). Business rules that need several rows
-- (pity, wallet, ledger, receipt) are enforced by the application inside ONE transaction; the constraints
-- below are the last line of defence, not the whole rule set.

BEGIN;

CREATE TABLE content_version (
  rules_version   text PRIMARY KEY,            -- e.g. '0.7.0'
  content_hash    text NOT NULL,               -- sha256 of data/game_data.json
  activated_at    timestamptz,
  note            text
);

CREATE TABLE account (
  id              uuid PRIMARY KEY,
  created_at      timestamptz NOT NULL DEFAULT now(),
  region          text NOT NULL DEFAULT 'KR',
  locale          text NOT NULL DEFAULT 'ko-KR',
  age_gate        text NOT NULL DEFAULT 'unknown' CHECK (age_gate IN ('unknown','under15','15to18','adult')), -- self-declared band; used for purchase limits, never a birth date
  status          text NOT NULL DEFAULT 'active' CHECK (status IN ('active','suspended','deleting','deleted')),
  deletion_requested_at timestamptz
);

CREATE TABLE auth_identity (
  provider        text NOT NULL CHECK (provider IN ('guest','google_play_games','apple','dev')),
  subject         text NOT NULL,               -- provider user id (hashed for guest devices)
  account_id      uuid NOT NULL REFERENCES account(id),
  created_at      timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (provider, subject)
);

CREATE TABLE account_progress (
  account_id      uuid PRIMARY KEY REFERENCES account(id),
  level           int NOT NULL DEFAULT 1 CHECK (level BETWEEN 1 AND 60),
  xp              bigint NOT NULL DEFAULT 0 CHECK (xp >= 0),
  stamina         int NOT NULL DEFAULT 240 CHECK (stamina BETWEEN 0 AND 999),
  stamina_at      timestamptz NOT NULL DEFAULT now(),   -- regen is computed lazily from this timestamp (1 per 6 min up to 240)
  story_cleared   int NOT NULL DEFAULT -1,               -- highest cleared chapter (−1 none, 0 prologue … 6)
  tutorial        jsonb NOT NULL DEFAULT '{}'::jsonb,
  version         bigint NOT NULL DEFAULT 0
);

-- currencies (spendable balances). Paid and free crystals are separate for refunds / withdrawal of subscription.
CREATE TABLE wallet (
  account_id      uuid NOT NULL REFERENCES account(id),
  currency        text NOT NULL CHECK (currency IN ('crystal_paid','crystal_free','credit','ticket_limited','ticket_weapon','ticket_standard',
                                                     'shard','afterglow','boss_token','event_token')),
  balance         bigint NOT NULL DEFAULT 0 CHECK (balance >= 0),
  version         bigint NOT NULL DEFAULT 0,
  PRIMARY KEY (account_id, currency)
);

-- materials and items (ink, oil, seal, alloy, book, polish, core, gifts …)
CREATE TABLE inventory (
  account_id      uuid NOT NULL REFERENCES account(id),
  item_id         text NOT NULL,
  qty             bigint NOT NULL DEFAULT 0 CHECK (qty >= 0),
  PRIMARY KEY (account_id, item_id)
);

-- append-only ledger for every balance change (currency or item)
CREATE TABLE ledger (
  event_id        bigserial PRIMARY KEY,
  account_id      uuid NOT NULL REFERENCES account(id),
  request_id      text NOT NULL,
  asset           text NOT NULL,              -- currency or item id
  delta           bigint NOT NULL CHECK (delta <> 0),
  balance_after   bigint NOT NULL CHECK (balance_after >= 0),
  reason          text NOT NULL,              -- gacha_pull, mission_claim, purchase, level_up …
  policy_version  text NOT NULL,              -- rules_version that priced this change
  created_at      timestamptz NOT NULL DEFAULT now(),
  UNIQUE (account_id, request_id, asset, reason)
);
CREATE INDEX ledger_account_time ON ledger (account_id, created_at DESC);

-- idempotency: one stored response per (account, request id). Same id + different payload hash = conflict.
CREATE TABLE command_receipt (
  account_id      uuid NOT NULL REFERENCES account(id),
  request_id      text NOT NULL,
  request_hash    text NOT NULL,
  response        jsonb NOT NULL,
  created_at      timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (account_id, request_id)
);

-- ───────── collection ─────────
CREATE TABLE character_owned (
  account_id      uuid NOT NULL REFERENCES account(id),
  char_id         text NOT NULL,
  copies          int NOT NULL DEFAULT 1 CHECK (copies >= 1),     -- resonance = least(6, copies − 1)
  level           int NOT NULL DEFAULT 1 CHECK (level BETWEEN 1 AND 60),
  xp              bigint NOT NULL DEFAULT 0 CHECK (xp >= 0),
  ascension       int NOT NULL DEFAULT 0 CHECK (ascension BETWEEN 0 AND 4),
  skill_card      int NOT NULL DEFAULT 1 CHECK (skill_card BETWEEN 1 AND 10),
  skill_ult       int NOT NULL DEFAULT 1 CHECK (skill_ult BETWEEN 1 AND 10),
  armor_enh       int CHECK (armor_enh BETWEEN 0 AND 15),         -- NULL = not crafted
  accessory_enh   int CHECK (accessory_enh BETWEEN 0 AND 15),
  affinity_level  int NOT NULL DEFAULT 1 CHECK (affinity_level BETWEEN 1 AND 10),
  affinity_xp     int NOT NULL DEFAULT 0 CHECK (affinity_xp >= 0),
  obtained_at     timestamptz NOT NULL DEFAULT now(),
  version         bigint NOT NULL DEFAULT 0,
  PRIMARY KEY (account_id, char_id),
  CHECK (level <= CASE ascension WHEN 0 THEN 20 WHEN 1 THEN 30 WHEN 2 THEN 40 WHEN 3 THEN 50 ELSE 60 END)
);

CREATE TABLE weapon_instance (
  instance_id     uuid PRIMARY KEY,
  account_id      uuid NOT NULL REFERENCES account(id),
  weapon_id       text NOT NULL,
  rarity          text NOT NULL CHECK (rarity IN ('SSR','SR','R')),
  copies          int NOT NULL DEFAULT 1 CHECK (copies >= 1),      -- SSR/SR: refine = least(5, copies); R: refine via refine cores
  refine          int NOT NULL DEFAULT 1 CHECK (refine BETWEEN 1 AND 5),
  level           int NOT NULL DEFAULT 1 CHECK (level BETWEEN 1 AND 60),
  xp              bigint NOT NULL DEFAULT 0 CHECK (xp >= 0),
  ascension       int NOT NULL DEFAULT 0 CHECK (ascension BETWEEN 0 AND 4),
  equipped_by     text,                                             -- char_id or NULL
  version         bigint NOT NULL DEFAULT 0,
  CHECK (level <= CASE ascension WHEN 0 THEN 20 WHEN 1 THEN 30 WHEN 2 THEN 40 WHEN 3 THEN 50 ELSE 60 END),
  FOREIGN KEY (account_id, equipped_by) REFERENCES character_owned(account_id, char_id)
);
-- gacha weapons stack into one instance per weapon id; crafted R weapons may have several instances
CREATE UNIQUE INDEX weapon_gacha_single ON weapon_instance (account_id, weapon_id) WHERE rarity IN ('SSR','SR');
-- a character holds at most one weapon
CREATE UNIQUE INDEX weapon_one_per_char ON weapon_instance (account_id, equipped_by) WHERE equipped_by IS NOT NULL;

-- saved memory decks (카제나식 세이브): one current deck per character, history kept for support/rollback
CREATE TABLE memory_deck (
  account_id      uuid NOT NULL REFERENCES account(id),
  char_id         text NOT NULL,
  version         int NOT NULL DEFAULT 1,
  specs           jsonb NOT NULL,             -- [{id, plus?, mods?{c,e}, from?, assigned?}]
  capacity_used   int NOT NULL CHECK (capacity_used BETWEEN 0 AND 9),
  source_run      uuid,
  updated_at      timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (account_id, char_id),
  FOREIGN KEY (account_id, char_id) REFERENCES character_owned(account_id, char_id)
);
CREATE TABLE memory_deck_history (
  account_id      uuid NOT NULL,
  char_id         text NOT NULL,
  version         int NOT NULL,
  specs           jsonb NOT NULL,
  capacity_used   int NOT NULL,
  source_run      uuid,
  saved_at        timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (account_id, char_id, version)
);

-- ───────── recruitment ─────────
CREATE TABLE banner (
  id              text PRIMARY KEY,
  kind            text NOT NULL CHECK (kind IN ('character','weapon','standard','beginner')),
  featured_ssr    text,
  featured_sr     text[] NOT NULL DEFAULT '{}',
  ticket          text NOT NULL CHECK (ticket IN ('ticket_limited','ticket_weapon','ticket_standard')),
  starts_at       timestamptz,
  ends_at         timestamptz,
  rules_version   text NOT NULL REFERENCES content_version(rules_version),
  CHECK (kind NOT IN ('character','weapon') OR featured_ssr IS NOT NULL),
  CHECK (ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at)
);

-- pity is per POOL (character banners share one, weapon banners share one) and carries over between banners
CREATE TABLE gacha_pity (
  account_id      uuid NOT NULL REFERENCES account(id),
  pool            text NOT NULL CHECK (pool IN ('character','weapon','standard','beginner')),
  n               int NOT NULL DEFAULT 0 CHECK (n >= 0 AND n < 80),     -- pulls since SSR (hard pity 80 / 70 / 80 / 50)
  sr_n            int NOT NULL DEFAULT 0 CHECK (sr_n BETWEEN 0 AND 9),
  guar            boolean NOT NULL DEFAULT false,
  sr_guar         boolean NOT NULL DEFAULT false,
  total           int NOT NULL DEFAULT 0 CHECK (total >= 0),
  done            boolean NOT NULL DEFAULT false,
  got_ssr         boolean NOT NULL DEFAULT false,
  selector_used   boolean NOT NULL DEFAULT false,
  version         bigint NOT NULL DEFAULT 0,
  PRIMARY KEY (account_id, pool),
  CHECK (pool <> 'weapon' OR n < 70),
  CHECK (pool <> 'beginner' OR (n < 50 AND total <= 50))
);

-- every single pull is stored (probability audits, disputes, history screen). roll_bps comes from a CSPRNG.
CREATE TABLE gacha_pull (
  pull_id         bigserial PRIMARY KEY,
  account_id      uuid NOT NULL REFERENCES account(id),
  request_id      text NOT NULL,
  banner_id       text NOT NULL REFERENCES banner(id),
  idx             int NOT NULL CHECK (idx BETWEEN 0 AND 9),
  rarity          text NOT NULL CHECK (rarity IN ('SSR','SR','R')),
  item_kind       text NOT NULL CHECK (item_kind IN ('char','weapon','item')),
  item_id         text NOT NULL,
  featured        boolean NOT NULL DEFAULT false,
  lost_5050       boolean NOT NULL DEFAULT false,
  pity_at         int NOT NULL,
  roll_bps        int NOT NULL CHECK (roll_bps BETWEEN 0 AND 9999),
  rules_version   text NOT NULL,
  created_at      timestamptz NOT NULL DEFAULT now(),
  UNIQUE (account_id, request_id, idx)
);
CREATE INDEX gacha_pull_history ON gacha_pull (account_id, banner_id, pull_id DESC);

-- ───────── runs, battles, rewards ─────────
CREATE TABLE run (
  run_id          uuid PRIMARY KEY,
  account_id      uuid NOT NULL REFERENCES account(id),
  mode            text NOT NULL CHECK (mode IN ('story','event','free')),
  chapter         int,
  stage_level     int NOT NULL CHECK (stage_level BETWEEN 1 AND 60),
  party           jsonb NOT NULL,             -- derived stats are recomputed from collection rows at creation, never taken from the client
  state_version   bigint NOT NULL DEFAULT 0,
  status          text NOT NULL DEFAULT 'active' CHECK (status IN ('active','victory','defeat','returned','abandoned','saved')),
  rules_version   text NOT NULL REFERENCES content_version(rules_version),
  private_state   jsonb NOT NULL,             -- full engine state incl. RNG + draw order. NEVER returned to clients.
  created_at      timestamptz NOT NULL DEFAULT now(),
  finished_at     timestamptz
);
CREATE UNIQUE INDEX run_one_active ON run (account_id) WHERE status = 'active';

CREATE TABLE battle (
  battle_id       uuid PRIMARY KEY,
  account_id      uuid NOT NULL REFERENCES account(id),
  kind            text NOT NULL CHECK (kind IN ('weekly_boss','rift','dungeon','event')),
  content_id      text NOT NULL,              -- boss id / rift floor / dungeon id
  difficulty      int NOT NULL DEFAULT 1,
  state_version   bigint NOT NULL DEFAULT 0,
  status          text NOT NULL DEFAULT 'active' CHECK (status IN ('active','victory','defeat','abandoned')),
  rules_version   text NOT NULL REFERENCES content_version(rules_version),
  private_state   jsonb NOT NULL,
  created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE weekly_claim_counter (
  account_id      uuid NOT NULL REFERENCES account(id),
  week_key        date NOT NULL,              -- Monday 05:00 KST week
  claimed         int NOT NULL CHECK (claimed BETWEEN 0 AND 3),
  PRIMARY KEY (account_id, week_key)
);

CREATE TABLE rift_progress (
  account_id      uuid NOT NULL REFERENCES account(id),
  cycle_key       date NOT NULL,              -- 14-day cycle start
  floor           int NOT NULL CHECK (floor BETWEEN 1 AND 6),
  stars           int NOT NULL CHECK (stars BETWEEN 0 AND 3),
  rewarded        boolean NOT NULL DEFAULT false,
  PRIMARY KEY (account_id, cycle_key, floor)
);

CREATE TABLE dungeon_clear (
  account_id      uuid NOT NULL REFERENCES account(id),
  dungeon_id      text NOT NULL,
  tier            int NOT NULL,
  best_stars      int NOT NULL CHECK (best_stars BETWEEN 0 AND 3),   -- 3 stars unlocks sweep
  PRIMARY KEY (account_id, dungeon_id, tier)
);

CREATE TABLE mission_progress (
  account_id      uuid NOT NULL REFERENCES account(id),
  mission_id      text NOT NULL,
  period_key      text NOT NULL,              -- 'D:2026-10-14' / 'W:2026-10-12' / 'S:1'
  progress        int NOT NULL DEFAULT 0 CHECK (progress >= 0),
  claimed         boolean NOT NULL DEFAULT false,
  PRIMARY KEY (account_id, mission_id, period_key)
);

CREATE TABLE pass_state (
  account_id      uuid NOT NULL REFERENCES account(id),
  season_id       text NOT NULL,
  xp              int NOT NULL DEFAULT 0 CHECK (xp >= 0),
  premium         boolean NOT NULL DEFAULT false,
  plus            boolean NOT NULL DEFAULT false,
  claimed_free    int[] NOT NULL DEFAULT '{}',
  claimed_premium int[] NOT NULL DEFAULT '{}',
  PRIMARY KEY (account_id, season_id),
  CHECK (NOT plus OR premium)
);

CREATE TABLE monthly_card (
  account_id      uuid PRIMARY KEY REFERENCES account(id),
  expires_on      date NOT NULL,              -- last day with a daily claim
  last_claim_on   date,
  CHECK (expires_on <= current_date + 180)    -- stacking cap (180 days)
);

-- Google Play purchases. token stored as a keyed digest; the raw token is encrypted elsewhere.
CREATE TABLE purchase (
  token_digest    text PRIMARY KEY,
  account_id      uuid NOT NULL REFERENCES account(id),
  product_id      text NOT NULL,
  order_id        text,
  price_micros    bigint,
  currency_code   text,
  purchase_state  text NOT NULL CHECK (purchase_state IN ('pending','purchased','cancelled','refunded')),
  validation_mode text NOT NULL CHECK (validation_mode IN ('play','mock')),
  grant_state     text NOT NULL CHECK (grant_state IN ('pending','granted','revoked')),
  acknowledged    boolean NOT NULL DEFAULT false,
  received_at     timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE mail (
  mail_id         uuid PRIMARY KEY,
  account_id      uuid NOT NULL REFERENCES account(id),
  title           text NOT NULL,
  body            text NOT NULL,
  attachments     jsonb NOT NULL DEFAULT '[]'::jsonb,
  expires_at      timestamptz NOT NULL,
  claimed_at      timestamptz,
  created_at      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX mail_inbox ON mail (account_id, claimed_at, expires_at);

COMMIT;
