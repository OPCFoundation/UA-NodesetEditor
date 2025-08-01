---------------------------------------------------------------------
--  profile Designer DB
--	Create Script for initial profile designer db. 
--
--	Details:
--	Create DB, create login, assign ownership
--	Create users
--
--	Create lookup data
--	Create profiles, profile attributes
--
---------------------------------------------------------------------

---------------------------------------------------------------------
--	Create Login
---------------------------------------------------------------------
/*
-- DROP ROLE cesmii;
CREATE ROLE cesmii WITH
  LOGIN
  NOSUPERUSER
  INHERIT
  NOCREATEDB
  NOCREATEROLE
  NOREPLICATION
  ENCRYPTED PASSWORD '***********';
*/
---------------------------------------------------------------------
--	Create DB
---------------------------------------------------------------------
/*
-- DROP DATABASE profile_designer_dev;
CREATE DATABASE profile_designer_dev
    WITH 
    OWNER = cesmii
    ENCODING = 'UTF8'
    LC_COLLATE = 'en_US.UTF-8'
    LC_CTYPE = 'en_US.UTF-8'
    TABLESPACE = pg_default
    CONNECTION LIMIT = -1;

COMMENT ON DATABASE profile_designer_dev
    IS 'dev instance of the profile designer DB';
*/

---------------------------------------------------------------------
--	0. Drop All and Re-start from original
---------------------------------------------------------------------
DROP VIEW IF EXISTS public.v_data_type_rank;
DROP VIEW IF EXISTS public.v_engineering_unit_rank;

DROP TABLE IF EXISTS public.profile_attribute;
DROP TABLE IF EXISTS public.profile_interface;
DROP TABLE IF EXISTS public.profile_composition;
DROP TABLE IF EXISTS public.profile_type_definition_user_analytics;
DROP TABLE IF EXISTS public.profile_type_definition_user_favorite;
DROP TABLE IF EXISTS public.data_type_rank;
DROP TABLE IF EXISTS public.data_type;
DROP TABLE IF EXISTS public.engineering_unit_rank;
DROP TABLE IF EXISTS public.engineering_unit;
DROP TABLE IF EXISTS public.profile_nodeset_file;
DROP TABLE IF EXISTS public.profile_type_definition;
DROP TABLE IF EXISTS public.import_log_warning;
DROP TABLE IF EXISTS public.profile_additional_properties;
DROP TABLE IF EXISTS public.profile;
DROP TABLE IF EXISTS public.import_file_chunk;
DROP TABLE IF EXISTS public.import_file;
DROP TABLE IF EXISTS public.import_log_message;
DROP TABLE IF EXISTS public.import_log;

DROP TABLE IF EXISTS public.lookup;
DROP TABLE IF EXISTS public.lookup_type;
DROP TABLE IF EXISTS public.nodeset_file;
DROP TABLE IF EXISTS public.standard_nodeset;
DROP TABLE IF EXISTS public.user_permission;
DROP TABLE IF EXISTS public.permission;
DROP TABLE IF EXISTS public.user;
DROP TABLE IF EXISTS public.organization;
DROP TABLE IF EXISTS public.app_log;


---------------------------------------------------------------------
--	0. Create application log table
---------------------------------------------------------------------
--DROP TABLE public.app_log;
CREATE TABLE public.app_log
(
    id SERIAL PRIMARY KEY,
    application character varying(50) COLLATE pg_catalog."default" NULL,
    host_name character varying(100) COLLATE pg_catalog."default" NULL,
    message varchar COLLATE pg_catalog."default" NULL,
    exception_message varchar COLLATE pg_catalog."default" NULL,
    level character varying(128) COLLATE pg_catalog."default" NULL,
    logger character varying(256) COLLATE pg_catalog."default" NULL,
    type character varying(128) COLLATE pg_catalog."default" NULL,
    call_site character varying(128) COLLATE pg_catalog."default" NULL,
    inner_exception varchar COLLATE pg_catalog."default" NULL,
    stack_trace varchar COLLATE pg_catalog."default" NULL,
    created timestamp with time zone NULL DEFAULT now()
)

TABLESPACE pg_default;

ALTER TABLE public.app_log
    OWNER to profiledesigner;

---------------------------------------------------------------------
--	Org TABLE
---------------------------------------------------------------------
-- DROP TABLE public.organization;
CREATE TABLE public.organization
(
    id SERIAL PRIMARY KEY,
    name character varying(128) COLLATE pg_catalog."default" NOT NULL,
    description character varying(512) COLLATE pg_catalog."default" NULL
)

TABLESPACE pg_default;

ALTER TABLE public.organization
    OWNER to profiledesigner;

---------------------------------------------------------------------
--	Insert orgs
---------------------------------------------------------------------
INSERT INTO public.organization(name, description)
SELECT 'CESMII', 'description here'
;
---------------------------------------------------------------------
--	USERS TABLE
---------------------------------------------------------------------
-- DROP TABLE public.user;
CREATE TABLE public.user
(
    id SERIAL PRIMARY KEY,
    organization_id integer NULL,
    last_login timestamp with time zone,
    --username character varying(150) COLLATE pg_catalog."default" NULL,
    objectid_aad character varying(100) COLLATE pg_catalog."default" NULL, 
    display_name character varying(250) COLLATE pg_catalog."default" NULL,
    email_address character varying(320) COLLATE pg_catalog."default" NULL,
    --is_active boolean NOT NULL,
    date_joined timestamp with time zone NOT NULL,
    sssu_organization character varying (128) NULL,
    sssu_cesmii_member boolean NULL,
    --registration_complete timestamp with time zone,
    --CONSTRAINT user_username_key UNIQUE (username),
    CONSTRAINT user_id_fk_org_id FOREIGN KEY (organization_id)
        REFERENCES public.organization (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.user
    OWNER to profiledesigner;
-- Index: user_username_6821ab7c_like

-- DROP INDEX public.user_username_6821ab7c_like;

/*CREATE INDEX user_username_6821ab7c_like
    ON public.user USING btree
    (username COLLATE pg_catalog."default" varchar_pattern_ops ASC NULLS LAST)
    TABLESPACE pg_default;
*/	
	

---------------------------------------------------------------------
--	TABLE LOOKUP TYPE
---------------------------------------------------------------------
--DROP TABLE public.lookup_type;
CREATE TABLE public.lookup_type
(
    id SERIAL PRIMARY KEY,
    --owner_id integer NULL,
    name character varying(200) COLLATE pg_catalog."default" NOT NULL,
    description character varying COLLATE pg_catalog."default" NULL,
    display_order integer NOT NULL,
    is_active boolean NOT NULL
)

TABLESPACE pg_default;

ALTER TABLE public.lookup_type
    OWNER to profiledesigner;

----------------------------------------------------------
-- DROP TABLE public.lookup;
CREATE TABLE public.lookup
(
    id SERIAL PRIMARY KEY,
    --owner_id integer NULL,
    code character varying(200) COLLATE pg_catalog."default" NOT NULL,
    name character varying(200) COLLATE pg_catalog."default" NOT NULL,
    type_id integer NOT NULL,
    display_order integer NOT NULL,
    is_active boolean NOT NULL,
    CONSTRAINT lookup_typeid FOREIGN KEY (type_id)
        REFERENCES public.lookup_type (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.lookup
    OWNER to profiledesigner;

---------------------------------------------------------------------
--	Insert lookup types
---------------------------------------------------------------------
INSERT INTO public.lookup_type(id, name, display_order, is_active)
SELECT 1, 'ProfileType', 1, true
UNION SELECT 2, 'AttributeType', 2, true
--UNION SELECT 3, 'EngUnit', 3, true
UNION SELECT 4, 'TaskStatus', 4, true
;

---------------------------------------------------------------------
--	Insert lookup items
---------------------------------------------------------------------
--need the ids to be reliably the same b/c front end will have some behavior for special data types
INSERT INTO public.lookup(id, code, name, display_order, is_active, type_id )

SELECT 1, 'Interface', 'Interface', 9999, true, id FROM public.lookup_type WHERE name = 'ProfileType'
UNION SELECT  2, 'Class', 'Class', 9999, true, id FROM public.lookup_type WHERE name = 'ProfileType'
UNION SELECT  12, 'VariableType', 'Variable Type', 9999, true, id FROM public.lookup_type WHERE name = 'ProfileType'
UNION SELECT  3, 'DataType', 'Data Type', 9999, true, id FROM public.lookup_type WHERE name = 'ProfileType'
UNION SELECT  18, 'Struct', 'Structure', 9999, true, id FROM public.lookup_type WHERE name = 'ProfileType'
UNION SELECT  19, 'Enum', 'Enumeration', 9999, true, id FROM public.lookup_type WHERE name = 'ProfileType'
UNION SELECT  11, 'Object', 'Object', 9999, true, id FROM public.lookup_type WHERE name = 'ProfileType'
UNION SELECT  20, 'Method', 'Method', 9999, true, id FROM public.lookup_type WHERE name = 'ProfileType'

UNION SELECT  4, 'Other', 'Other', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
--attribute type
UNION SELECT  5, 'Property', 'Property', 9999, true, id FROM public.lookup_type WHERE name = 'AttributeType'
UNION SELECT  6, 'DataVariable', 'Data Variable', 9999, true, id FROM public.lookup_type WHERE name = 'AttributeType'
UNION SELECT  7, 'StructField', 'Structure Field', 9999, true, id FROM public.lookup_type WHERE name = 'AttributeType' -- only for profile type Struct (9)
UNION SELECT  8, 'EnumField', 'Enumeration Field', 9999, true, id FROM public.lookup_type WHERE name = 'AttributeType' -- only for profile type Enum (10)
UNION SELECT  9, 'Composition', 'Composition', 9999, true, id FROM public.lookup_type WHERE name = 'AttributeType'
UNION SELECT  10, 'Interface', 'Interface', 9999, true, id FROM public.lookup_type WHERE name = 'AttributeType'
--TaskStatus
UNION SELECT  13, 'NotStarted', 'Not Started', 1, true, id FROM public.lookup_type WHERE name = 'TaskStatus'
UNION SELECT  14, 'InProgress', 'In Progress', 2, true, id FROM public.lookup_type WHERE name = 'TaskStatus'
UNION SELECT  15, 'Completed', 'Completed', 3, true, id FROM public.lookup_type WHERE name = 'TaskStatus' 
UNION SELECT  16, 'Failed', 'Failed', 4, true, id FROM public.lookup_type WHERE name = 'TaskStatus' 
UNION SELECT  17, 'Cancelled', 'Cancelled', 5, true, id FROM public.lookup_type WHERE name = 'TaskStatus' 
;

--manually adjust the identity starting val
SELECT setval('lookup_id_seq', 21);

/*
INSERT INTO public.lookup(code, name, display_order, is_active, type_id )
SELECT  'hour', 'Duration (hr)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'minute', 'Duration (min)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'second', 'Duration (sec)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'millisecond', 'Duration (ms)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'tick', 'Duration (tick)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'meter', 'Length (m)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'centimeter', 'Length (cm)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'millimeter', 'Length (mm)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'foot', 'Length (ft)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'inch', 'Length (inch)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'kilogram', 'Mass (kg)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'gram', 'Mass (g)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'milligram', 'Mass (mg)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'celsius', 'Temperature (C)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'farenheit', 'Temperature (F)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'kelvin', 'Temperature (K)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'liter', 'Volume (liter)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'cubic centimeter', 'Volume (cc)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'milliliter', 'Volume (ml)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'gallon', 'Volume (gallon)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'pound', 'Weight (lb)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
UNION SELECT  'ounce', 'Weight (oz)', 9999, true, id FROM public.lookup_type WHERE name = 'EngUnit'
;
*/
----------------------------------------------------------
-- DROP TABLE public.engineering_unit;
CREATE TABLE public.engineering_unit
(
    id SERIAL PRIMARY KEY,
    owner_id integer NULL,
    display_name character varying(200) COLLATE pg_catalog."default",
    description character varying COLLATE pg_catalog."default" NULL,
    namespace_uri character varying(200) COLLATE pg_catalog."default" NULL,
    unit_id integer NOT NULL,
    is_active boolean NOT NULL
)

TABLESPACE pg_default;

ALTER TABLE public.engineering_unit
    OWNER to profiledesigner;

---------------------------------------------------------------------
--	Create NodeSet Lookup Table
---------------------------------------------------------------------

/*
CREATE TABLE public.standard_nodeset
(
    id SERIAL PRIMARY KEY,
    namespace character varying(400) COLLATE pg_catalog."default" NOT NULL,
    version character varying(25) COLLATE pg_catalog."default" NULL,
    filename character varying(255) COLLATE pg_catalog."default",
    publish_date timestamp with time zone,
    cloudlibrary_id character varying(25) COLLATE  pg_catalog."default" NOT NULL,
    is_active boolean NOT NULL,

    CONSTRAINT namespace_publish_date_14a6b632_uniq UNIQUE (namespace, publish_date)

)

TABLESPACE pg_default;

ALTER TABLE public.standard_nodeset
    OWNER to profiledesigner;
*/
/* These now come from the cloud library
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (1, 'http://opcfoundation.org/UA/ADI/', '1.01', NULL, '2013-07-31', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (2, 'http://opcfoundation.org/UA/AML/', '1.00', NULL, '2016-02-22', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (3, 'http://opcfoundation.org/UA/AMLLibs/', '', NULL, NULL, true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (4, 'http://opcfoundation.org/UA/AutoID/', '1.01', NULL, '2020-06-18', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (5, 'http://opcfoundation.org/UA/CAS/', '1.00.1', NULL, '2021-07-13', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (6, 'http://opcfoundation.org/UA/', '1.04.9', NULL, '2021-01-21', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (7, 'http://opcfoundation.org/UA/', '1.04.7', NULL, '2020-07-15', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (8, 'http://opcfoundation.org/UA/', '1.03.7', NULL, '2019-07-23', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (9, 'http://opcfoundation.org/UA/', '1.02', NULL, '2013-03-06', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (10, 'http://opcfoundation.org/UA/DI/', '1.03.0', NULL, '2021-03-09', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (11, 'http://opcfoundation.org/UA/Robotics/', '1.0.0', NULL, '2019-05-06', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (12, 'http://opcfoundation.org/UA/Machinery/', '1.01.0', NULL, '2021-02-25', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (13, 'http://opcfoundation.org/UA/CNC/', '1.0.0', NULL, '2017-06-16', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (14, 'http://opcfoundation.org/UA/CommercialKitchenEquipment/', '1.1.0', NULL, '2021-01-27', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (15, 'http://opcfoundation.org/UA/CSPPlusForMachine/', '1.00', NULL, '2017-11-28', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (16, 'http://opcfoundation.org/UA/DEXPI/', '1.0.0', NULL, '2021-09-10', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (17, 'http://fdi-cooperation.com/OPCUA/FDI5/', '1.1', NULL, '2017-07-14', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (18, 'http://fdi-cooperation.com/OPCUA/FDI7/', '', NULL, '2017-07-14', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (19, 'http://opcfoundation.org/UA/FDT/', '1.01.00', NULL, '2021-08-06', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (20, 'http://opcfoundation.org/UA/I4AAS/', '5.0.0', NULL, '2021-06-04', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (21, 'http://opcfoundation.org/UA/IA/', '1.01.0', NULL, '2021-07-31', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (22, 'http://opcfoundation.org/UA/IEC61850-6/', '2.0', NULL, '2018-02-05', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (23, 'http://opcfoundation.org/UA/IEC61850-7-3/', '2.0', NULL, '2018-02-05', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (24, 'http://opcfoundation.org/UA/IEC61850-7-4/', '2.0', NULL, '2018-02-05', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (25, 'http://opcfoundation.org/UA/IOLink/', '1.0', NULL, '2018-12-01', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (26, 'http://opcfoundation.org/UA/IOLink/IODD/', '1.0', NULL, '2018-12-01', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (27, 'http://www.OPCFoundation.org/UA/2013/01/ISA95', '1.00', NULL, '2013-12-02', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (28, 'http://opcfoundation.org/UA/ISA95-JOBCONTROL', '1.0.0', NULL, '2021-03-31', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (29, 'http://opcfoundation.org/UA/MachineTool/', '1.00.0', NULL, '2020-09-25', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (30, 'http://opcfoundation.org/UA/MachineVision/', '1.0.0', NULL, '2019-07-11', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (31, 'http://opcfoundation.org/UA/MDIS', '1.20', NULL, '2018-10-03', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (32, 'http://opcfoundation.org/UA/MTConnect/v2/', '2.00.01', NULL, '2020-06-05', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (33, 'http://opcfoundation.org/UA/OPENSCS-SER/', '1.00', NULL, '2019-02-04', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (34, 'http://opcfoundation.org/UA/PackML/', '1.01', NULL, '2020-10-08', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (35, 'http://opcfoundation.org/UA/Weihenstephan/', '1.00.0', NULL, '2021-07-12', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (36, 'http://opcfoundation.org/UA/Scales', '1.0', NULL, '2020-06-01', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (37, 'http://opcfoundation.org/UA/TMC/', '1.0', NULL, '2017-10-11', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (38, 'http://opcfoundation.org/UA/Pumps/', '1.0.0', NULL, '2021-04-19', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (39, 'http://PLCopen.org/OpcUa/IEC61131-3/', '1.02', NULL, '2020-11-25', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (40, 'http://opcfoundation.org/UA/PNEM/', '1.0.0', NULL, '2021-03-11', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (41, 'http://opcfoundation.org/UA/POWERLINK/', '1.0.0', NULL, '2017-10-10', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (42, 'http://opcfoundation.org/UA/PROFINET/', '1.0.1', NULL, '2021-04-13', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (43, 'http://opcfoundation.org/UA/Robotics/', '1.01.2', NULL, '2019-05-06', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (44, 'http://opcfoundation.org/UA/PlasticsRubber/TCD/', '1.01', NULL, '2020-06-01', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (45, 'http://opcfoundation.org/UA/PlasticsRubber/LDS/', '1.00.1', NULL, '2021-06-21', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (46, 'http://opcfoundation.org/UA/PlasticsRubber/IMM2MES/', '1.01', NULL, '2020-06-01', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (47, 'http://opcfoundation.org/UA/PlasticsRubber/HotRunner/', '1.00', NULL, '2021-05-10', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (48, 'http://opcfoundation.org/UA/PlasticsRubber/GeneralTypes/', '1.03', NULL, '2021-05-10', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (49, 'http://opcfoundation.org/UA/PlasticsRubber/Extrusion/Calender/', '1.00', NULL, '2021-04-01', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (50, 'http://opcfoundation.org/UA/PlasticsRubber/Extrusion/Calibrator/', '1.00', NULL, '2020-06-01', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (51, 'http://opcfoundation.org/UA/PlasticsRubber/Extrusion/Corrugator/', '1.00', NULL, '2020-06-01', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (52, 'http://opcfoundation.org/UA/PlasticsRubber/Extrusion/Cutter/', '1.00', NULL, '2020-06-01', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (53, 'http://opcfoundation.org/UA/PlasticsRubber/Extrusion/Die/', '1.00', NULL, '2020-06-01', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (54, 'http://opcfoundation.org/UA/PlasticsRubber/Extrusion/Extruder/', '1.00', NULL, '2020-06-01', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (55, 'http://opcfoundation.org/UA/PlasticsRubber/Extrusion/ExtrusionLine/', '1.00.01', NULL, '2020-11-09', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (56, 'http://opcfoundation.org/UA/PlasticsRubber/Extrusion/Filter/', '1.00', NULL, '2020-06-01', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (57, 'http://opcfoundation.org/UA/PlasticsRubber/Extrusion/GeneralTypes/', '1.01', NULL, '2021-04-01', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (58, 'http://opcfoundation.org/UA/PlasticsRubber/Extrusion/HaulOff/', '1.00', NULL, '2020-06-01', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (59, 'http://opcfoundation.org/UA/PlasticsRubber/Extrusion/MeltPump/', '1.00', NULL, '2020-06-01', true);
INSERT INTO public.standard_nodeset (id, namespace, version, filename, publish_date, is_active) VALUES (60, 'http://opcfoundation.org/UA/PlasticsRubber/Extrusion/Pelletizer/', '1.00', NULL, '2020-06-01', true);
*/
--manually adjust the identity starting val
--SELECT setval('standard_nodeset_id_seq', 61);

/*profile-profiletype-rename-refactor - START*/
---------------------------------------------------------------------
--	nodeset_file Table
--	Formerly a part of nodeset table
--  Represents a file that was imported. Could have one or many profiles (aka models). 
--  Not directly tied to any version, etc. the contents will have a version 
--	which is part of the profile (aka model)
---------------------------------------------------------------------
-- DROP TABLE public.nodeset_file;

CREATE TABLE public.nodeset_file
(
    id SERIAL PRIMARY KEY,
    owner_id integer NULL,
    --file name is informational only - just the file name as it was imported
	filename character varying(400) COLLATE pg_catalog."default" NOT NULL,
    file_cache text COLLATE pg_catalog."default",
    version character varying(25) COLLATE pg_catalog."default",
    publish_date timestamp with time zone,
    imported_by_id integer NULL,
    CONSTRAINT nodeset_imported_by_id FOREIGN KEY (imported_by_id)
        REFERENCES public.user (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED	
)

TABLESPACE pg_default;

ALTER TABLE public.nodeset_file
    OWNER to profiledesigner;
	
---------------------------------------------------------------------
--	profile Table (aka Model in OPC nomenclature - formerly a part of nodeset table)
---------------------------------------------------------------------
-- DROP TABLE public.profile;

CREATE TABLE public.profile
(
    id SERIAL PRIMARY KEY,
    owner_id integer NULL,
    namespace character varying(400) COLLATE pg_catalog."default" NOT NULL,
    version character varying(25) COLLATE pg_catalog."default",
    publish_date timestamp with time zone NULL,
    xml_schema_uri character varying(400) COLLATE pg_catalog."default" NULL,
    --ua_standard_profile_id integer NULL,
    author_id integer NULL,
    created timestamp with time zone,
    created_by_id integer,
    updated timestamp with time zone,
    updated_by_id integer,
	
	-- Cloud Library meta data
	cloud_library_id character varying(100)  NULL,
  cloud_library_pending_approval boolean NULL,
  title text NULL,
	license text NULL,
	copyright_text text NULL,
	contributor_name text NULL,
	description text NULL,
	category_name text NULL,
	documentation_url text NULL,
	icon_url text NULL,
	license_url text NULL,
	keywords text[] NULL,
    purchasing_information_url text NULL,
	release_notes_url text NULL,
	test_specification_url text NULL,
	supported_locales text[] NULL,
	-- End Cloud Library meta data
	
    --CONSTRAINT profile_standard_profile_id FOREIGN KEY (ua_standard_profile_id)
    --    REFERENCES public.standard_nodeset (id) MATCH SIMPLE
    --    ON UPDATE NO ACTION
    --    ON DELETE NO ACTION
    --    DEFERRABLE INITIALLY DEFERRED,	
    CONSTRAINT profile_author_id FOREIGN KEY (author_id)
        REFERENCES public.user (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,	
	CONSTRAINT profile_unique_name_pub_date UNIQUE (namespace,publish_date,owner_id)
)

TABLESPACE pg_default;

ALTER TABLE public.profile
    OWNER to profiledesigner;

CREATE TABLE public.profile_additional_properties
(
    id SERIAL PRIMARY KEY,
    profile_id integer NOT NULL,
	name text NOT NULL,
	value text NULL,
	CONSTRAINT profile_id_fk FOREIGN KEY (profile_id)
		REFERENCES public.profile (id) MATCH SIMPLE
)
TABLESPACE pg_default;

ALTER TABLE public.profile_additional_properties
    OWNER to profiledesigner;

---------------------------------------------------------------------
--	Create profile to nodeset_file join table
--  A profile can live in many nodeset files (or one)
--  A nodeset file can contain many profiles (or one)
---------------------------------------------------------------------
--DROP TABLE public.profile_nodeset_file;
CREATE TABLE public.profile_nodeset_file
(
    id SERIAL PRIMARY KEY,
    owner_id integer NULL,
    nodeset_file_id integer NOT NULL,
    profile_id integer NOT NULL,
    CONSTRAINT nodeset_file_id_uniq UNIQUE (nodeset_file_id, profile_id),
    CONSTRAINT nodeset_file_id_fk_id FOREIGN KEY (nodeset_file_id)
        REFERENCES public.nodeset_file (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT profile_id_fk FOREIGN KEY (profile_id)
        REFERENCES public.profile (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.profile_nodeset_file
    OWNER to profiledesigner;

---------------------------------------------------------------------
--	Profile Table
---------------------------------------------------------------------
-- DROP TABLE public.profile_type_definition;
CREATE TABLE public.profile_type_definition
(
    id SERIAL PRIMARY KEY,
    owner_id integer NULL,
    type_id integer NOT NULL,  --formerly profile_type_id. this is FK to lookup with values such as class, interface, structure, dataType
    parent_id integer NULL,
    instance_parent_id integer NULL,
	is_option_set boolean NULL,
	variable_data_type_id integer NULL,
	variable_value_rank integer NULL,
	variable_array_dimensions character varying(256) NULL,
	variable_value character varying NULL,
    opc_node_id character varying(256) NULL,
    name character varying(256) COLLATE pg_catalog."default" NOT NULL,
    --namespace character varying(512) COLLATE pg_catalog."default" NULL,
	profile_id integer NOT NULL,
    browse_name character varying(256) COLLATE pg_catalog."default" NULL,
    symbolic_name character varying(256) COLLATE pg_catalog."default" NULL,
    description character varying COLLATE pg_catalog."default" NULL,
    metatags varchar NULL,
    author_id integer NULL,
    author_name character varying(512) COLLATE pg_catalog."default" NULL,
    document_url character varying(512) COLLATE pg_catalog."default" NULL,
    is_abstract boolean NOT NULL,
    created timestamp with time zone NOT NULL,
    created_by_id integer NOT NULL,
    updated timestamp with time zone NOT NULL,
    updated_by_id integer NOT NULL,
    CONSTRAINT profile_type_definition_type_id_304b6874_fk_type_id FOREIGN KEY (type_id)
        REFERENCES public.lookup (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT profile_type_def_parent_id_4ae5e508_fk_parent_id FOREIGN KEY (parent_id)
        REFERENCES public.profile_type_definition (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT prof_type_def_inst_parent_fk_inst_parent_id FOREIGN KEY (instance_parent_id)
        REFERENCES public.profile_type_definition (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT prof_type_def_variable_data_type_fk_variable_data_type_id FOREIGN KEY (variable_data_type_id)
        REFERENCES public.profile_type_definition (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT profile_type_def_created_by_id_75e08c8a_fk_user_id FOREIGN KEY (created_by_id)
        REFERENCES public.user (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT profile_type_def_author_id_75e08c8a_fk_user_id FOREIGN KEY (author_id)
        REFERENCES public.user (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT profile_type_def_updated_by_id_e46b0ae2_fk_user_id FOREIGN KEY (updated_by_id)
        REFERENCES public.user (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    --TBD - NOTE: This will eventually go away in favor of a many to many relationship...
	CONSTRAINT profile_type_def_nodeset_id_304b6874_fk_nodeset_id FOREIGN KEY (profile_id)
        REFERENCES public.profile (id) MATCH SIMPLE
		-- This need to reference the nodeset_file table eventually
		-- REFERENCES public.nodeset_file (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.profile_type_definition
    OWNER to profiledesigner;
/*profile-profiletype-rename-refactor - END*/

---------------------------------------------------------------------
--	Create profile interfaces join table
---------------------------------------------------------------------
--DROP TABLE public.profile_interface;
CREATE TABLE public.profile_interface
(
    id SERIAL PRIMARY KEY,
    profile_type_definition_id integer NOT NULL,
    interface_id integer NOT NULL,
    CONSTRAINT profile_interface_id_uniq UNIQUE (profile_type_definition_id, interface_id),
    CONSTRAINT profile_interface_id_fk_id FOREIGN KEY (interface_id)
        REFERENCES public.profile_type_definition (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT profile_interface_id_fk_profile_type_definition_id FOREIGN KEY (profile_type_definition_id)
        REFERENCES public.profile_type_definition (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.profile_interface
    OWNER to profiledesigner;
-- Index: profile_interfaces_8373b171

-- DROP INDEX public.profile_interfaces_8373b171;

CREATE INDEX profile_interface_8373b171
    ON public.profile_interface USING btree
    (interface_id ASC NULLS LAST)
    TABLESPACE pg_default;
-- Index: profile_interfaces_e8701ad4

-- DROP INDEX public.profile_interfaces_e8701ad4;

CREATE INDEX profile_interface_e8701ad4
    ON public.profile_interface USING btree
    (profile_type_definition_id ASC NULLS LAST)
    TABLESPACE pg_default;

---------------------------------------------------------------------
--	INSERT Profile Interface Join Table
---------------------------------------------------------------------
/* SC - Don't populate test data now that we import from nodesets.	
with _interface (_id) as (select id FROM public.profile WHERE name = 'IEngine')
INSERT INTO public.profile_interface (profile_id, interface_id)
SELECT id, _id FROM public.profile p, _interface WHERE p.name like 'Engine' and profile_type_id = 2;

with _interface (_id) as (select id FROM public.profile WHERE name = 'IElectricEngine')
INSERT INTO public.profile_interface (profile_id, interface_id)
SELECT id, _id FROM public.profile p, _interface WHERE p.name like '%lectric%' and profile_type_id = 3;

with _interface (_id) as (select id FROM public.profile WHERE name = 'ICombustionEngine')
INSERT INTO public.profile_interface (profile_id, interface_id)
SELECT id, _id FROM public.profile p, _interface WHERE p.name like '%ombustion%' and profile_type_id = 3;
*/

---------------------------------------------------------------------
--	Create profile compositions join table
---------------------------------------------------------------------
--DROP TABLE public.profile_composition;
CREATE TABLE public.profile_composition
(
    id SERIAL PRIMARY KEY,
    profile_type_definition_id integer NOT NULL,
    composition_id integer NOT NULL,
    name character varying(256) COLLATE pg_catalog."default" NOT NULL,
	  opc_browse_name character varying(256) NULL,
    opc_node_id character varying(256) NULL, 
    symbolic_name character varying(256) COLLATE pg_catalog."default" NULL,
    metatags varchar NULL,
    document_url character varying(512) COLLATE pg_catalog."default" NULL,
    --namespace character varying(512) COLLATE pg_catalog."default" NULL,
    is_required boolean NULL,
	  modeling_rule character varying(256) NULL,
    is_event boolean NULL,
	  reference_id character varying(256) NULL,
	  reference_is_inverse boolean NULL,
    description character varying COLLATE pg_catalog."default" NULL,
    CONSTRAINT profile_composition_id_fk_id FOREIGN KEY (composition_id)
        REFERENCES public.profile_type_definition (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT profile_composition_id_fk_profile_type_definition_id FOREIGN KEY (profile_type_definition_id)
        REFERENCES public.profile_type_definition (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.profile_composition
    OWNER to profiledesigner;
-- Index: profile_compositions_8373b171

-- DROP INDEX public.profile_compositions_8373b171;

CREATE INDEX profile_composition_8373b171
    ON public.profile_composition USING btree
    (composition_id ASC NULLS LAST)
    TABLESPACE pg_default;
-- Index: profile_compositions_e8701ad4

-- DROP INDEX public.profile_compositions_e8701ad4;

CREATE INDEX profile_composition_e8701ad4
    ON public.profile_composition USING btree
    (profile_type_definition_id ASC NULLS LAST)
    TABLESPACE pg_default;

---------------------------------------------------------------------
--	INSERT Profile composition Join Table
---------------------------------------------------------------------
/* SC - Don't populate test data now that we import from nodesets.	
with _composition (_id) as (select id FROM public.profile WHERE name = 'Starter')
INSERT INTO public.profile_composition (profile_id, composition_id, name)
SELECT id, _id, 'MyStarter' FROM public.profile p, _composition WHERE (p.name = 'Engine');
*/
---------------------------------------------------------------------
--	Create user to profile favorite table. This tracks user favorite
---------------------------------------------------------------------
CREATE TABLE public.profile_type_definition_user_favorite
(
    id SERIAL PRIMARY KEY,
    owner_id integer NOT NULL,
    profile_type_definition_id integer NOT NULL,
    is_favorite boolean not NULL,
    CONSTRAINT ptd_fav_uniq UNIQUE (owner_id, profile_type_definition_id),
    CONSTRAINT ptd_fav_ptd_fk FOREIGN KEY (profile_type_definition_id)
        REFERENCES public.profile_type_definition (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT ptd_fav_user_fk FOREIGN KEY (owner_id)
        REFERENCES public.user (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.profile_type_definition_user_favorite
    OWNER to profiledesigner;


---------------------------------------------------------------------
--	Profile analytics table. This tracks user page visit count, page extend count
---------------------------------------------------------------------
CREATE TABLE public.profile_type_definition_user_analytics
(
    id SERIAL PRIMARY KEY,
    profile_type_definition_id integer NOT NULL,
    page_visit_count integer NULL,
    extend_count integer NULL,
    manual_rank integer NULL,
    CONSTRAINT ptd_analy_uniq UNIQUE (profile_type_definition_id),
    CONSTRAINT ptd_analy_ptd_fk FOREIGN KEY (profile_type_definition_id)
        REFERENCES public.profile_type_definition (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.profile_type_definition_user_analytics
    OWNER to profiledesigner;


---------------------------------------------------------------------
--	TABLE Data TYPE
---------------------------------------------------------------------
--DROP TABLE public.data_type;
CREATE TABLE public.data_type
(
    id SERIAL PRIMARY KEY,
    owner_id integer NULL,  --if a specific user creates a custom type, only visible to that user
    code character varying(200) COLLATE pg_catalog."default" NOT NULL,
    name character varying(200) COLLATE pg_catalog."default" NOT NULL,
    description character varying COLLATE pg_catalog."default" NULL,
    display_order integer NOT NULL,
    use_min_max boolean NOT NULL,
    use_eng_unit boolean NOT NULL,
    is_numeric boolean NOT NULL,
    custom_type_id integer NULL, --optional - FK to profile of type custom data type
    is_active boolean NOT NULL,
    CONSTRAINT data_type_profile_fk FOREIGN KEY (custom_type_id)
        REFERENCES public.profile_type_definition (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT data_type_owner_fk FOREIGN KEY (owner_id)
        REFERENCES public.user (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.data_type
    OWNER to profiledesigner;

---------------------------------------------------------------------
--	TABLE Data TYPE
---------------------------------------------------------------------
--special data types used in front end
INSERT INTO public.data_type(id, code, name, display_order, is_active, use_min_max, use_eng_unit, is_numeric )
SELECT  1, 'composition', 'Composition', 1, true, false, false, false 
;

--manually adjust the identity starting val
SELECT setval('data_type_id_seq', 2);

---------------------------------------------------------------------
---------------------------------------------------------------------
---------------------------------------------------------------------
---------------------------------------------------------------------
---------------------------------------------------------------------

---------------------------------------------------------------------
--	Profile AttributeTable
---------------------------------------------------------------------
-- DROP TABLE public.profile_attribute;
CREATE TABLE public.profile_attribute
(
    id SERIAL PRIMARY KEY,
    data_type_id integer NOT NULL,
	variable_type_definition_id integer NULL,
	data_variable_nodeids character varying NULL,
	profile_type_definition_id integer NULL,
    opc_node_id character varying(256) NULL,
    opc_browse_name character varying(256) NULL,
    symbolic_name character varying(256) COLLATE pg_catalog."default" NULL,
    namespace character varying(512) COLLATE pg_catalog."default" NULL,
    attribute_type_id integer NOT NULL,
    name character varying(256) COLLATE pg_catalog."default" NOT NULL,
    description character varying COLLATE pg_catalog."default" NULL,
    display_name character varying(256) COLLATE pg_catalog."default" NULL,
    min_value numeric NULL,
    max_value numeric NULL,

	instrument_min_value numeric NULL,
	instrument_max_value numeric NULL,
	instrument_range_nodeid character varying(256) NULL,
	instrument_range_modeling_rule character varying(256) NULL,
	instrument_range_access_level integer NULL,
	
	eng_unit_id integer NULL,
	eng_unit_nodeid character varying(256) NULL,
	eng_unit_modeling_rule character varying(256) NULL,
	eng_unit_access_level integer NULL,
	eu_range_nodeid character varying(256) NULL,
	eu_range_modeling_rule character varying(256) NULL,
	eu_range_access_level integer NULL,
	minimum_sampling_interval numeric NULL,
    is_array boolean NOT NULL default(false),
	value_rank integer NULL,
	array_dimensions character varying(256) NULL,
	max_string_length integer NULL,
    is_required boolean NULL,
	modeling_rule character varying(256) NULL,
    enum_value bigint NULL,

	access_level integer NULL,
	access_restrictions integer NULL,
	write_mask integer NULL,
	user_write_mask integer NULL,

	addl_data_json varchar NULL,
    created timestamp with time zone NOT NULL,
    created_by_id integer NOT NULL,
    updated timestamp with time zone NOT NULL,
    updated_by_id integer NOT NULL,
    CONSTRAINT profile_attribute_data_type_id_304b6874_fk_data_type_id FOREIGN KEY (data_type_id)
        REFERENCES public.data_type (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT profile_attr_prof_type_def_id_fk_prof_type_def_id FOREIGN KEY (profile_type_definition_id)
        REFERENCES public.profile_type_definition (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT profile_attr_var_type_def_id_fk_prof_type_def_id FOREIGN KEY (variable_type_definition_id)
        REFERENCES public.profile_type_definition (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT profile_attribute_fk_attr_type_id FOREIGN KEY (attribute_type_id)
        REFERENCES public.lookup (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT profile_attribute_unit_id_4ae5e508_fk_eng_unit FOREIGN KEY (eng_unit_id)
        REFERENCES public.engineering_unit (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT profile_attribute_created_by_id_75e08c8a_fk_user_id FOREIGN KEY (created_by_id)
        REFERENCES public.user (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT profile_attribute_updated_by_id_e46b0ae2_fk_user_id FOREIGN KEY (updated_by_id)
        REFERENCES public.user (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.profile_attribute
    OWNER to profiledesigner;

CREATE INDEX profile_attribute_3f2f3687
    ON public.profile_attribute USING btree
    (data_type_id ASC NULLS LAST)
    TABLESPACE pg_default;

CREATE INDEX profile_attribute_created_by_id_75e08c8a_uniq
    ON public.profile_attribute USING btree
    (created_by_id ASC NULLS LAST)
    TABLESPACE pg_default;

CREATE INDEX profile_attribute_profile_type_definition_id_4ae5e508
    ON public.profile_attribute USING btree
    (profile_type_definition_id ASC NULLS LAST)
    TABLESPACE pg_default;

CREATE INDEX profile_attribute_updated_by_id_e46b0ae2_uniq
    ON public.profile_attribute USING btree
    (updated_by_id ASC NULLS LAST)
    TABLESPACE pg_default;

CREATE INDEX profile_attribute_engUnit_3f2f3687
    ON public.profile_attribute USING btree
    (eng_unit_id ASC NULLS LAST)
    TABLESPACE pg_default;

CREATE INDEX profile_attribute_attribute_type_id
    ON public.profile_attribute USING btree
    (attribute_type_id ASC NULLS LAST)
    TABLESPACE pg_default;
	

---------------------------------------------------------------------
--	Table to allow an admin to rank more common data types and push them higher in the data type selection ui 
---------------------------------------------------------------------
CREATE TABLE public.data_type_rank
(
    id SERIAL PRIMARY KEY,
    data_type_id integer NOT NULL,
    manual_rank integer NULL,
    CONSTRAINT dtr_data_type_id_uniq UNIQUE (data_type_id),
    CONSTRAINT dtr_data_type_id_fk FOREIGN KEY (data_type_id)
        REFERENCES public.data_type (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.data_type_rank
    OWNER to profiledesigner;


---------------------------------------------------------------------
--	View used by lookup data to allow us to get list of data types and rank
---------------------------------------------------------------------

--DROP VIEW IF EXISTS public.v_data_type_rank;

CREATE VIEW public.v_data_type_rank
AS
select 
	--lu.name, 
	--ptd.*, 
	dt.*, 
    baseDt.id as base_data_type_id,
	COALESCE(a.usage_count, 0) + COALESCE(dtr.manual_rank, 0) as popularity_index,
	--create a tiered system to distinguish between very popular and mildly popular and the others
	CASE 
		WHEN COALESCE(a.usage_count, 0) + COALESCE(dtr.manual_rank, 0) > 40 THEN 3
		WHEN COALESCE(a.usage_count, 0) + COALESCE(dtr.manual_rank, 0) > 20 THEN 2
		WHEN COALESCE(a.usage_count, 0) + COALESCE(dtr.manual_rank, 0) > 10 THEN 1
		ELSE 0 END as popularity_level,
	COALESCE(a.usage_count, 0) as usage_count, 
	COALESCE(dtr.manual_rank, 0) as manual_rank
from public.data_type dt
left outer join public.data_type_rank dtr on dtr.data_type_id = dt.id
left outer join public.profile_type_definition ptd on ptd.id = dt.custom_type_id
left outer join public.data_type baseDt on ptd.parent_id = baseDt.custom_type_id
--left outer join public.lookup lu on lu.id = ptd.type_id
left outer join (
	SELECT data_type_id, count(*) as usage_count 
	from public.profile_attribute 
	group by data_type_id
) a on a.data_type_id = dt.id
--order by 
-- 	 COALESCE(a.usage_count, 0) + COALESCE(dtr.manual_rank, 0) desc
-- 	,COALESCE(a.usage_count, 0) desc 
--	,dt.display_order
--	,dt.name
;

ALTER VIEW public.v_data_type_rank
    OWNER to profiledesigner;

	
---------------------------------------------------------------------
--	Table to allow an admin to rank more common engineering units and push them higher in the selection ui 
---------------------------------------------------------------------
CREATE TABLE public.engineering_unit_rank
(
    id SERIAL PRIMARY KEY,
    eng_unit_id integer NOT NULL,
    manual_rank integer NULL,
    CONSTRAINT eur_eng_unit_id_uniq UNIQUE (eng_unit_id),
    CONSTRAINT eur_eng_unit_id_fk FOREIGN KEY (eng_unit_id)
        REFERENCES public.engineering_unit (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.engineering_unit_rank
    OWNER to profiledesigner;


---------------------------------------------------------------------
--	View used by lookup data to allow us to get list of data types and rank
---------------------------------------------------------------------

--DROP VIEW IF EXISTS public.v_engineering_unit_rank;

CREATE VIEW public.v_engineering_unit_rank
AS
select 
	--lu.name, 
	--ptd.*, 
	eu.*, 
	COALESCE(a.usage_count, 0) + COALESCE(eur.manual_rank, 0) as popularity_index,
	--create a tiered system to distinguish between very popular and mildly popular and the others
	CASE 
		WHEN COALESCE(a.usage_count, 0) + COALESCE(eur.manual_rank, 0) > 40 THEN 3
		WHEN COALESCE(a.usage_count, 0) + COALESCE(eur.manual_rank, 0) > 20 THEN 2
		WHEN COALESCE(a.usage_count, 0) + COALESCE(eur.manual_rank, 0) > 10 THEN 1
		ELSE 0 END as popularity_level,
	COALESCE(a.usage_count, 0) as usage_count, 
	COALESCE(eur.manual_rank, 0) as manual_rank
from public.engineering_unit eu
left outer join public.engineering_unit_rank eur on eur.eng_unit_id = eu.id
left outer join (
	SELECT eng_unit_id, count(*) as usage_count 
	from public.profile_attribute 
	group by eng_unit_id
) a on a.eng_unit_id = eu.id
;

ALTER VIEW public.v_engineering_unit_rank
    OWNER to profiledesigner;
	
---------------------------------------------------------------------
--	Import Log Table
---------------------------------------------------------------------
-- DROP TABLE public.import_log;
CREATE TABLE public.import_log
(
    id SERIAL PRIMARY KEY,
    owner_id integer NULL,
    status_id integer NOT NULL,  
    is_active boolean NOT NULL,
    notify_on_complete boolean NOT NULL,
    created timestamp with time zone NOT NULL,
    updated timestamp with time zone NOT NULL,
    completed timestamp with time zone NULL,
    CONSTRAINT import_log_status_id_304b6874_fk FOREIGN KEY (status_id)
        REFERENCES public.lookup (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT import_log_owner_id_75e08c8a_fk_user_id FOREIGN KEY (owner_id)
        REFERENCES public.user (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.import_log
    OWNER to profiledesigner;

	
-- DROP TABLE public.import_log_message;
CREATE TABLE public.import_log_message
(
    id SERIAL PRIMARY KEY,
    import_log_id integer NOT NULL,  
    message character varying NOT NULL,
    created timestamp with time zone NOT NULL,
    CONSTRAINT import_log_import_log_id_304b6874_fk FOREIGN KEY (import_log_id)
        REFERENCES public.import_log (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.import_log_message
    OWNER to profiledesigner;

---------------------------------------------------------------------
--	Import log profile warning message
---------------------------------------------------------------------
-- DROP TABLE public.import_log_warning;
CREATE TABLE public.import_log_warning
(
    id SERIAL PRIMARY KEY,
    import_log_id integer NOT NULL,  
    profile_id integer NOT NULL,  
    message character varying NOT NULL,
    created timestamp with time zone NOT NULL,
    CONSTRAINT import_warning_import_log_id_fk FOREIGN KEY (import_log_id)
        REFERENCES public.import_log (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
    ,CONSTRAINT import_log_warning_profile_id_fk FOREIGN KEY (profile_id)
        REFERENCES public.profile (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.import_log_warning
    OWNER to profiledesigner;

---------------------------------------------------------------------
--	New Table - import file - child table to import_action (formerly import_log)
---------------------------------------------------------------------
-- DROP TABLE public.import_file;
CREATE TABLE public.import_file
(
    id SERIAL PRIMARY KEY,
    import_id integer NOT NULL,  
    file_name character varying NOT NULL,
    total_chunks integer NOT NULL,
    total_bytes bigint NOT NULL,
    CONSTRAINT import_import_id_fk FOREIGN KEY (import_id)
        REFERENCES public.import_log (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.import_file
    OWNER to profiledesigner;
---------------------------------------------------------------------
--	New Table - import file chunk - child table to import_file
---------------------------------------------------------------------
-- DROP TABLE public.import_file_chunk;
CREATE TABLE public.import_file_chunk
(
    id SERIAL PRIMARY KEY,
    import_file_id integer NOT NULL,  
    chunk_order integer NOT NULL,
    contents text NULL,
    CONSTRAINT import_file_import_file_id_fk FOREIGN KEY (import_file_id)
        REFERENCES public.import_file (id) MATCH SIMPLE
        ON UPDATE NO ACTION
        ON DELETE NO ACTION
        DEFERRABLE INITIALLY DEFERRED
)

TABLESPACE pg_default;

ALTER TABLE public.import_file_chunk
    OWNER to profiledesigner;
---------------------------------------------------------------------
--	Delete a nodeset and all of its children
---------------------------------------------------------------------

drop procedure if exists public.sp_nodeset_delete;
create procedure public.sp_nodeset_delete(
   _idList varchar
)
language plpgsql    
as $$
begin
	--
	delete from public.profile_type_definition_user_analytics
	--SELECT * FROM public.profile_type_definition_user_analytics
	WHERE profile_type_definition_id in (
		SELECT ptd.id 
		FROM public.profile_type_definition ptd 
		JOIN public.profile p on p.id = ptd.profile_id
			AND p.id IN (select cast(unnest(string_to_array(_idList, ',')) as int)) 
	);

	--
	delete from public.profile_type_definition_user_favorite
	--SELECT * FROM public.profile_type_definition_user_favorite
	WHERE profile_type_definition_id in (
		SELECT ptd.id 
		FROM public.profile_type_definition ptd 
		JOIN public.profile p on p.id = ptd.profile_id
			AND p.id IN (select cast(unnest(string_to_array(_idList, ',')) as int)) 
	);

	--
	delete from public.profile_attribute
	--SELECT * FROM public.profile_attribute
	WHERE profile_type_definition_id in (
		SELECT ptd.id 
		FROM public.profile_type_definition ptd 
		JOIN public.profile p on p.id = ptd.profile_id
			AND p.id IN (select cast(unnest(string_to_array(_idList, ',')) as int)) 
	);

	delete from public.data_type
	--SELECT * FROM public.data_type
	WHERE custom_type_id in (
		SELECT ptd.id 
		FROM public.profile_type_definition ptd 
		JOIN public.profile p on p.id = ptd.profile_id
			AND p.id IN (select cast(unnest(string_to_array(_idList, ',')) as int))
	);

	delete from public.profile_composition
	--SELECT * FROM public.profile_composition
	WHERE profile_type_definition_id in (
		SELECT ptd.id 
		FROM public.profile_type_definition ptd 
		JOIN public.profile p on p.id = ptd.profile_id
			AND p.id IN (select cast(unnest(string_to_array(_idList, ',')) as int))
	);

	delete from public.profile_interface
	--SELECT * FROM public.profile_interface
	WHERE profile_type_definition_id in (
		SELECT ptd.id 
		FROM public.profile_type_definition ptd
		JOIN public.profile p on p.id = ptd.profile_id
			AND p.id IN (select cast(unnest(string_to_array(_idList, ',')) as int))
	);

	delete from public.profile_type_definition
	--SELECT * FROM public.profile_type_definition
	WHERE profile_id in (
		SELECT id FROM public.profile p
		WHERE p.id IN (select cast(unnest(string_to_array(_idList, ',')) as int))
	);

	delete from public.profile_nodeset_file
	--SELECT * FROM public.profile_nodeset_file
	WHERE profile_id in (
		SELECT id FROM public.profile p
		WHERE p.id IN (select cast(unnest(string_to_array(_idList, ',')) as int))
	);

  	delete from public.nodeset_file f
	--SELECT * FROM public.nodeset_file f
	WHERE NOT EXISTS (
		SELECT id FROM public.profile_nodeset_file pf
		WHERE f.id = pf.nodeset_file_id
	);

	delete from public.import_log_warning
	--SELECT * FROM public.import_log_warning
	WHERE profile_id in (
		SELECT id FROM public.profile p
		WHERE p.id IN (select cast(unnest(string_to_array(_idList, ',')) as int))
	);

	delete from public.profile_additional_properties
	--SELECT * FROM public.profile
	WHERE profile_id IN (select cast(unnest(string_to_array(_idList, ',')) as int))
	;

	delete from public.profile
	--SELECT * FROM public.profile
	WHERE id IN (select cast(unnest(string_to_array(_idList, ',')) as int))
	;
    commit;
end;$$
;


/*---------------------------------------------------------
	Function: fn_profile_get_owner
---------------------------------------------------------*/
drop function if exists fn_profile_get_owner; 
create function fn_profile_get_owner (
   IN _id int, _ownerId int) 
/*
	Function: fn_profile_get_owner
	Who: scoxen
	When: 2023-03-27
	Description: 
	Determine if this profile is owned by this ownerId. 
	Return user
*/
returns table ( 
	id integer, 
	objectid_aad character varying(100) 
) 
language plpgsql
as $$
declare 
-- variable declaration
begin
	-- body
	return query
		SELECT u.id, u.objectid_aad  
		FROM public.profile p
		--determine ownership
		--has no cloudlibraryid and author id = passed in user id - owned by user
		--has cloudlibraryid and cloud library pending approval is true - owned by user
		--note Cloud library fields are managed and maintained by the API updating its values based on current
		--Cloud Lib (separate system). If there was any mismatch in values between this and CloudLib, it would be uncommon and updated through 
		--the normal course of visiting pages within the application.
		LEFT OUTER JOIN public.user u ON u.id = p.author_id AND
			(p.cloud_library_id IS NULL OR
			(p.cloud_library_id IS NOT NULL AND 
				COALESCE(p.cloud_library_pending_approval, FALSE) IS TRUE))
		WHERE 
			 p.id = _id AND
			(p.owner_id IS NULL --root nodesets
			 OR p.owner_id = _ownerId)  --my nodesets or nodesets I imported
	;
end; $$ 
;

/*---------------------------------------------------------
	Function: fn_profile_type_definition_get_descendants
---------------------------------------------------------*/
drop function if exists fn_profile_type_definition_get_descendants; 
create function fn_profile_type_definition_get_descendants (
   IN _id int, _ownerId int, _limitByType boolean, _excludeAbstract boolean
) 
/*
	Function: fn_profile_type_definition_get_descendants
	Who: scoxen
	When: 2023-01-31
	Description: 
	Get a list of type defs which are descendants of this 
	type def. This is a recursive query.
	This also considers ownership. It will consider type defs associated with 
	standard nodeset profiles or profiles this owner has created.
	Notes:
		allow for ordered, paged results
*/
returns table ( 
	id integer, 
	browse_name character varying(256), 
	description character varying, 
	is_abstract boolean, 
	name character varying(512), 
	opc_node_id character varying(100), 
	parent_id integer, 
	type_id integer, 
	type_name character varying(256), 
    variable_data_type_id integer,
	profile_id integer, 
	profile_author_id integer, 
	profile_author_objectid_aad character varying(100), 
	profile_namespace character varying(400), 
	profile_owner_id integer, 
	profile_publish_date timestamp with time zone, 
	profile_title text, 
	profile_version character varying(25), 
	level integer
) 
language plpgsql
as $$
declare 
-- variable declaration
begin
	-- body
	return query
	WITH RECURSIVE descendant AS (
		SELECT  t.id,
				CAST(0 as integer) AS level
		FROM public.profile_type_definition t
		JOIN public.profile p on p.id = t.profile_id
		WHERE t.id = _id

		UNION ALL

		SELECT  t.id,
				CAST(d.level + 1 as integer) as level
		FROM public.profile_type_definition t
		JOIN public.profile p on p.id = t.profile_id
		JOIN descendant d ON t.parent_id = d.id
		WHERE
			(p.owner_id IS NULL) --root nodesets
			OR (p.owner_id = _ownerId)  --my nodesets or nodesets I imported
	)

	SELECT  d.id,
			t.browse_name,
			t.description,
			t.is_abstract,
			t.name,
			t.opc_node_id,
			t.parent_id,
			t.type_id,
			l.name as type_name,
			t.variable_data_type_id,
			p.id as profile_id,
			u.id as profile_author_id,
			u.objectid_aad as profile_author_objectid_aad,
			p.namespace as profile_namespace,
			p.owner_id as profile_owner_id,
			p.publish_date as profile_publish_date,
			p.title as profile_title,
			p.version as profile_version,
			d.level
	FROM descendant d
	JOIN public.profile_type_definition t ON d.id = t.id 
		AND t.id <> _id
	JOIN public.profile p ON p.id = t.profile_id
	JOIN public.lookup l ON l.id = t.type_id
	--determine ownership
	LEFT OUTER JOIN public.fn_profile_get_owner(p.id, _ownerId) u on  u.id = p.author_id
	WHERE 
	--rule: always exclude object and method
	l.id NOT IN (11, 20)   
	--optional parameters
	AND 1 = (CASE WHEN _limitByType = false THEN 1
		 WHEN _limitByType = true AND t.type_id IN (SELECT t1.type_id FROM public.profile_type_definition t1 WHERE t1.id = _id) THEN 1
		 ELSE 0 END)
	--optional parameters
	AND 1 = (CASE WHEN _excludeAbstract = false THEN 1
		 WHEN _excludeAbstract = true AND t.is_abstract = false THEN 1
		 ELSE 0 END)
	;
end; $$ 
;


/*---------------------------------------------------------
	Function: fn_profile_type_definition_get_dependencies
---------------------------------------------------------*/
drop function if exists fn_profile_type_definition_get_dependencies; 
create function fn_profile_type_definition_get_dependencies (
   IN _id int, _ownerId int, _limitByType boolean, _excludeAbstract boolean
) 
/*
	Function: fn_profile_type_definition_get_dependencies
	Who: scoxen
	When: 2023-01-31
	Description: 
	Get a list of type defs which are descendants OR dependencies of this 
	type def. Call the descendants function to get those items. 
	Then call union queries to find the dependencies based on compositions or data type usage
*/
returns table ( 
	id integer, 
	browse_name character varying(256), 
	description character varying, 
	is_abstract boolean, 
	name character varying(512), 
	opc_node_id character varying(100), 
	parent_id integer, 
	type_id integer, 
	type_name character varying(256), 
    variable_data_type_id integer,
	profile_id integer, 
	profile_author_id integer, 
	profile_author_objectid_aad character varying(100), 
	profile_namespace character varying(400), 
	profile_owner_id integer, 
	profile_publish_date timestamp with time zone, 
	profile_title text, 
	profile_version character varying(25), 
	level integer
) 
language plpgsql
as $$
declare 
-- variable declaration
begin
	-- body
	return query

	WITH dependants AS (
		--union with type defs that use this type def as a parent
		SELECT  t.id,
				CAST(1 as integer) AS level
		FROM public.profile_type_definition t 
		WHERE t.parent_id = _id
		UNION
		--union with type defs that use this profile as a composition
		SELECT  t.id,
				CAST(1 as integer) AS level
		FROM public.profile_type_definition t 
		JOIN public.profile_composition c on c.profile_type_definition_id = t.id AND c.composition_id = _id
		JOIN public.profile p ON p.id = t.profile_id
		WHERE 
			(p.owner_id IS NULL) --root nodesets
			OR (p.owner_id = _ownerId)  --my nodesets or nodesets I imported
		
		UNION
		--union with type defs that use this profile as an interface
		SELECT  t.id,
				CAST(1 as integer) AS level
		FROM public.profile_type_definition t 
		JOIN public.profile_interface i on i.profile_type_definition_id = t.id AND i.interface_id = _id
		JOIN public.profile p ON p.id = t.profile_id
		WHERE 
			(p.owner_id IS NULL) --root nodesets
			OR (p.owner_id = _ownerId)  --my nodesets or nodesets I imported
		
		UNION
		--union with type defs that have attributes that use a data type which points to a profile type def (2nd level)
		SELECT  t.id,
				CAST(2 as integer) AS level
		FROM public.profile_type_definition t 
		JOIN public.profile p ON p.id = t.profile_id
		WHERE 
			((p.owner_id IS NULL) --root nodesets
			OR (p.owner_id = _ownerId)) AND  --my nodesets or nodesets I imported
			t.id IN (
			SELECT distinct(t.id) -- , t.name, a.name, d.* 
			FROM public.profile_attribute a
			JOIN public.data_type d on d.id = a.data_type_id
			JOIN public.profile_type_definition t on t.id = a.profile_type_definition_id
			WHERE d.custom_type_id = _id
		)

	)

	--adding extra wrapping sql statement to get the distinct values
	--a type def could be a dependency in multiple scenarios
	SELECT
			dFinal.id,
			dFinal.browse_name,
			dFinal.description,
			dFinal.is_abstract,
			dFinal.name,
			dFinal.opc_node_id,
			dFinal.parent_id,
			dFinal.type_id,
			dFinal.type_name,
			dFinal.variable_data_type_id,
			dFinal.profile_id,
			dFinal.profile_author_id,
			dFinal.profile_author_objectid_aad,
			dFinal.profile_namespace,
			dFinal.profile_owner_id,
			dFinal.profile_publish_date,
			dFinal.profile_title,
			dFinal.profile_version,
			min(dFinal.level) as level
	FROM (SELECT  d.id,
			t.browse_name,
			t.description,
			t.is_abstract,
			t.name,
			t.opc_node_id,
			t.parent_id,
			t.type_id,
			l.name as type_name,
			t.variable_data_type_id,
			p.id as profile_id,
			u.id as profile_author_id,
			u.objectid_aad as profile_author_objectid_aad,
			p.namespace as profile_namespace,
			p.owner_id as profile_owner_id,
			p.publish_date as profile_publish_date,
			p.title as profile_title,
			p.version as profile_version,
			d.level
	FROM dependants d
	JOIN public.profile_type_definition t ON d.id = t.id AND t.id <> _id
	JOIN public.profile p ON p.id = t.profile_id
	JOIN public.lookup l ON l.id = t.type_id
	--determine ownership
	LEFT OUTER JOIN public.fn_profile_get_owner(p.id, _ownerId) u on  u.id = p.author_id
	WHERE 
	--rule: always exclude object and method
	l.id NOT IN (11, 20)   
	--optional parameters
	AND 1 = (CASE WHEN _limitByType = false THEN 1
		 WHEN _limitByType = true AND t.type_id IN (SELECT t1.type_id FROM public.profile_type_definition t1 WHERE t1.id = _id) THEN 1
		 ELSE 0 END)
	--optional parameters
	AND 1 = (CASE WHEN _excludeAbstract = false THEN 1
		 WHEN _excludeAbstract = true AND t.is_abstract = false THEN 1
		 ELSE 0 END)
	UNION 
	SELECT * FROM public.fn_profile_type_definition_get_descendants(_id, _ownerId, _limitByType, _excludeAbstract)
	) as dFinal
	group by 
			dFinal.id,
			dFinal.browse_name,
			dFinal.description,
			dFinal.is_abstract,
			dFinal.name,
			dFinal.opc_node_id,
			dFinal.parent_id,
			dFinal.type_id,
			dFinal.type_name,
			dFinal.variable_data_type_id,
			dFinal.profile_id,
			dFinal.profile_author_id,
			dFinal.profile_author_objectid_aad,
			dFinal.profile_namespace,
			dFinal.profile_owner_id,
			dFinal.profile_publish_date,
			dFinal.profile_title,
			dFinal.profile_version	
	;
end; $$ 
;

/*---------------------------------------------------------
	Function: fn_profile_type_definition_get_ancestors
---------------------------------------------------------*/
drop function if exists fn_profile_type_definition_get_ancestors; 
create function fn_profile_type_definition_get_ancestors (
   IN _id int, _ownerId int
) 
/*
	Function: fn_profile_type_definition_get_ancestors
	Who: scoxen
	When: 2023-01-31
	Description: 
	Get a list of type defs which are parents, grandparents of this 
	type def. 
*/
returns table ( 
	id integer, 
	browse_name character varying(256), 
	description character varying, 
	is_abstract boolean, 
	name character varying(512), 
	opc_node_id character varying(100), 
	parent_id integer, 
	type_id integer, 
	type_name character varying(256), 
    variable_data_type_id integer,
	profile_id integer, 
	profile_author_id integer, 
	profile_author_objectid_aad character varying(100), 
	profile_namespace character varying(400), 
	profile_owner_id integer, 
	profile_publish_date timestamp with time zone, 
	profile_title text, 
	profile_version character varying(25), 
	level integer
) 
language plpgsql
as $$
declare 
-- variable declaration
begin
	-- body
	return query
	WITH RECURSIVE ancestor AS (
		SELECT  t.id,
				t.parent_id,
				CAST(0 as integer) AS level
		FROM public.profile_type_definition t
		JOIN public.profile p on p.id = t.profile_id
		WHERE t.id = _id

		UNION ALL

		SELECT  t.id,
				t.parent_id,
				CAST(d.level - 1 as integer) as level
		FROM public.profile_type_definition t
		JOIN public.profile p on p.id = t.profile_id
		JOIN ancestor d ON d.parent_id = t.id
		--WHERE p.cloud_library_id IS NOT NULL OR p.owner_id = _ownerId
	)

	SELECT  d.id,
			t.browse_name,
			t.description,
			t.is_abstract,
			t.name,
			t.opc_node_id,
			t.parent_id,
			t.type_id,
			l.name as type_name,
			t.variable_data_type_id,
			p.id as profile_id,
			u.id as profile_author_id,
			u.objectid_aad as profile_author_objectid_aad,
			p.namespace as profile_namespace,
			p.owner_id as profile_owner_id,
			p.publish_date as profile_publish_date,
			p.title as profile_title,
			p.version as profile_version,
			d.level
	FROM ancestor d
	JOIN public.profile_type_definition t ON d.id = t.id --AND t.id <> _id --include item itself
	JOIN public.profile p ON p.id = t.profile_id
	JOIN public.lookup l ON l.id = t.type_id
	--determine ownership
	LEFT OUTER JOIN public.fn_profile_get_owner(p.id, _ownerId) u on  u.id = p.author_id
	order by d.level, t.name
	;
end; $$ 


---------------------------------------------------------------------
---------------------------------------------------------------------
---------------------------------------------------------------------
---------------------------------------------------------------------
---------------------------------------------------------------------
---------------------------------------------------------------------

