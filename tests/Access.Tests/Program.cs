using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyRevitAddin.Access;

// Run on Windows: exercises actual CurrentUser DPAPI without a live account.
internal static class Program
{
    private static readonly CancellationToken Token = CancellationToken.None;
    private static readonly AccessSettings Settings = new AccessSettings
    { ProjectUrl = "https://example.supabase.co", PublishableKey = "sb_publishable_test" };
    private static string SessionJson(string refresh = "refresh-1", int expires = 3600) =>
        "{\"access_token\":\"access-1\",\"refresh_token\":\"" + refresh + "\",\"expires_in\":" + expires + "}";
    private static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }

    public static async Task Main()
    {
        var folder = Path.Combine(Path.GetTempPath(), "DokaAccessTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "session.bin");
        try
        {
            var handler = new ScriptedHandler();
            using (var client = new AccessClient(Settings, path, handler))
            {
                Assert((await client.Check(Token)).Reason == "sign_in_required", "No session must require sign-in.");
                Assert(handler.Calls == 0, "No session must not call authorize.");
                handler.Add("/auth/v1/otp", 200, "{}", async request =>
                {
                    Assert(request.Headers.Authorization == null, "OTP must not contain a session token.");
                    Assert((await request.Content.ReadAsStringAsync()).Contains("\"create_user\":true"), "Allow first signup.");
                });
                await client.SendCode("team@example.com", Token);
                handler.Add("/auth/v1/verify", 200, SessionJson(), async request =>
                {
                    var body = await request.Content.ReadAsStringAsync();
                    Assert(body.Contains("\"type\":\"email\"") && body.Contains("12345678"), "Accept configured code lengths.");
                });
                await client.VerifyCode("team@example.com", "12345678", Token);
                Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("refresh-1"), "No plaintext refresh token on disk.");
                handler.Add("/functions/v1/authorize-tool", 200, "{\"allowed\":true}", request =>
                {
                    Assert(request.Headers.Authorization?.Parameter == "access-1", "Send user token.");
                    return Task.CompletedTask;
                });
                Assert((await client.Check(Token)).Allowed, "Approved user should pass.");
                handler.Add("/functions/v1/authorize-tool", 403, "{\"allowed\":false,\"reason\":\"access_not_granted\"}");
                Assert(!(await client.Check(Token)).Allowed, "Same session must observe revocation on next action.");
                handler.Add("/functions/v1/authorize-tool", 403, "{\"allowed\":true}");
                Assert(!(await client.Check(Token)).Allowed, "Non-200 must deny even with allowed true.");
                handler.Add("/functions/v1/authorize-tool", 200, "{}");
                Assert(!(await client.Check(Token)).Allowed, "Missing allowed must deny.");
                handler.Add("/functions/v1/authorize-tool", 200, "not-json");
                await MustFail<SerializationException>(() => client.Check(Token), "Malformed decision must fail closed.");
                handler.Add("/functions/v1/authorize-tool", 401, "{}");
                handler.Add("/auth/v1/token", 200, SessionJson("refresh-2"));
                handler.Add("/functions/v1/authorize-tool", 200, "{\"allowed\":true}");
                Assert((await client.Check(Token)).Allowed, "401 must refresh then check approval again.");
                using (var restarted = new AccessClient(Settings, path, handler))
                {
                    handler.Add("/functions/v1/authorize-tool", 401, "{}");
                    handler.Add("/auth/v1/token", 200, SessionJson("refresh-3"), async request =>
                        Assert((await request.Content.ReadAsStringAsync()).Contains("refresh-2"), "Persist rotated refresh token."));
                    handler.Add("/functions/v1/authorize-tool", 403, "{\"allowed\":false}");
                    Assert(!(await restarted.Check(Token)).Allowed, "Refresh alone must not grant access.");
                    handler.Add("/auth/v1/logout", 503, "{}");
                    await MustFail<IOException>(() => restarted.SignOut(Token), "Failed remote logout must be reported.");
                    Assert(!File.Exists(path) && !restarted.HasSession, "Offline logout must clear local credentials.");
                }
                handler.Add("/functions/v1/authorize-tool", 401, "{}");
                handler.Add("/auth/v1/token", 400, "{}");
                Assert((await client.Check(Token)).Reason == "sign_in_required" && !client.HasSession, "Invalid refresh must clear session.");
                handler.Add("/auth/v1/otp", 429, "{}");
                await MustFail<InvalidOperationException>(() => client.SendCode("team@example.com", Token), "Rate limits must be surfaced.");
                handler.Add("/auth/v1/verify", 400, "{}");
                await MustFail<InvalidOperationException>(() => client.VerifyCode("team@example.com", "wrong", Token), "Wrong code must deny.");
                Assert(!client.HasSession, "Failed verification cannot create a session.");
                handler.AssertDone();
            }
            Console.WriteLine("Access client checks passed.");
        }
        finally { Directory.Delete(folder, true); }
    }

    private static async Task MustFail<TException>(Func<Task> action, string message) where TException : Exception
    {
        try { await action(); } catch (TException) { return; }
        throw new Exception(message);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> steps =
            new Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>>();
        internal int Calls;
        internal void Add(string path, int status, string json, Func<HttpRequestMessage, Task> inspect = null)
        {
            steps.Enqueue(async request =>
            {
                Assert(request.RequestUri.AbsolutePath == path, "Unexpected endpoint: " + request.RequestUri.AbsolutePath);
                Assert(request.Method == HttpMethod.Post, "Use POST.");
                Assert(request.Headers.Contains("apikey"), "Publishable key required.");
                if (inspect != null) await inspect(request);
                return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            });
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; token.ThrowIfCancellationRequested(); return steps.Dequeue()(request); }
        internal void AssertDone() => Assert(steps.Count == 0, "All expected requests must occur.");
    }
}
