SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- Customers, their personal details, products and accounts (blueprint 7.1),
-- with every rule a table definition can carry enforced here. Rules that need
-- application logic, such as the KYC gate (BR-01), come later (LL-007).
--
-- Naming: pk_ primary key, fk_ foreign key, uq_ unique constraint, ck_ check,
-- df_ default, ux_ unique index, tr_ trigger, _seq sequence.

-- core.customer: the customer profile, with no personal data. Names, dates
-- of birth and contact details live in pii.customer_identity.
CREATE TABLE core.customer (
    customer_id  BIGINT IDENTITY(1,1) NOT NULL,
    persona_code VARCHAR(10) NOT NULL,
    home_emirate CHAR(3)     NOT NULL,
    kyc_status   VARCHAR(10) NOT NULL,
    onboarded_on DATE        NOT NULL, -- business date from the caller, so no default
    status       VARCHAR(10) NOT NULL,
    CONSTRAINT pk_customer PRIMARY KEY (customer_id),
    -- The four personas of the business plan (section 3).
    CONSTRAINT ck_customer_persona_code CHECK (persona_code IN ('STARTER', 'BUILDER', 'PROVIDER', 'INVESTOR')),
    -- The seven emirates: Abu Dhabi, Dubai, Sharjah, Ajman, Umm Al Quwain,
    -- Ras Al Khaimah, Fujairah.
    CONSTRAINT ck_customer_home_emirate CHECK (home_emirate IN ('AUH', 'DXB', 'SHJ', 'AJM', 'UAQ', 'RAK', 'FUJ')),
    CONSTRAINT ck_customer_kyc_status   CHECK (kyc_status IN ('PENDING', 'PASSED', 'FAILED')),
    CONSTRAINT ck_customer_status       CHECK (status IN ('ACTIVE', 'CLOSED'))
);
GO

-- pii.customer_identity: personal data, one row per customer. Change data
-- capture is never enabled on the pii schema, so this never leaves BankCore.
CREATE TABLE pii.customer_identity (
    customer_id           BIGINT         NOT NULL,
    full_name             NVARCHAR(200)  NOT NULL,
    date_of_birth         DATE           NOT NULL,
    national_id_synthetic VARCHAR(20)    NOT NULL, -- synthetic, never a real Emirates ID
    phone                 VARCHAR(20)    NULL,
    email                 NVARCHAR(254)  NULL,
    address               NVARCHAR(400)  NULL,
    CONSTRAINT pk_customer_identity PRIMARY KEY (customer_id),
    CONSTRAINT fk_customer_identity_customer FOREIGN KEY (customer_id) REFERENCES core.customer (customer_id),
    CONSTRAINT uq_customer_identity_national_id_synthetic UNIQUE (national_id_synthetic)
);
GO

-- pii.token_vault: maps each internal ID to the random token the warehouse
-- sees in its place (blueprint 10). The vault itself never leaves BankCore.
CREATE TABLE pii.token_vault (
    entity_type VARCHAR(10)      NOT NULL,
    entity_id   BIGINT           NOT NULL,
    token       UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_token_vault_token DEFAULT NEWID(),
    CONSTRAINT pk_token_vault PRIMARY KEY (entity_type, entity_id),
    -- CARD is listed now so the vault does not change when cards arrive (LL-017).
    CONSTRAINT ck_token_vault_entity_type CHECK (entity_type IN ('CUSTOMER', 'ACCOUNT', 'CARD')),
    CONSTRAINT uq_token_vault_token UNIQUE (token)
);
GO

-- core.product: what an account can be.
CREATE TABLE core.product (
    product_code     VARCHAR(20) NOT NULL,
    product_type     VARCHAR(10) NOT NULL,
    currency_code    CHAR(3)     NOT NULL,
    interest_bearing BIT         NOT NULL,
    CONSTRAINT pk_product PRIMARY KEY (product_code),
    CONSTRAINT ck_product_product_type  CHECK (product_type IN ('CURRENT', 'SAVINGS')),
    -- v1 is AED only; a later migration widens this list.
    CONSTRAINT ck_product_currency_code CHECK (currency_code IN ('AED'))
);
GO

-- The two v1 products (business plan section 4): the salary account and
-- Goal Savings.
INSERT INTO core.product (product_code, product_type, currency_code, interest_bearing)
VALUES ('CURRENT_AED', 'CURRENT', 'AED', 0),
       ('SAVINGS_AED', 'SAVINGS', 'AED', 1);
GO

-- Account numbers. The application asks for the next number before it
-- inserts, because it needs the number to build the IBAN. A number that is
-- drawn but never used leaves a gap, which is normal.
CREATE SEQUENCE core.account_id_seq AS BIGINT START WITH 1000000001 INCREMENT BY 1;
GO

-- core.account: a customer's account.
CREATE TABLE core.account (
    account_id    BIGINT      NOT NULL CONSTRAINT df_account_account_id DEFAULT (NEXT VALUE FOR core.account_id_seq),
    customer_id   BIGINT      NOT NULL,
    product_code  VARCHAR(20) NOT NULL,
    iban          CHAR(23)    NOT NULL,
    currency_code CHAR(3)     NOT NULL,
    status        VARCHAR(10) NOT NULL,
    opened_on     DATE        NOT NULL,
    row_version   ROWVERSION  NOT NULL, -- lets the application detect a concurrent update
    CONSTRAINT pk_account PRIMARY KEY (account_id),
    CONSTRAINT fk_account_customer FOREIGN KEY (customer_id)  REFERENCES core.customer (customer_id),
    CONSTRAINT fk_account_product  FOREIGN KEY (product_code) REFERENCES core.product (product_code),
    CONSTRAINT uq_account_iban UNIQUE (iban),
    -- A UAE IBAN is AE, 2 check digits, a 3-digit bank code and a 16-digit
    -- account number: 23 characters. 999 is Ledgerline's fictional bank code,
    -- so no IBAN here can belong to a real bank.
    -- The mod-97 check: move the first four characters (AE and the check
    -- digits) to the end, turn A into 10 and E into 14 (so AE becomes 1014),
    -- and the whole number must leave remainder 1 when divided by 97. That
    -- number has 25 digits, too many for BIGINT but within DECIMAL(38,0).
    -- TRY_CAST gives NULL for anything that is not a number, and a check
    -- constraint lets NULL through, so the LIKE pattern must come first and
    -- reject letters on its own.
    CONSTRAINT ck_account_iban CHECK (
        iban LIKE 'AE' + REPLICATE('[0-9]', 21)
        AND SUBSTRING(iban, 5, 3) = '999'
        AND TRY_CAST(SUBSTRING(iban, 5, 19) + '1014' + SUBSTRING(iban, 3, 2) AS DECIMAL(38,0)) % 97 = 1
    ),
    -- v1 is AED only; a later migration widens this list.
    CONSTRAINT ck_account_currency_code CHECK (currency_code IN ('AED')),
    CONSTRAINT ck_account_status        CHECK (status IN ('OPEN', 'FROZEN', 'CLOSED'))
);
GO

-- One salary account per customer (part of BR-02). Only accounts that are
-- not closed count, so a customer can open a new salary account after
-- closing the old one. A frozen account still counts.
CREATE UNIQUE INDEX ux_account_one_open_salary
    ON core.account (customer_id)
    WHERE product_code = 'CURRENT_AED' AND status <> 'CLOSED';
GO

-- Token triggers. Nothing can exist without a token, so the extract can
-- always swap internal IDs for tokens (blueprint 10). A trigger fires once
-- per statement, so it reads every row from the inserted pseudo-table rather
-- than assuming a single row.
CREATE TRIGGER core.tr_customer_token_vault
ON core.customer
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON; -- keeps the trigger's row count out of the caller's result

    INSERT INTO pii.token_vault (entity_type, entity_id)
    SELECT 'CUSTOMER', customer_id
    FROM inserted;
END;
GO

CREATE TRIGGER core.tr_account_token_vault
ON core.account
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO pii.token_vault (entity_type, entity_id)
    SELECT 'ACCOUNT', account_id
    FROM inserted;
END;
GO
