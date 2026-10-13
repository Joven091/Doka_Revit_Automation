import { createClient } from "npm:@supabase/supabase-js@2.57.4";
import { createHandler } from "./handler.js";

// Supabase supplies these secrets to deployed Edge Functions. They must never
// be included in the installer, desktop config, source, or a browser bundle.
const admin = createClient(
  Deno.env.get("SUPABASE_URL")!,
  Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
  { auth: { persistSession: false, autoRefreshToken: false } },
);

Deno.serve(createHandler({
  getUser: (token: string) => admin.auth.getUser(token),
  getAccess: (userId: string) => admin.from("tool_access")
    .select("enabled, expires_at")
    .eq("user_id", userId)
    .maybeSingle(),
}));
