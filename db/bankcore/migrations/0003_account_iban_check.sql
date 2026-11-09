SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- Replaces the IBAN check created in 0002_core.sql. That script has already
-- been applied, and an applied script is never edited (ADR-010), so the fix
-- is this new script: drop the old check and add the corrected one.
--
-- The rule itself is unchanged: AE, two check digits, bank code 999, sixteen
-- digits, and the mod-97 remainder must be 1. Two holes are closed:
--
-- (a) A check constraint refuses a row only when its condition is false. When
--     the condition is unknown (NULL), the row is let through. In the old
--     version, text that could not be turned into a number made TRY_CAST
--     return NULL, so the whole condition became unknown and the row was
--     accepted. ISNULL(..., 0) turns that case into remainder 0, which is not
--     1, so the row is definitely refused.
--
-- (b) The old pattern followed the database's collation. This server's
--     collation ignores case, so a lower-case "ae" passed; and under some
--     collations [0-9] also matches characters such as the superscript two.
--     Latin1_General_BIN2 compares the raw characters, so the pattern is
--     exact: only the capital letters AE and only the ten digits 0 to 9,
--     whatever collation the server uses.
--
-- WITH CHECK makes SQL Server test every existing row against the new rule,
-- so the constraint is trusted from the start.

ALTER TABLE core.account DROP CONSTRAINT ck_account_iban;

ALTER TABLE core.account WITH CHECK ADD CONSTRAINT ck_account_iban CHECK (
    iban COLLATE Latin1_General_BIN2 LIKE 'AE' + REPLICATE('[0-9]', 21)
    AND SUBSTRING(iban, 5, 3) = '999'
    AND ISNULL(TRY_CAST(SUBSTRING(iban, 5, 19) + '1014' + SUBSTRING(iban, 3, 2) AS DECIMAL(38,0)) % 97, 0) = 1
);
GO
