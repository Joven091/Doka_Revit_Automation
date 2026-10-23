-- Administrators can approve exact emails before users register. Auth still
-- verifies mailbox ownership before the authorization function reads this table.
create table public.tool_email_access (
  email text primary key,
  enabled boolean not null default false,
  expires_at timestamptz,
  admin_note text,
  created_at timestamptz not null default now(),
  constraint tool_email_access_normalized_email check (
    email = lower(btrim(email)) and email <> '' and position('@' in email) > 1
  )
);

alter table public.tool_email_access enable row level security;
-- No client policies: clients must use the authenticated authorization function.
revoke all on table public.tool_email_access from public, anon, authenticated;
grant select on table public.tool_email_access to service_role;

comment on table public.tool_email_access is
  'Administrator-managed exact-email pre-approvals. Requires an Auth-verified email; a tool_access account row always takes precedence.';
