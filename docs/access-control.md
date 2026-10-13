# Supabase access control

Supabase hosts Auth, the approved-user database, and an Edge Function. No
separate application server is required. Users sign in with a one-time code
sent to their email by Supabase. You manage the approval table yourself; no
Microsoft 365 administrator or Entra application registration is needed.
Verifying an email proves mailbox ownership but does not grant tool access.

## Implementation status

The migration and authorization function are prepared and all 21 backend tests
pass. The Revit email-code integration is now implemented, including gates at
the dashboard command and all calculator openings. It has not been compiled
or verified in Revit in this environment. The server is deployed to **Doka
Calculator**, project `qwgfdbvbdfpgbbntezil` in Joven091's Org. The bundled
`src/access-settings.json` contains its project URL and enabled publishable key.
The verified owner account is approved. Existing
installers remain unrestricted. Do not distribute an installer as protected
until the Windows build and live checks below are complete.

Continuation on 12 October 2026: the previous chat's uncommitted backend files
were restored from its recorded changes. The user selected Joven091's Org
(`okmikbkkhzzedjjryaqu`) for the dedicated project. The connected Supabase
`get_cost` tool returned UNAVAILABLE, so the required cost confirmation and
project creation could not be completed. No existing Supabase project was
changed. This environment has no .NET SDK and its HTTP proxy is unreachable.

Deployment on 13 October 2026: the user created the project manually and supplied
its dashboard URL. Migration `20261013034838_tool_access.sql` is applied; the
local filename matches the hosted migration history. `authorize-tool` version 1
is ACTIVE and its retrieved source matches the local source. The project URL is
`https://qwgfdbvbdfpgbbntezil.supabase.co`. Email delivery configuration is still
pending; the available connector does not expose Auth SMTP/template management.

Database verification confirmed RLS enabled, zero client policies, no SELECT or
write privileges for `anon`/`authenticated`, and SELECT access for the function's
`service_role`. The security advisor reports only the expected informational
[RLS enabled with no policies](https://supabase.com/docs/guides/database/database-linter?lint=0008_rls_enabled_no_policy)
notice: client access is intentionally denied. The verified owner account now has an enabled approval row with no expiry.
Live HTTP checks could not run because the environment's proxy is unreachable;
Windows build, DPAPI harness, and Revit checks remain pending.

The desktop checks online approval once per Revit process, on first tool use.
A successful approval is cached only in process memory until Revit exits,
sign-out/account switching starts, or an explicit access recheck is made.
Calculator openings then work without further network requests, including offline.
Revocation and approval expiry take effect on the next check, normally after
restarting Revit; an existing process can remain approved indefinitely. This
tradeoff was explicitly accepted for a smoother experience. Checks never
interrupt an already-running calculation or modify a Revit model. Older unrestricted installers cannot be retroactively
revoked. Local DLL checks can be patched; essential server-side functionality
is needed for protection against determined reverse engineering.

## Account setup

1. Create or choose a dedicated Supabase project. Keep production separate from
   development. Note its project URL, project reference, and publishable key.
   The desktop app may contain the URL and publishable key. Never put the
   service-role/secret key or SMTP credentials in the installer.
2. In Supabase Authentication, enable the Email provider and require email
   confirmation. Allow new signups so a teammate can verify their email before
   you approve their UUID. Disable unused OAuth providers and anonymous sign-in.
   A signup without a matching enabled approval row cannot use the tool.
3. Configure custom SMTP under Authentication using an email provider you
   control. Verify a sending domain with that provider and enter its SMTP
   credentials in Supabase only. Supabase's built-in email service is limited
   to testing and restricts recipients; it is not suitable for team distribution.
   You can send from your own verified domain to team work addresses without
   configuring the company's Microsoft 365 tenant.
4. Edit both the Magic Link and Confirm Signup email templates to show
   `{{ .Token }}` as the one-time code instead of requiring a link click. For
   example: "Your Doka Revit Automation sign-in code is {{ .Token }}."
   The signup template handles new accounts, and Magic Link handles returning
   accounts. Retain Supabase's OTP expiry and sending rate limits.
   Paste `supabase/templates/email-code.html` into both templates in the dashboard;
   that file is a prepared template, not an applied hosted Auth configuration.
5. Test code delivery to your own address first. The desktop flow uses email
   code entry, so no browser callback or redirect URL is required.

## Deploy the prepared server components

### Email provider setup for a solo tool owner

Resend is one supported option for Supabase Auth custom SMTP. Create a Resend
account and add a domain you own at <https://resend.com/domains>. A sending
subdomain such as `auth.your-domain.example` keeps authentication mail separate.
Add the DNS records Resend supplies through your domain's DNS provider, then
wait for verification. You do not need a Microsoft 365 administrator when you
control that domain. If you do not own a domain, obtain one before this setup.

Follow Resend's current Supabase SMTP instructions at
<https://resend.com/docs/send-with-supabase-smtp>. In the dedicated Supabase
project's Authentication SMTP settings, enable custom SMTP and enter the host,
port, username and credential specified by Resend. Choose a sender on the
verified domain, such as `no-reply@auth.your-domain.example`, and sender name
`Doka Revit Automation`. Enter credentials directly in Supabase; do not commit
them or put them in the desktop configuration.

Paste `supabase/templates/email-code.html` into both Confirm Signup and Magic
Link templates. Keep email confirmation enabled. Test a new account and a
returning account, then inspect Auth rate limits against your team's expected
usage. Supabase's default mail service can be used for initial tests with
organization-member addresses, but cannot deliver codes to arbitrary teammates.
See <https://supabase.com/docs/guides/auth/auth-smtp>.

With the Supabase CLI installed, run from the repository root:

```sh
supabase login
supabase link --project-ref YOUR_PROJECT_REF
supabase db push
supabase functions deploy authorize-tool
```

Review pending migrations before pushing to an existing project. The function
configuration turns off the gateway's legacy JWT verification because the
function validates every token through `auth.getUser(token)` itself. Removing
that validation would expose the endpoint. Supabase supplies its server-only
service-role key to Edge Functions; no custom secret needs to be shipped to users.

## Approve and revoke users

A first email-code request may create a user in Supabase Auth, but grants no
tool access. After the teammate verifies their email, find their UUID in
Authentication → Users. In the SQL
editor, approve that UUID:

```sql
insert into public.tool_access (user_id, enabled, admin_note)
values ('USER_UUID', true, 'Approved by tool administrator')
on conflict (user_id) do update set enabled = true, expires_at = null;
```

To revoke:

```sql
update public.tool_access set enabled = false where user_id = 'USER_UUID';
```

Use `expires_at` for temporary access. The Table Editor can also manage these
rows. Users cannot read or change this table through the public API: RLS is
enabled without client policies, and client table privileges are revoked.
Supabase project administrators retain control; protect those accounts with MFA.

## Desktop integration contract

Implementation: `src/Access/AccessClient.cs` performs Auth and online approval
requests; `AccessWindow.cs` provides the cancellable modal sign-in/check UI.
Only HTTP work is asynchronous. Command bodies run on their original Revit
thread after the modal gate returns successfully. Calculator openings and
slab-system switching reuse the process approval without loading credentials
or making network requests. Reopening the dashboard also reuses that approval. Use
**Account / sign out** on the dashboard to clear credentials or switch accounts.
Sessions are stored under the current user's LocalAppData directory using
Windows DPAPI, bound to the project. No approvals are persisted to disk. Sign-out clears the in-memory approval
before attempting server sign-out and clears the local session even if that fails; the UI reports an unconfirmed
server sign-out. Supabase may still accept an issued access token until its
expiry; disable the approval row for immediate denial on the next checked action.

Before building, populate `src/access-settings.json`:

```json
{
  "project_url": "https://PROJECT_REF.supabase.co",
  "publishable_key": "sb_publishable_YOUR_PUBLIC_KEY"
}
```

The build copies this file to its output, and the installer includes it.
Keep only public settings here. Missing, invalid, or blank settings deny access.

Show an email field and "Send sign-in code", then a code field and "Sign in".
The desktop client calls Supabase Auth directly over HTTPS with the publishable
key in the `apikey` header. Request a code:

```http
POST https://PROJECT_REF.supabase.co/auth/v1/otp
apikey: SUPABASE_PUBLISHABLE_KEY
Content-Type: application/json

{"email":"teammate@example.com","create_user":true}
```

Verify the code (do not hardcode a code length; it is a project setting):

```http
POST https://PROJECT_REF.supabase.co/auth/v1/verify
apikey: SUPABASE_PUBLISHABLE_KEY
Content-Type: application/json

{"email":"teammate@example.com","token":"USER_ENTERED_CODE","type":"email"}
```

Use only the session returned by successful verification. Never log codes or
tokens. Handle incorrect/expired codes and email rate limits with retry/resend
messages. A successful code verification still needs the approval check below.
Persist refresh tokens using Windows DPAPI scoped to the current user. Refresh
expired access tokens through `/auth/v1/token?grant_type=refresh_token`, and
persist the rotated refresh token. Clear the local session on sign-out and
revoke it through Supabase Auth. Send this request when no process approval exists:

```http
POST https://PROJECT_REF.supabase.co/functions/v1/authorize-tool
apikey: SUPABASE_PUBLISHABLE_KEY
Authorization: Bearer SUPABASE_USER_ACCESS_TOKEN
```

No user ID, email, or device identity is accepted from the caller. The function
derives identity from the validated token and checks the approval table on every
request. Only HTTP 200 with `allowed: true` establishes process approval. Never
persist that approval across Revit restarts. When checking, fail closed on malformed responses,
timeouts, or outages, and offer a retry without touching the Revit model.

| Status / reason | User experience |
| --- | --- |
| 200 | Open the requested tool. |
| 401 / `sign_in_required` | Refresh the session or show email-code sign-in. |
| 403 / `verified_email_required` | Ask the user to sign in again with a verified email. |
| 403 / `access_not_granted` | Explain that approval is required; offer contact tool owner and switch account. |
| 403 / `access_expired` | Explain that access expired; contact tool owner. |
| 503 / `service_unavailable` | Explain that access cannot be checked; offer retry. |

Protect the remaining dashboard `IExternalCommand.Execute` method, plus tool-opening
handlers so sign-out or account switching requires approval again. Keep
network/authentication work asynchronous in a modal WPF UI, without making
Revit API calls on background threads. Resume Revit actions on the main thread
only after authorization completes. Provide sign-out/switch-account controls.

## Verification before distribution

The user confirmed code delivery to the owner's address on 13 October 2026
after configuring Resend SMTP and the email templates. `auth.ascentrpg.com`
was shown as Verified in the user's Resend dashboard. The Auth user exists,
and email confirmation was verified in the database at 04:19 UTC on 13 October
2026. The user reported SIGN-IN VERIFIED from the local test. The matching
owner account was then approved with no expiry. The user then reported ACCESS APPROVED from the live authorization check
using the same session. Email delivery, OTP verification, and owner approval
are confirmed; live revocation and interactive Revit verification remain pending. Receiving a code alone does not approve access.

Use the standalone `scripts/test-email-access.ps1 -Email your-address@example.com`
on Windows to enter the code
privately and test approval. It embeds only this project's public URL and key,
keeps session tokens in memory, and signs the test session out on exit. Add
`-RequestNewCode` to request a fresh code if needed. After it prints SIGN-IN
VERIFIED and the expected initial ACCESS BLOCKED result, keep the window open
while the owner approves the verified UUID. Press Enter to check again using
the same session. Type `q` to sign out. Sign-in verification and the subsequent approval recheck have run successfully. Neither
PowerShell nor the .NET SDK is available in the agent environment.

The function's security tests run without network access:

```sh
node --test supabase/functions/authorize-tool/handler.test.js
```

Use `node supabase/functions/authorize-tool/handler.test.js` to see the individual
21 tests when the installed Node test runner reports only a file-level summary.
The Windows client harness covers session encryption, persisted token rotation,
approval and revocation with the same session, malformed responses, inconsistent
HTTP status/approval, expired refresh tokens, wrong codes, rate limits, and local
credential removal when server sign-out fails. Run on Windows with the .NET SDK:

```powershell
dotnet build src/MyRevitAddin.csproj -c Release
dotnet build tests/Access.Tests/Access.Tests.csproj -c Release
& tests/Access.Tests/bin/Release/net48/Access.Tests.exe
```

Windows CI passed the 21 backend tests, DPAPI client harness, and Release add-in build
on 13 October 2026 ([run 38349258102](https://github.com/Joven091/Doka_Revit_Automation/actions/runs/38349258102)).
The same checks can be run together from the repository root on Windows:

```powershell
./scripts/validate-access.ps1
# After filling the deployed project settings:
./scripts/validate-access.ps1 -RequireConfiguredProject
```

The migration and Edge Function are deployed, database permissions and security
advisors checked, and public settings filled. SMTP and both code email templates
are configured; complete the remaining live checks below. SMTP credentials must be configured only in
Supabase, through a provider account you control.

After deployment and desktop integration, verify signup and returning-user code
delivery, invalid/expired codes, token refresh, a permitted account, an
unapproved account, and revocation followed by a Revit restart. Confirm that
repeated calculator openings make no approval requests after success, and
that sign-out (including failed server sign-out) clears process approval. Check that an authenticated client cannot modify
`tool_access`, an invalid token cannot obtain approval, first use after restart is blocked offline,
and cancelling sign-in leaves the Revit model untouched. These live checks
require the configured Supabase project, email provider, and Revit on Windows.

## GitHub delivery

The access changes are prepared on current main at `d62b5f9`. The four standalone
commands removed upstream remain removed. The current installer paths and WPF
resource URI fix are preserved. The delivery branch is `codex/supabase-email-access`, published in
[PR #136](https://github.com/Joven091/Doka_Revit_Automation/pull/136).
Fetch that branch to get complete files. Windows CI runs the access validation
script automatically; interactive Revit checks remain required before release.
