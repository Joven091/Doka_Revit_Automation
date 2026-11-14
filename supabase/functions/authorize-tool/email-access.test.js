import test from "node:test";
import assert from "node:assert/strict";
import { createHandler } from "./handler.js";

const now = Date.parse("2026-10-23T12:00:00Z");
const user = {
  id: "new-account", email: "Teammate@Example.com",
  email_confirmed_at: "2026-10-23T11:00:00Z",
  identities: [{ provider: "email" }], is_anonymous: false,
};
const approved = { enabled: true, expires_at: null };
const request = () => new Request("https://example.test/authorize-tool", {
  method: "POST", headers: { Authorization: "Bearer token" },
  body: JSON.stringify({ email: "other@example.com", user_id: "other-account" }),
});
function setup({ account = null, emailAccess = approved, authUser = user,
  accountError = null, emailError = null, throwEmail = false } = {}) {
  const queried = [];
  const run = createHandler({
    getUser: async () => ({ data: { user: authUser } }),
    getAccess: async (id) => {
      assert.equal(id, user.id);
      return { data: account, error: accountError };
    },
    getEmailAccess: async (email) => {
      queried.push(email);
      if (throwEmail) throw new Error("network unavailable");
      return { data: emailAccess, error: emailError };
    },
    now: () => now,
  });
  return { run, queried };
}

test("pre-approval uses normalized Auth email, ignoring caller-provided identity", async () => {
  const { run, queried } = setup();
  const response = await run(request());
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { allowed: true });
  assert.deepEqual(queried, ["teammate@example.com"]);
});

for (const [name, account, status] of [
  ["approved", approved, 200],
  ["disabled", { enabled: false }, 403],
  ["expired", { enabled: true, expires_at: "2026-10-23T11:59:59Z" }, 403],
  ["invalid expiry", { enabled: true, expires_at: "invalid" }, 403],
]) {
  test(`an existing ${name} account takes precedence over email pre-approval`, async () => {
    const { run, queried } = setup({ account });
    assert.equal((await run(request())).status, status);
    assert.deepEqual(queried, []);
  });
}

for (const [name, emailAccess, status] of [
  ["unlisted", null, 403],
  ["disabled", { enabled: false }, 403],
  ["expired", { enabled: true, expires_at: "2026-10-23T11:59:59Z" }, 403],
  ["expires now", { enabled: true, expires_at: "2026-10-23T12:00:00Z" }, 403],
  ["invalid expiry", { enabled: true, expires_at: "invalid" }, 403],
  ["temporary approval", { enabled: true, expires_at: "2026-10-23T12:00:01Z" }, 200],
]) {
  test(`handles ${name} email pre-approval`, async () => {
    const { run } = setup({ emailAccess });
    assert.equal((await run(request())).status, status);
  });
}

for (const [name, fields] of [
  ["unverified", { email_confirmed_at: null, user_metadata: { email_verified: true } }],
  ["anonymous", { is_anonymous: true }],
  ["non-email identity", { identities: [{ provider: "azure" }] }],
]) {
  test(`does not consult pre-approvals for an ${name} account`, async () => {
    const { run, queried } = setup({ authUser: { ...user, ...fields } });
    assert.equal((await run(request())).status, 403);
    assert.deepEqual(queried, []);
  });
}

test("database failures cannot fall back to an email approval", async () => {
  const { run, queried } = setup({ accountError: new Error("database unavailable") });
  assert.equal((await run(request())).status, 503);
  assert.deepEqual(queried, []);
});

test("email approval lookup errors fail closed", async () => {
  for (const options of [{ emailError: new Error("database unavailable") }, { throwEmail: true }]) {
    const { run } = setup(options);
    assert.equal((await run(request())).status, 503);
  }
});

test("disabling a pre-approval denies the next check with the same verified identity", async () => {
  const access = { ...approved };
  const { run } = setup({ emailAccess: access });
  assert.equal((await run(request())).status, 200);
  access.enabled = false;
  assert.equal((await run(request())).status, 403);
});
