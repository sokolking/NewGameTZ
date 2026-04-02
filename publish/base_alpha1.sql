-- -------------------------------------------------------------
-- TablePlus 6.8.0(654)
--
-- https://tableplus.com/
--
-- Database: battle_server
-- Generation Time: 2026-04-01 09:31:55.9500
-- -------------------------------------------------------------


DROP TABLE IF EXISTS "public"."battle_turn_links";
-- Table Definition
CREATE TABLE "public"."battle_turn_links" (
    "battle_id" text NOT NULL,
    "turn_index" int4 NOT NULL,
    "turn_id" text NOT NULL,
    PRIMARY KEY ("battle_id","turn_index")
);

DROP TABLE IF EXISTS "public"."battle_turns";
-- Table Definition
CREATE TABLE "public"."battle_turns" (
    "turn_id" text NOT NULL,
    "battle_id" text NOT NULL,
    "turn_result_json" jsonb NOT NULL,
    "created_utc" timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY ("turn_id")
);

DROP TABLE IF EXISTS "public"."body_parts";
-- Table Definition
CREATE TABLE "public"."body_parts" (
    "id" int2 NOT NULL CHECK (id > 0),
    "code" text NOT NULL,
    PRIMARY KEY ("id")
);

DROP TABLE IF EXISTS "public"."battle_zone_shrink";
-- Table Definition
CREATE TABLE "public"."battle_zone_shrink" (
    "id" int4 NOT NULL,
    "shrink_start_round" int4 NOT NULL DEFAULT 10,
    "horizontal_shrink_interval" int4 NOT NULL DEFAULT 2,
    "horizontal_shrink_amount" int4 NOT NULL DEFAULT 2,
    "vertical_shrink_interval" int4 NOT NULL DEFAULT 2,
    "vertical_shrink_amount" int4 NOT NULL DEFAULT 1,
    "min_width" int4 NOT NULL DEFAULT 5,
    "min_height" int4 NOT NULL DEFAULT 3,
    PRIMARY KEY ("id")
);

DROP TABLE IF EXISTS "public"."user_inventory_items";
-- Sequence and defined type
CREATE SEQUENCE IF NOT EXISTS user_inventory_items_id_seq;

-- Table Definition
CREATE TABLE "public"."user_inventory_items" (
    "id" int8 NOT NULL DEFAULT nextval('user_inventory_items_id_seq'::regclass),
    "user_id" int8 NOT NULL,
    "start_slot" int2 NOT NULL CHECK ((start_slot >= 0) AND (start_slot < 12)),
    "slot_width" int2 NOT NULL DEFAULT 1 CHECK (slot_width = ANY (ARRAY[1, 2])),
    "is_equipped" bool NOT NULL DEFAULT false,
    "chamber_rounds" int4 NOT NULL DEFAULT 0,
    "item_id" int8,
    "rounds" int4 NOT NULL DEFAULT 0,
    PRIMARY KEY ("id")
);

DROP TABLE IF EXISTS "public"."users";
-- Sequence and defined type
CREATE SEQUENCE IF NOT EXISTS users_id_seq;

-- Table Definition
CREATE TABLE "public"."users" (
    "id" int8 NOT NULL DEFAULT nextval('users_id_seq'::regclass),
    "username" text NOT NULL,
    "password" text NOT NULL,
    "max_hp" int4 NOT NULL DEFAULT 10,
    "max_ap" int4 NOT NULL DEFAULT 100,
    "experience" int4 NOT NULL DEFAULT 0,
    "strength" int4 NOT NULL DEFAULT 10,
    "endurance" int4 NOT NULL DEFAULT 10,
    "accuracy" int4 NOT NULL DEFAULT 10,
    "intuition" int4 NOT NULL DEFAULT 0,
    "intellect" int4 NOT NULL DEFAULT 0,
    "current_hp" int4 NOT NULL DEFAULT 10,
    "active_session_jti" text,
    "agility" int4 NOT NULL DEFAULT 0,
    "equipped_item_id" int8,
    PRIMARY KEY ("id")
);

DROP TABLE IF EXISTS "public"."hope_schema_migrations";
-- Table Definition
CREATE TABLE "public"."hope_schema_migrations" (
    "id" text NOT NULL,
    PRIMARY KEY ("id")
);

DROP TABLE IF EXISTS "public"."battle_obstacle_balance";
-- Table Definition
CREATE TABLE "public"."battle_obstacle_balance" (
    "id" int4 NOT NULL,
    "wall_max_hp" int4 NOT NULL DEFAULT 5,
    "tree_cover_miss_percent" int4 NOT NULL DEFAULT 15,
    "rock_cover_miss_percent" int4 NOT NULL DEFAULT 20,
    "wall_segments_count" int4 NOT NULL DEFAULT 10,
    "rock_count" int4 NOT NULL DEFAULT 5,
    "tree_count" int4 NOT NULL DEFAULT 5,
    PRIMARY KEY ("id")
);

DROP TABLE IF EXISTS "public"."battles";
-- Table Definition
CREATE TABLE "public"."battles" (
    "battle_id" text NOT NULL,
    "created_utc" timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY ("battle_id")
);

DROP TABLE IF EXISTS "public"."weapons";
-- Sequence and defined type
CREATE SEQUENCE IF NOT EXISTS weapons_id_seq;

-- Table Definition
CREATE TABLE "public"."weapons" (
    "id" int8 NOT NULL DEFAULT nextval('weapons_id_seq'::regclass),
    "name" text NOT NULL,
    "damage" int4 NOT NULL,
    "range" int4 NOT NULL,
    "icon_key" text NOT NULL DEFAULT 'fist'::text,
    "attack_ap_cost" int4 NOT NULL DEFAULT 1,
    "spread_penalty" float4 NOT NULL DEFAULT 1.0,
    "trajectory_height" int4 NOT NULL DEFAULT 1,
    "quality" int4 NOT NULL DEFAULT 100,
    "weapon_condition" int4 NOT NULL DEFAULT 100,
    "is_sniper" bool NOT NULL DEFAULT false,
    "mass" float8 NOT NULL DEFAULT 0,
    "armor_pierce" int4 NOT NULL DEFAULT 0,
    "magazine_size" int4 NOT NULL DEFAULT 0,
    "reload_ap_cost" int4 NOT NULL DEFAULT 0,
    "category" text NOT NULL DEFAULT 'cold'::text,
    "req_level" int4 NOT NULL DEFAULT 1,
    "req_strength" int4 NOT NULL DEFAULT 0,
    "req_endurance" int4 NOT NULL DEFAULT 0,
    "req_accuracy" int4 NOT NULL DEFAULT 0,
    "req_mastery_category" text NOT NULL DEFAULT ''::text,
    "stat_effect_strength" int4 NOT NULL DEFAULT 0,
    "stat_effect_endurance" int4 NOT NULL DEFAULT 0,
    "stat_effect_accuracy" int4 NOT NULL DEFAULT 0,
    "damage_type" text NOT NULL DEFAULT 'physical'::text,
    "damage_min" int4 NOT NULL DEFAULT 1,
    "damage_max" int4 NOT NULL DEFAULT 1,
    "burst_rounds" int4 NOT NULL DEFAULT 0,
    "burst_ap_cost" int4 NOT NULL DEFAULT 0,
    "inventory_slot_width" int4 NOT NULL DEFAULT 1,
    "item_id" int8,
    "ammo_type_id" int8,
    PRIMARY KEY ("id")
);

DROP TABLE IF EXISTS "public"."items";
-- Sequence and defined type
CREATE SEQUENCE IF NOT EXISTS items_id_seq;

-- Table Definition
CREATE TABLE "public"."items" (
    "id" int8 NOT NULL DEFAULT nextval('items_id_seq'::regclass),
    "name" text NOT NULL,
    "mass" float8 NOT NULL DEFAULT 0,
    "quality" int4 NOT NULL DEFAULT 100,
    "condition" int4 NOT NULL DEFAULT 100,
    "icon_key" text NOT NULL DEFAULT ''::text,
    "type" text NOT NULL,
    "inventorygrid" int2 NOT NULL DEFAULT 1 CHECK (inventorygrid = ANY (ARRAY[0, 1, 2])),
    "is_equippable" bool NOT NULL DEFAULT false,
    "category" text NOT NULL DEFAULT ''::text,
    PRIMARY KEY ("id")
);

DROP TABLE IF EXISTS "public"."medicine";
-- Table Definition
CREATE TABLE "public"."medicine" (
    "item_id" int8 NOT NULL,
    "attack_ap_cost" int4 NOT NULL DEFAULT 3,
    "req_level" int4 NOT NULL DEFAULT 1,
    "req_strength" int4 NOT NULL DEFAULT 0,
    "req_endurance" int4 NOT NULL DEFAULT 0,
    "req_accuracy" int4 NOT NULL DEFAULT 0,
    "req_mastery_category" text NOT NULL DEFAULT ''::text,
    "effect_type" text NOT NULL DEFAULT ''::text,
    "effect_sign" text NOT NULL DEFAULT 'positive'::text,
    "effect_min" int4 NOT NULL DEFAULT 0,
    "effect_max" int4 NOT NULL DEFAULT 0,
    "effect_target" text NOT NULL DEFAULT 'enemy'::text,
    "inventory_slot_width" int4 NOT NULL DEFAULT 1 CHECK (inventory_slot_width = ANY (ARRAY[1, 2])),
    PRIMARY KEY ("item_id")
);

INSERT INTO "public"."body_parts" ("id", "code") VALUES
(1, 'head'),
(2, 'torso'),
(3, 'legs'),
(4, 'left_arm'),
(5, 'right_arm');

INSERT INTO "public"."battle_zone_shrink" ("id", "shrink_start_round", "horizontal_shrink_interval", "horizontal_shrink_amount", "vertical_shrink_interval", "vertical_shrink_amount", "min_width", "min_height") VALUES
(1, 10, 2, 2, 2, 1, 5, 5);

INSERT INTO "public"."user_inventory_items" ("id", "user_id", "start_slot", "slot_width", "is_equipped", "chamber_rounds", "item_id", "rounds") VALUES
(124, 2, 0, 1, 't', 0, 1, 0),
(125, 3, 0, 1, 't', 0, 1, 0),
(126, 4, 0, 1, 't', 0, 1, 0),
(127, 5, 0, 1, 't', 0, 1, 0),
(128, 6, 0, 1, 't', 0, 1, 0),
(129, 7, 0, 1, 't', 0, 1, 0),
(130, 8, 0, 1, 't', 0, 1, 0),
(131, 9, 0, 1, 't', 0, 1, 0),
(132, 10, 0, 1, 't', 0, 1, 0),
(133, 11, 0, 1, 't', 0, 1, 0),
(134, 12, 0, 1, 't', 0, 1, 0),
(144, 1, 0, 1, 't', 0, 1, 1),
(145, 1, 1, 1, 'f', 0, 2, 1),
(146, 1, 2, 1, 'f', 5, 3, 1),
(147, 1, 3, 1, 'f', 25, 4, 1),
(148, 1, 4, 1, 'f', 0, 78, 20),
(149, 1, 11, 1, 'f', 0, 37, 25),
(150, 1, 10, 1, 'f', 0, 35, 40),
(151, 2, 1, 1, 'f', 0, 2, 1),
(152, 2, 2, 1, 'f', 5, 3, 1),
(153, 2, 3, 1, 'f', 25, 4, 1),
(154, 2, 4, 1, 'f', 0, 78, 20),
(155, 2, 11, 1, 'f', 0, 37, 25),
(156, 2, 10, 1, 'f', 0, 35, 40),
(157, 3, 1, 1, 'f', 0, 2, 1),
(158, 3, 2, 1, 'f', 5, 3, 1),
(159, 3, 3, 1, 'f', 25, 4, 1),
(160, 3, 4, 1, 'f', 0, 78, 20),
(161, 3, 11, 1, 'f', 0, 37, 25),
(162, 3, 10, 1, 'f', 0, 35, 40),
(163, 4, 1, 1, 'f', 0, 2, 1),
(164, 4, 2, 1, 'f', 5, 3, 1),
(165, 4, 3, 1, 'f', 25, 4, 1),
(166, 4, 4, 1, 'f', 0, 78, 20),
(167, 4, 11, 1, 'f', 0, 37, 25),
(168, 4, 10, 1, 'f', 0, 35, 40),
(169, 5, 1, 1, 'f', 0, 2, 1),
(170, 5, 2, 1, 'f', 5, 3, 1),
(171, 5, 3, 1, 'f', 25, 4, 1),
(172, 5, 4, 1, 'f', 0, 78, 20),
(173, 5, 11, 1, 'f', 0, 37, 25),
(174, 5, 10, 1, 'f', 0, 35, 40),
(175, 6, 1, 1, 'f', 0, 2, 1),
(176, 6, 2, 1, 'f', 5, 3, 1),
(177, 6, 3, 1, 'f', 25, 4, 1),
(178, 6, 4, 1, 'f', 0, 78, 20),
(179, 6, 11, 1, 'f', 0, 37, 25),
(180, 6, 10, 1, 'f', 0, 35, 40),
(181, 7, 1, 1, 'f', 0, 2, 1),
(182, 7, 2, 1, 'f', 5, 3, 1),
(183, 7, 3, 1, 'f', 25, 4, 1),
(184, 7, 4, 1, 'f', 0, 78, 20),
(185, 7, 11, 1, 'f', 0, 37, 25),
(186, 7, 10, 1, 'f', 0, 35, 40),
(187, 8, 1, 1, 'f', 0, 2, 1),
(188, 8, 2, 1, 'f', 5, 3, 1),
(189, 8, 3, 1, 'f', 25, 4, 1),
(190, 8, 4, 1, 'f', 0, 78, 20),
(191, 8, 11, 1, 'f', 0, 37, 25),
(192, 8, 10, 1, 'f', 0, 35, 40),
(193, 9, 1, 1, 'f', 0, 2, 1),
(194, 9, 2, 1, 'f', 5, 3, 1),
(195, 9, 3, 1, 'f', 25, 4, 1),
(196, 9, 4, 1, 'f', 0, 78, 20),
(197, 9, 11, 1, 'f', 0, 37, 25),
(198, 9, 10, 1, 'f', 0, 35, 40),
(199, 10, 1, 1, 'f', 0, 2, 1),
(200, 10, 2, 1, 'f', 5, 3, 1),
(201, 10, 3, 1, 'f', 25, 4, 1),
(202, 10, 4, 1, 'f', 0, 78, 20),
(203, 10, 11, 1, 'f', 0, 37, 25),
(204, 10, 10, 1, 'f', 0, 35, 40),
(205, 11, 1, 1, 'f', 0, 2, 1),
(206, 11, 2, 1, 'f', 5, 3, 1),
(207, 11, 3, 1, 'f', 25, 4, 1),
(208, 11, 4, 1, 'f', 0, 78, 20),
(209, 11, 11, 1, 'f', 0, 37, 25),
(210, 11, 10, 1, 'f', 0, 35, 40),
(211, 12, 1, 1, 'f', 0, 2, 1),
(212, 12, 2, 1, 'f', 5, 3, 1),
(213, 12, 3, 1, 'f', 25, 4, 1),
(214, 12, 4, 1, 'f', 0, 78, 20),
(215, 12, 11, 1, 'f', 0, 37, 25),
(216, 12, 10, 1, 'f', 0, 35, 40);

INSERT INTO "public"."users" ("id", "username", "password", "max_hp", "max_ap", "experience", "strength", "endurance", "accuracy", "intuition", "intellect", "current_hp", "active_session_jti", "agility", "equipped_item_id") VALUES
(1, 'alan', 'test', 100, 100, 100, 10, 10, 10, 0, 0, 100, '', 0, 1),
(2, 'stacy', 'test', 100, 100, 0, 10, 10, 10, 0, 0, 100, '', 0, 1),
(3, 'bushhiroo', 'test', 100, 100, 0, 10, 10, 10, 0, 0, 100, '', 0, 1),
(4, 'cronos939', 'test', 100, 100, 0, 10, 10, 10, 0, 0, 100, '', 0, 1),
(5, 'lovemarriage77', 'test', 100, 100, 0, 10, 10, 10, 0, 0, 100, '', 0, 1),
(6, 'v.dusiak', 'test', 100, 100, 0, 10, 10, 10, 0, 0, 100, '', 0, 1),
(7, 'lasqsurr', 'test', 100, 100, 0, 10, 10, 10, 0, 0, 100, '', 0, 1),
(8, 's.adpushchennikau', 'test', 100, 100, 0, 10, 10, 10, 0, 0, 100, '', 0, 1),
(9, 'iurii007', 'test', 100, 100, 0, 10, 10, 10, 0, 0, 100, '', 0, 1),
(10, 'vladheylo', 'test', 100, 100, 0, 10, 10, 10, 0, 0, 100, '', 0, 1),
(11, 'kasperini1994', 'test', 100, 100, 0, 10, 10, 10, 0, 0, 100, '', 0, 1),
(12, 'xomutof.alex176', 'test', 100, 100, 0, 10, 10, 10, 0, 0, 100, '', 0, 1);

INSERT INTO "public"."hope_schema_migrations" ("id") VALUES
('medicine_split_weapons_effect_v1'),
('weapons_spread_column_is_tightness_v1');

INSERT INTO "public"."battle_obstacle_balance" ("id", "wall_max_hp", "tree_cover_miss_percent", "rock_cover_miss_percent", "wall_segments_count", "rock_count", "tree_count") VALUES
(1, 35, 15, 20, 40, 15, 25);

INSERT INTO "public"."weapons" ("id", "name", "damage", "range", "icon_key", "attack_ap_cost", "spread_penalty", "trajectory_height", "quality", "weapon_condition", "is_sniper", "mass", "armor_pierce", "magazine_size", "reload_ap_cost", "category", "req_level", "req_strength", "req_endurance", "req_accuracy", "req_mastery_category", "stat_effect_strength", "stat_effect_endurance", "stat_effect_accuracy", "damage_type", "damage_min", "damage_max", "burst_rounds", "burst_ap_cost", "inventory_slot_width", "item_id", "ammo_type_id") VALUES
(1, 'Fist', 1, 1, 'weapon_fist', 1, -1, -1, 100, 100, 'f', 0, 0, -1, -1, 'cold', 1, 0, 0, 0, '0', 0, 0, 0, 'physical', 1, 1, -1, -1, 1, 1, NULL),
(2, 'Knife', 4, 1, 'weapon_knife', 3, -1, -1, 100, 100, 'f', 100, 0, -1, -1, 'cold', 1, 0, 0, 0, '0', 0, 0, 0, 'physical', 1, 4, -1, -1, 1, 2, NULL),
(3, 'Pistol', 6, 4, 'weapon_pistol', 4, 1, 1, 100, 100, 'f', 100, 0, 5, 3, 'light', 0, 0, 0, 0, '0', 0, 0, 0, 'physical', 1, 6, 3, 10, 1, 3, 35),
(4, 'Riffle', 7, 6, 'weapon_riffle', 6, 1, 1, 100, 100, 'f', 100, -1, 25, 14, 'medium', 1, 0, 0, 0, '0', 0, 0, 0, 'physical', 4, 7, -1, -1, 1, 4, 37);

INSERT INTO "public"."items" ("id", "name", "mass", "quality", "condition", "icon_key", "type", "inventorygrid", "is_equippable", "category") VALUES
(1, 'Fist', 0, 100, 100, 'weapon_fist', 'weapon', 1, 't', ''),
(2, 'Knife', 100, 100, 100, 'weapon_knife', 'weapon', 1, 't', ''),
(3, 'Pistol', 100, 100, 100, 'weapon_pistol', 'weapon', 1, 't', ''),
(4, 'Riffle', 100, 100, 100, 'weapon_riffle', 'weapon', 1, 't', ''),
(35, '9mm', 0.02, 100, 100, 'ammo_9', 'ammo', 1, 'f', ''),
(37, '5.45mm', 0.02, 100, 100, 'ammo_545', 'ammo', 1, 'f', ''),
(78, 'Medkit', 1, 100, 100, 'item_medkit', 'medicine', 1, 't', 'medkit');

INSERT INTO "public"."medicine" ("item_id", "attack_ap_cost", "req_level", "req_strength", "req_endurance", "req_accuracy", "req_mastery_category", "effect_type", "effect_sign", "effect_min", "effect_max", "effect_target", "inventory_slot_width") VALUES
(78, 5, 0, 0, 0, 0, '0', 'hp', 'positive', 4, 8, 'self', 1);

ALTER TABLE "public"."battle_turn_links" ADD FOREIGN KEY ("turn_id") REFERENCES "public"."battle_turns"("turn_id") ON DELETE CASCADE;
ALTER TABLE "public"."battle_turn_links" ADD FOREIGN KEY ("battle_id") REFERENCES "public"."battles"("battle_id") ON DELETE CASCADE;


-- Indices
CREATE UNIQUE INDEX battle_turn_links_turn_id_key ON public.battle_turn_links USING btree (turn_id);
CREATE INDEX ix_battle_turn_links_battle_id_turn_index ON public.battle_turn_links USING btree (battle_id, turn_index);
ALTER TABLE "public"."battle_turns" ADD FOREIGN KEY ("battle_id") REFERENCES "public"."battles"("battle_id") ON DELETE CASCADE;


-- Indices
CREATE UNIQUE INDEX body_parts_code_key ON public.body_parts USING btree (code);
ALTER TABLE "public"."user_inventory_items" ADD FOREIGN KEY ("user_id") REFERENCES "public"."users"("id") ON DELETE CASCADE;
ALTER TABLE "public"."user_inventory_items" ADD FOREIGN KEY ("item_id") REFERENCES "public"."items"("id") ON DELETE CASCADE;


-- Indices
CREATE UNIQUE INDEX uq_user_inventory_one_equipped ON public.user_inventory_items USING btree (user_id) WHERE is_equipped;
CREATE INDEX ix_user_inventory_items_user_id ON public.user_inventory_items USING btree (user_id);
CREATE INDEX ix_user_inventory_items_user_item ON public.user_inventory_items USING btree (user_id, item_id);
CREATE UNIQUE INDEX uq_user_inventory_user_item ON public.user_inventory_items USING btree (user_id, item_id) WHERE (item_id IS NOT NULL);
ALTER TABLE "public"."users" ADD FOREIGN KEY ("equipped_item_id") REFERENCES "public"."items"("id") ON DELETE SET NULL;


-- Indices
CREATE UNIQUE INDEX users_username_key ON public.users USING btree (username);
ALTER TABLE "public"."weapons" ADD FOREIGN KEY ("ammo_type_id") REFERENCES "public"."items"("id") ON DELETE SET NULL;
ALTER TABLE "public"."weapons" ADD FOREIGN KEY ("item_id") REFERENCES "public"."items"("id") ON DELETE SET NULL;


-- Indices
CREATE UNIQUE INDEX uq_weapons_item_id ON public.weapons USING btree (item_id);


-- Indices
CREATE UNIQUE INDEX uq_items_name_unique ON public.items USING btree (lower(name));
ALTER TABLE "public"."medicine" ADD FOREIGN KEY ("item_id") REFERENCES "public"."items"("id") ON DELETE CASCADE;
