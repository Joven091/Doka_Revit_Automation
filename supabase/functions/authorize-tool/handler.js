// Dependency injection lets the security boundary be tested without live users.
export function createHandler({ getUser, getAccess, now = () => Date.now() }) {
  const reply = (status, body) => new Response(JSON.stringify(body), {
    status,
    headers: {
      "Content-Type": "application/json",
      "Cache-Control": "no-store",
    },
  });

  return async (request) => {
    if (request.method !== "POST") {
      return reply(405, { allowed: false, reason: "method_not_allowed" });
    }
    const header = request.headers.get("Authorization") ?? "";
    const match = /^Bearer ([^\s]+)$/i.exec(header);
    if (!match) return reply(401, { allowed: false, reason: "sign_in_required" });

    try {
      // Never derive identity from an email/user_id in the request body or from
      // an unverified JWT payload. Auth must validate the supplied access token.
      const { data, error } = await getUser(match[1]);
      if (error || !data?.user?.id) {
        return reply(401, { allowed: false, reason: "sign_in_required" });
      }
      const user = data.user;
      // Trust Auth's confirmed-email fields, never editable user_metadata.
      // Email verification proves mailbox ownership; the table below grants access.
      const confirmedAt = Date.parse(user.email_confirmed_at ?? "");
      if (user.is_anonymous === true || !user.email?.trim() ||
          !Number.isFinite(confirmedAt) || confirmedAt > now() ||
          !user.identities?.some((identity) => identity.provider === "email")) {
        return reply(403, { allowed: false, reason: "verified_email_required" });
      }

      const { data: access, error: accessError } = await getAccess(user.id);
      if (accessError) return reply(503, { allowed: false, reason: "service_unavailable" });
      if (!access || access.enabled !== true) {
        return reply(403, { allowed: false, reason: "access_not_granted" });
      }
      const expiresAt = access.expires_at == null ? Infinity : Date.parse(access.expires_at);
      if (Number.isNaN(expiresAt) || expiresAt <= now()) {
        return reply(403, { allowed: false, reason: "access_expired" });
      }
      // This is an online decision for the current action, not an offline lease.
      return reply(200, { allowed: true });
    } catch {
      return reply(503, { allowed: false, reason: "service_unavailable" });
    }
  };
}
