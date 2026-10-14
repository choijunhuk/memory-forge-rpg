-- Behavioural checks for server/schema.sql. Run by tools/check_schema.sh. Any failed assertion aborts with an error.
\set ON_ERROR_STOP on
CREATE OR REPLACE FUNCTION pg_temp.expect_error(stmt text, state text) RETURNS void LANGUAGE plpgsql AS $$
BEGIN
  BEGIN
    EXECUTE stmt;
  EXCEPTION WHEN OTHERS THEN
    IF SQLSTATE <> state THEN RAISE EXCEPTION 'expected % but got % (%): %', state, SQLSTATE, SQLERRM, stmt; END IF;
    RETURN;
  END;
  RAISE EXCEPTION 'expected error % but statement succeeded: %', state, stmt;
END $$;

INSERT INTO content_version VALUES ('0.7.0', 'sha256:test', now(), 'check');
INSERT INTO account (id) VALUES ('00000000-0000-0000-0000-000000000001');
INSERT INTO account_progress (account_id) VALUES ('00000000-0000-0000-0000-000000000001');
INSERT INTO wallet (account_id, currency, balance) VALUES
  ('00000000-0000-0000-0000-000000000001','crystal_free',1600),('00000000-0000-0000-0000-000000000001','crystal_paid',0),
  ('00000000-0000-0000-0000-000000000001','ticket_limited',5);
INSERT INTO banner (id, kind, featured_ssr, featured_sr, ticket, rules_version) VALUES
  ('limited_lumi','character','lumi','{nia,rien}','ticket_limited','0.7.0'),('standard','standard',NULL,'{}','ticket_standard','0.7.0');
INSERT INTO gacha_pity (account_id, pool) VALUES ('00000000-0000-0000-0000-000000000001','character');

-- balances never go negative
SELECT pg_temp.expect_error($$UPDATE wallet SET balance = balance - 1601 WHERE account_id='00000000-0000-0000-0000-000000000001' AND currency='crystal_free'$$, '23514');
-- unknown currency rejected
SELECT pg_temp.expect_error($$INSERT INTO wallet VALUES ('00000000-0000-0000-0000-000000000001','gold',1,0)$$, '23514');
-- character/weapon banners need a featured SSR
SELECT pg_temp.expect_error($$INSERT INTO banner (id,kind,ticket,rules_version) VALUES ('bad','character','ticket_limited','0.7.0')$$, '23514');

-- a 10-pull: 5 tickets + 5×160 crystals, ledger + pulls + pity + receipt in ONE transaction
BEGIN;
-- lock order (always): wallet rows → gacha_pity → (inserts). Same order everywhere prevents deadlocks.
DO $$ BEGIN
  PERFORM 1 FROM wallet WHERE account_id='00000000-0000-0000-0000-000000000001' AND currency IN ('crystal_free','ticket_limited') ORDER BY currency FOR UPDATE;
  PERFORM 1 FROM gacha_pity WHERE account_id='00000000-0000-0000-0000-000000000001' AND pool='character' FOR UPDATE;
END $$;
UPDATE wallet SET balance = balance - 5, version = version + 1 WHERE account_id='00000000-0000-0000-0000-000000000001' AND currency='ticket_limited';
UPDATE wallet SET balance = balance - 800, version = version + 1 WHERE account_id='00000000-0000-0000-0000-000000000001' AND currency='crystal_free';
INSERT INTO ledger (account_id, request_id, asset, delta, balance_after, reason, policy_version) VALUES
  ('00000000-0000-0000-0000-000000000001','req-1','ticket_limited',-5,0,'gacha_pull','0.7.0'),
  ('00000000-0000-0000-0000-000000000001','req-1','crystal_free',-800,800,'gacha_pull','0.7.0');
INSERT INTO gacha_pull (account_id, request_id, banner_id, idx, rarity, item_kind, item_id, pity_at, roll_bps, rules_version)
  SELECT '00000000-0000-0000-0000-000000000001','req-1','limited_lumi', i, CASE WHEN i = 9 THEN 'SR' ELSE 'R' END, CASE WHEN i = 9 THEN 'char' ELSE 'item' END,
         CASE WHEN i = 9 THEN 'rien' ELSE 'ink_s' END, i + 1, 5000, '0.7.0' FROM generate_series(0, 9) AS i;
UPDATE gacha_pity SET n = 10, sr_n = 0, version = version + 1 WHERE account_id='00000000-0000-0000-0000-000000000001' AND pool='character';
INSERT INTO command_receipt VALUES ('00000000-0000-0000-0000-000000000001','req-1','hash-A','{"ok":true}'::jsonb, now());
COMMIT;
DO $$ BEGIN
  IF (SELECT balance FROM wallet WHERE account_id='00000000-0000-0000-0000-000000000001' AND currency='crystal_free') <> 800 THEN RAISE EXCEPTION 'crystals'; END IF;
  IF (SELECT count(*) FROM gacha_pull WHERE request_id='req-1') <> 10 THEN RAISE EXCEPTION 'pull rows'; END IF;
END $$;
-- replaying the same request id cannot double-charge: the receipt / ledger / pull keys collide
SELECT pg_temp.expect_error($$INSERT INTO command_receipt VALUES ('00000000-0000-0000-0000-000000000001','req-1','hash-B','{}'::jsonb, now())$$, '23505');
SELECT pg_temp.expect_error($$INSERT INTO ledger (account_id, request_id, asset, delta, balance_after, reason, policy_version) VALUES ('00000000-0000-0000-0000-000000000001','req-1','crystal_free',-800,0,'gacha_pull','0.7.0')$$, '23505');
SELECT pg_temp.expect_error($$INSERT INTO gacha_pull (account_id, request_id, banner_id, idx, rarity, item_kind, item_id, pity_at, roll_bps, rules_version) VALUES ('00000000-0000-0000-0000-000000000001','req-1','limited_lumi',0,'R','item','x',1,1,'0.7.0')$$, '23505');
-- pity bounds per pool
SELECT pg_temp.expect_error($$UPDATE gacha_pity SET n = 80 WHERE pool='character'$$, '23514');
INSERT INTO gacha_pity (account_id, pool) VALUES ('00000000-0000-0000-0000-000000000001','weapon');
SELECT pg_temp.expect_error($$UPDATE gacha_pity SET n = 70 WHERE pool='weapon'$$, '23514');

-- collection rules
INSERT INTO character_owned (account_id, char_id) VALUES ('00000000-0000-0000-0000-000000000001','sera'),('00000000-0000-0000-0000-000000000001','harin');
SELECT pg_temp.expect_error($$UPDATE character_owned SET level = 21 WHERE char_id='sera'$$, '23514');      -- needs ascension 1
UPDATE character_owned SET ascension = 1, level = 30 WHERE char_id='sera';
INSERT INTO weapon_instance (instance_id, account_id, weapon_id, rarity, equipped_by) VALUES
  ('10000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000001','w_sera','SSR','sera');
SELECT pg_temp.expect_error($$INSERT INTO weapon_instance (instance_id, account_id, weapon_id, rarity) VALUES ('10000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000001','w_sera','SSR')$$, '23505');
INSERT INTO weapon_instance (instance_id, account_id, weapon_id, rarity) VALUES
  ('10000000-0000-0000-0000-000000000003','00000000-0000-0000-0000-000000000001','w_basic_ranged','R'),
  ('10000000-0000-0000-0000-000000000004','00000000-0000-0000-0000-000000000001','w_basic_ranged','R');   -- crafted R weapons may repeat
SELECT pg_temp.expect_error($$UPDATE weapon_instance SET equipped_by='sera' WHERE instance_id='10000000-0000-0000-0000-000000000003'$$, '23505');
SELECT pg_temp.expect_error($$UPDATE weapon_instance SET equipped_by='lumi' WHERE instance_id='10000000-0000-0000-0000-000000000003'$$, '23503'); -- not owned
SELECT pg_temp.expect_error($$INSERT INTO memory_deck (account_id, char_id, specs, capacity_used) VALUES ('00000000-0000-0000-0000-000000000001','sera','[]',10)$$, '23514');
INSERT INTO memory_deck (account_id, char_id, specs, capacity_used) VALUES ('00000000-0000-0000-0000-000000000001','sera','[{"id":"sera_round","plus":true}]',1);

-- weekly boss: at most 3 claims per week
INSERT INTO weekly_claim_counter VALUES ('00000000-0000-0000-0000-000000000001','2026-10-12',3);
SELECT pg_temp.expect_error($$UPDATE weekly_claim_counter SET claimed = claimed + 1$$, '23514');
-- one active run per account
INSERT INTO run (run_id, account_id, mode, stage_level, party, rules_version, private_state) VALUES
  ('20000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000001','story',8,'[]','0.7.0','{}');
SELECT pg_temp.expect_error($$INSERT INTO run (run_id, account_id, mode, stage_level, party, rules_version, private_state) VALUES ('20000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000001','story',8,'[]','0.7.0','{}')$$, '23505');
-- pass: plus requires premium; monthly card stacking cap
SELECT pg_temp.expect_error($$INSERT INTO pass_state (account_id, season_id, plus) VALUES ('00000000-0000-0000-0000-000000000001','S1',true)$$, '23514');
SELECT pg_temp.expect_error($$INSERT INTO monthly_card VALUES ('00000000-0000-0000-0000-000000000001', current_date + 200, NULL)$$, '23514');
SELECT 'schema checks passed' AS result;
