CREATE OR REPLACE FUNCTION {databaseSchema}.mt_patch_doc(doc jsonb, patch jsonb) RETURNS jsonb AS $$
DECLARE
	current_value bigint;
	next_value bigint;
BEGIN
    return '{}'::jsonb;
END
$$ LANGUAGE plpgsql;
