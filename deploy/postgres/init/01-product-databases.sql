-- Local development only. Each product has its own database and role,
-- so neither product can read the other's data.
CREATE ROLE trading LOGIN PASSWORD 'trading';
CREATE DATABASE trading OWNER trading;
REVOKE CONNECT ON DATABASE trading FROM PUBLIC;

CREATE ROLE prop LOGIN PASSWORD 'prop';
CREATE DATABASE prop OWNER prop;
REVOKE CONNECT ON DATABASE prop FROM PUBLIC;
