---------------------------------------------------------------------
--  Profile Designer DB - Update
--	Date: 2025-07-03
--	Who: DavidW
--	Details:
--	Add user deletion stored procedures for complete user cleanup
---------------------------------------------------------------------

---------------------------------------------------------------------
--  Delete a user and all of its dependencies (CASCADE DELETE)
---------------------------------------------------------------------
drop procedure if exists public.sp_user_delete;
create procedure public.sp_user_delete(
   _userId integer
)
language plpgsql    
as $$
declare
    record_count integer;
    total_deleted integer := 0;
begin
    -- Step 1: Handle import system dependencies (deepest level first)
    
    -- Delete import_file_chunks
    delete from public.import_file_chunk 
    where import_file_id in (
        select if.id from public.import_file if
        join public.import_log il on if.import_id = il.id
        where il.owner_id = _userId
    );
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % import_file_chunk records', record_count;
    
    -- Delete import_files
    delete from public.import_file 
    where import_id in (
        select id from public.import_log where owner_id = _userId
    );
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % import_file records', record_count;
    
    -- Delete import_log_messages
    delete from public.import_log_message 
    where import_log_id in (
        select id from public.import_log where owner_id = _userId
    );
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % import_log_message records', record_count;
    
    -- Delete import_log_warnings  
    delete from public.import_log_warning 
    where import_log_id in (
        select id from public.import_log where owner_id = _userId
    );
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % import_log_warning records', record_count;
    
    -- Delete import_logs
    delete from public.import_log where owner_id = _userId;
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % import_log records', record_count;
    
    -- Step 2: Handle profile_attribute dependencies
    delete from public.profile_attribute 
    where data_type_id in (
        select id from public.data_type where owner_id = _userId
    );
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % profile_attribute records (via data_type)', record_count;
    
    -- Delete profile_attributes created/updated by user
    delete from public.profile_attribute 
    where created_by_id = _userId or updated_by_id = _userId;
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % profile_attribute records (created/updated by user)', record_count;
    
    -- Delete data_types
    delete from public.data_type where owner_id = _userId;
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % data_type records', record_count;
    
    -- Step 3: Handle profile_type_definition hierarchical dependencies
    
    -- Clear hierarchical relationships first
    update public.profile_type_definition 
    set instance_parent_id = null 
    where instance_parent_id in (
        select id from public.profile_type_definition 
        where created_by_id = _userId or updated_by_id = _userId or author_id = _userId
    );
    GET DIAGNOSTICS record_count = ROW_COUNT;
    RAISE NOTICE 'Cleared % instance_parent_id relationships', record_count;
    
    update public.profile_type_definition 
    set parent_id = null 
    where parent_id in (
        select id from public.profile_type_definition 
        where created_by_id = _userId or updated_by_id = _userId or author_id = _userId
    );
    GET DIAGNOSTICS record_count = ROW_COUNT;
    RAISE NOTICE 'Cleared % parent_id relationships', record_count;
    
    -- Delete profile_type_definition dependencies following sp_nodeset_delete pattern
    delete from public.profile_type_definition_user_analytics
    where profile_type_definition_id in (
        select id from public.profile_type_definition 
        where created_by_id = _userId or updated_by_id = _userId or author_id = _userId
    );
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % profile_type_definition_user_analytics records', record_count;
    
    delete from public.profile_type_definition_user_favorite 
    where owner_id = _userId;
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % profile_type_definition_user_favorite records', record_count;
    
    delete from public.profile_interface 
    where profile_type_definition_id in (
        select id from public.profile_type_definition 
        where created_by_id = _userId or updated_by_id = _userId or author_id = _userId
    );
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % profile_interface records', record_count;
    
    delete from public.profile_composition 
    where profile_type_definition_id in (
        select id from public.profile_type_definition 
        where created_by_id = _userId or updated_by_id = _userId or author_id = _userId
    );
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % profile_composition records', record_count;
    
    -- Delete profile_type_definitions
    delete from public.profile_type_definition 
    where created_by_id = _userId or updated_by_id = _userId or author_id = _userId;
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % profile_type_definition records', record_count;
    
    -- Step 4: Handle profile dependencies and additional properties
    delete from public.profile_additional_properties
    where profile_id in (
        select id from public.profile 
        where created_by_id = _userId or updated_by_id = _userId or author_id = _userId or owner_id = _userId
    );
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % profile_additional_properties records', record_count;
    
    -- Delete profiles
    delete from public.profile 
    where created_by_id = _userId or updated_by_id = _userId or author_id = _userId or owner_id = _userId;
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % profile records', record_count;
    
    -- Step 5: Handle nodeset_file dependencies
    delete from public.nodeset_file where imported_by_id = _userId or owner_id = _userId;
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    RAISE NOTICE 'Deleted % nodeset_file records', record_count;
    
    -- Step 6: Finally delete the user
    delete from public."user" where id = _userId;
    GET DIAGNOSTICS record_count = ROW_COUNT;
    total_deleted := total_deleted + record_count;
    
    if record_count = 1 then
        RAISE NOTICE 'Successfully deleted user %', _userId;
    else
        RAISE WARNING 'User % not found or not deleted', _userId;
    end if;
    
    RAISE NOTICE 'Total records deleted: %', total_deleted;
    
    commit;
end;$$

---------------------------------------------------------------------
--  Count user dependencies (DRY RUN) - Function to preview what would be deleted
---------------------------------------------------------------------
drop function if exists public.fn_user_dependencies_count;
create function public.fn_user_dependencies_count(
   _userId integer
)
/*
    Function: fn_user_dependencies_count
    Who: DavidW
    When: 2025-07-03
    Description: 
    Count all dependencies for a user to preview what would be deleted.
    This provides a dry-run capability before executing sp_user_delete.
    
    Usage: SELECT * FROM public.fn_user_dependencies_count(243);
*/
returns table (
    dependency_type character varying(100),
    record_count integer
)
language plpgsql
as $$
declare 
    temp_count integer;
begin
    -- Count import_file_chunks
    select count(*) into temp_count
    from public.import_file_chunk 
    where import_file_id in (
        select if.id from public.import_file if
        join public.import_log il on if.import_id = il.id
        where il.owner_id = _userId
    );
    return query select 'import_file_chunk'::character varying(100), temp_count;
    
    -- Count import_files
    select count(*) into temp_count
    from public.import_file 
    where import_id in (
        select id from public.import_log where owner_id = _userId
    );
    return query select 'import_file'::character varying(100), temp_count;
    
    -- Count import_log_messages
    select count(*) into temp_count
    from public.import_log_message 
    where import_log_id in (
        select id from public.import_log where owner_id = _userId
    );
    return query select 'import_log_message'::character varying(100), temp_count;
    
    -- Count import_log_warnings
    select count(*) into temp_count
    from public.import_log_warning 
    where import_log_id in (
        select id from public.import_log where owner_id = _userId
    );
    return query select 'import_log_warning'::character varying(100), temp_count;
    
    -- Count import_logs
    select count(*) into temp_count 
    from public.import_log where owner_id = _userId;
    return query select 'import_log'::character varying(100), temp_count;
    
    -- Count profile_attributes via data_type
    select count(*) into temp_count
    from public.profile_attribute 
    where data_type_id in (
        select id from public.data_type where owner_id = _userId
    );
    return query select 'profile_attribute (via data_type)'::character varying(100), temp_count;
    
    -- Count profile_attributes created/updated by user
    select count(*) into temp_count
    from public.profile_attribute 
    where created_by_id = _userId or updated_by_id = _userId;
    return query select 'profile_attribute (created/updated)'::character varying(100), temp_count;
    
    -- Count data_types
    select count(*) into temp_count 
    from public.data_type where owner_id = _userId;
    return query select 'data_type'::character varying(100), temp_count;
    
    -- Count profile_type_definition_user_analytics
    select count(*) into temp_count
    from public.profile_type_definition_user_analytics
    where profile_type_definition_id in (
        select id from public.profile_type_definition 
        where created_by_id = _userId or updated_by_id = _userId or author_id = _userId
    );
    return query select 'profile_type_definition_user_analytics'::character varying(100), temp_count;
    
    -- Count profile_type_definition_user_favorites
    select count(*) into temp_count 
    from public.profile_type_definition_user_favorite where owner_id = _userId;
    return query select 'profile_type_definition_user_favorite'::character varying(100), temp_count;
    
    -- Count profile_interfaces
    select count(*) into temp_count
    from public.profile_interface 
    where profile_type_definition_id in (
        select id from public.profile_type_definition 
        where created_by_id = _userId or updated_by_id = _userId or author_id = _userId
    );
    return query select 'profile_interface'::character varying(100), temp_count;
    
    -- Count profile_compositions
    select count(*) into temp_count
    from public.profile_composition 
    where profile_type_definition_id in (
        select id from public.profile_type_definition 
        where created_by_id = _userId or updated_by_id = _userId or author_id = _userId
    );
    return query select 'profile_composition'::character varying(100), temp_count;
    
    -- Count profile_type_definitions
    select count(*) into temp_count
    from public.profile_type_definition 
    where created_by_id = _userId or updated_by_id = _userId or author_id = _userId;
    return query select 'profile_type_definition'::character varying(100), temp_count;
    
    -- Count profile_additional_properties
    select count(*) into temp_count
    from public.profile_additional_properties
    where profile_id in (
        select id from public.profile 
        where created_by_id = _userId or updated_by_id = _userId or author_id = _userId or owner_id = _userId
    );
    return query select 'profile_additional_properties'::character varying(100), temp_count;
    
    -- Count profiles
    select count(*) into temp_count
    from public.profile 
    where created_by_id = _userId or updated_by_id = _userId or author_id = _userId or owner_id = _userId;
    return query select 'profile'::character varying(100), temp_count;
    
    -- Count nodeset_files
    select count(*) into temp_count 
    from public.nodeset_file where imported_by_id = _userId or owner_id = _userId;
    return query select 'nodeset_file'::character varying(100), temp_count;
    
    -- Count the user itself
    select count(*) into temp_count 
    from public."user" where id = _userId;
    return query select 'user'::character varying(100), temp_count;
    
end; $$

---------------------------------------------------------------------
--  Usage Examples:
---------------------------------------------------------------------
/*
-- Dry run to see what would be deleted:
SELECT * FROM public.fn_user_dependencies_count(243) 
ORDER BY dependency_type;

-- Get total count of all dependencies:
SELECT SUM(record_count) as total_dependencies 
FROM public.fn_user_dependencies_count(243);

-- Execute the actual deletion:
CALL public.sp_user_delete(243);
*/