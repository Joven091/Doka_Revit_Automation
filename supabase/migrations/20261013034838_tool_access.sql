-- Only administrators (SQL editor / dashboard) manage access. Signing in does
-- not automatically grant permission to run the add-in.
create table public.tool_access (
  user_id uuid primary key references auth.users(id) on delete cascade,
  enabled boolean not null default false,
  expires_at timestamptz,
  admin_note text,
  created_at timestamptz not null default now()
);

alter table public.tool_access enable row level security;
-- Intentionally no client-facing RLS policies, including for approved users.
revoke all on table public.tool_access from public, anon, authenticated;
grant select on table public.tool_access to service_role;

comment on table public.tool_access is
  'Administrator-managed access to Doka Revit Automation. Missing, disabled, or expired rows deny access.';
