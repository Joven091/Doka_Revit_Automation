import test from "node:test";
import assert from "node:assert/strict";
import { createHandler } from "./handler.js";

const currentTime = Date.parse("2026-10-12T12:00:00Z");
const user = {
  id: "verified-user",
  email: "teammate@example.com",
  email_confirmed_at: "2026-10-12T11:00:00Z",
  is_anonymous: false,
  identities: [{ provider: "email" }],
};
const request = (token = "valid-token", body = {}) => new Request("https://example.test/authorize-tool", {
  method: "POST",
  headers: token ? { Authorization: `Bearer ${token}` } : {},
  body: JSON.stringify(body),
});
const handler = (overrides = {}) => createHandler({
  getUser: async () => ({ data: { user }, error: null }),
  getAccess: async () => ({ data: { enabled: true, expires_at: null }, error: null }),
  now: () => currentTime,
  ...overrides,
});

test("requires a token before querying any service", async () => {
  const unexpected = () => { throw new Error("must not be called"); };
  const response = await handler({ getUser: unexpected, getAccess: unexpected })(request(null));
  assert.equal(response.status, 401);
});

test("rejects invalid/expired tokens before querying access", async () => {
  let queried = false;
  const response = await handler({
    getUser: async () => ({ data: { user: null }, error: new Error("invalid token") }),
    getAccess: async () => { queried = true; },
  })(request("invalid-token"));
  assert.equal(response.status, 401);
  assert.equal(queried, false);
});

for (const [name, fields] of [
  ["unconfirmed email", { email_confirmed_at: null }],
  ["invalid confirmation", { email_confirmed_at: "invalid" }],
  ["future confirmation", { email_confirmed_at: "2026-10-13T12:00:00Z" }],
  ["missing email", { email: null }],
  ["blank email", { email: " " }],
  ["anonymous account", { is_anonymous: true }],
  ["other provider", { identities: [{ provider: "azure" }] }],
  ["missing identity", { identities: [] }],
  ["spoofed confirmation metadata", {
    email_confirmed_at: null,
    user_metadata: { email_verified: true, email_confirmed_at: "2026-10-12T11:00:00Z" },
  }],
]) {
  test(`denies ${name} before looking up approval`, async () => {
    let queried = false;
    const response = await handler({
      getUser: async () => ({ data: { user: { ...user, ...fields } } }),
      getAccess: async () => { queried = true; },
    })(request());
    assert.equal(response.status, 403);
    assert.deepEqual(await response.json(), { allowed: false, reason: "verified_email_required" });
    assert.equal(queried, false);
  });
}

test("uses verified identity even when the caller supplies another user ID", async () => {
  let queriedId;
  const response = await handler({
    getAccess: async (id) => { queriedId = id; return { data: { enabled: true, expires_at: null } }; },
  })(request("valid-token", { user_id: "administrator", email: "admin@example.com" }));
  assert.equal(queriedId, user.id);
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { allowed: true });
  assert.equal(response.headers.get("Cache-Control"), "no-store");
});

for (const [name, access] of [
  ["missing", null],
  ["disabled", { enabled: false }],
  ["expired", { enabled: true, expires_at: "2026-10-12T11:59:59Z" }],
  ["expires now", { enabled: true, expires_at: "2026-10-12T12:00:00Z" }],
  ["invalid expiry", { enabled: true, expires_at: "invalid" }],
]) {
  test(`denies ${name} access`, async () => {
    const response = await handler({ getAccess: async () => ({ data: access }) })(request());
    assert.equal(response.status, 403);
    assert.equal((await response.json()).allowed, false);
  });
}

test("allows an approved account before its expiry", async () => {
  const response = await handler({
    getAccess: async () => ({ data: { enabled: true, expires_at: "2026-10-12T12:00:01Z" } }),
  })(request());
  assert.equal(response.status, 200);
});

test("revocation takes effect on the next request with the same token", async () => {
  let enabled = true;
  const run = handler({ getAccess: async () => ({ data: { enabled, expires_at: null } }) });
  assert.equal((await run(request())).status, 200);
  enabled = false;
  assert.equal((await run(request())).status, 403);
});

test("database and network errors fail closed", async () => {
  for (const overrides of [
    { getAccess: async () => ({ error: new Error("database failure") }) },
    { getUser: async () => { throw new Error("network failure"); } },
  ]) {
    const response = await handler(overrides)(request());
    assert.equal(response.status, 503);
    assert.equal((await response.json()).allowed, false);
  }
});

test("rejects unsupported methods", async () => {
  const response = await handler()(new Request("https://example.test/authorize-tool"));
  assert.equal(response.status, 405);
});
