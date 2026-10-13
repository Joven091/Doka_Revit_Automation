using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MyRevitAddin.Access
{
    [DataContract]
    internal sealed class AccessSettings
    {
        [DataMember(Name = "project_url")] public string ProjectUrl { get; set; }
        [DataMember(Name = "publishable_key")] public string PublishableKey { get; set; }
    }

    [DataContract]
    internal sealed class AccessSession
    {
        [DataMember(Name = "access_token")] public string AccessToken { get; set; }
        [DataMember(Name = "refresh_token")] public string RefreshToken { get; set; }
        [DataMember(Name = "expires_in")] public long ExpiresIn { get; set; }
        [DataMember(Name = "expires_at")] public long ExpiresAt { get; set; }
    }

    [DataContract]
    internal sealed class AccessDecision
    {
        [DataMember(Name = "allowed")] public bool Allowed { get; set; }
        [DataMember(Name = "reason")] public string Reason { get; set; }
    }

    internal sealed class AccessClient : IDisposable
    {
        private readonly HttpClient http;
        private readonly AccessSettings settings;
        private readonly string sessionPath;
        private AccessSession session;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Doka Revit Automation access v1");

        internal static AccessClient Load()
        {
            string folder = Path.GetDirectoryName(typeof(AccessClient).Assembly.Location);
            var settings = Decode<AccessSettings>(File.ReadAllBytes(Path.Combine(folder, "access-settings.json")));
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DokaRevitAutomation", "access-session.bin");
            return new AccessClient(settings, path);
        }

        internal AccessClient(AccessSettings settings, string sessionPath, HttpMessageHandler handler = null)
        {
            Uri uri;
            if (settings == null || !Uri.TryCreate(settings.ProjectUrl, UriKind.Absolute, out uri) ||
                uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) ||
                uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                string.IsNullOrWhiteSpace(settings.PublishableKey) ||
                !settings.PublishableKey.StartsWith("sb_publishable_", StringComparison.Ordinal))
                throw new InvalidOperationException("The tool owner needs to configure access before this installation can be used.");
            this.settings = settings;
            this.sessionPath = sessionPath;
            http = handler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler);
            http.BaseAddress = uri;
            http.Timeout = TimeSpan.FromSeconds(20);
            // Bind the protected file to this project as well as the Windows user.
            try
            {
                if (File.Exists(sessionPath))
                    session = Decode<AccessSession>(ProtectedData.Unprotect(File.ReadAllBytes(sessionPath),
                        ProjectEntropy(), DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException) { Clear(); }
            catch (SerializationException) { Clear(); }
        }

        private byte[] ProjectEntropy() => Encoding.UTF8.GetBytes(settings.ProjectUrl + Encoding.UTF8.GetString(Entropy));
        internal bool HasSession => session != null;

        internal async Task SendCode(string email, CancellationToken cancellation)
        {
            using (var response = await Post("auth/v1/otp", new OtpRequest { Email = email, CreateUser = true }, null, cancellation))
            {
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(response.StatusCode == (HttpStatusCode)429
                        ? "Too many code requests. Please wait before trying again."
                        : "Unable to send a code. Check your email address and try again.");
            }
        }

        internal async Task VerifyCode(string email, string code, CancellationToken cancellation)
        {
            using (var response = await Post("auth/v1/verify", new VerifyRequest { Email = email, Token = code, Type = "email" }, null, cancellation))
            {
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException("The code could not be verified. Try again or request a new code.");
                await SaveResponse(response, cancellation);
            }
        }

        internal async Task<AccessDecision> Check(CancellationToken cancellation)
        {
            if (session == null) return new AccessDecision { Reason = "sign_in_required" };
            if (session.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60)
                if (!await Refresh(cancellation)) return new AccessDecision { Reason = "sign_in_required" };
            var result = await Authorize(cancellation);
            if (result.Reason == "sign_in_required")
            {
                if (!await Refresh(cancellation)) return result;
                result = await Authorize(cancellation);
                if (result.Reason == "sign_in_required") Clear();
            }
            return result;
        }

        private async Task<AccessDecision> Authorize(CancellationToken cancellation)
        {
            using (var response = await Post("functions/v1/authorize-tool", new EmptyRequest(), session.AccessToken, cancellation))
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    return new AccessDecision { Reason = "sign_in_required" };
                var result = Decode<AccessDecision>(await response.Content.ReadAsByteArrayAsync());
                // Both the HTTP status and the explicit boolean must permit this action.
                if (response.StatusCode == HttpStatusCode.OK && result.Allowed) return result;
                return new AccessDecision { Reason = result.Reason ?? "service_unavailable" };
            }
        }

        private async Task<bool> Refresh(CancellationToken cancellation)
        {
            using (var response = await Post("auth/v1/token?grant_type=refresh_token",
                new RefreshRequest { RefreshToken = session.RefreshToken }, null, cancellation))
            {
                if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Unauthorized)
                { Clear(); return false; }
                if (!response.IsSuccessStatusCode) throw new IOException("Access cannot be checked right now. Please retry.");
                await SaveResponse(response, cancellation);
                return true;
            }
        }

        private async Task SaveResponse(HttpResponseMessage response, CancellationToken cancellation)
        {
            var next = Decode<AccessSession>(await response.Content.ReadAsByteArrayAsync());
            if (string.IsNullOrWhiteSpace(next.AccessToken) || string.IsNullOrWhiteSpace(next.RefreshToken) || next.ExpiresIn <= 0)
                throw new SerializationException("Invalid sign-in response.");
            next.ExpiresAt = checked(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + next.ExpiresIn);
            cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(sessionPath));
            // Save each rotated token before any further network request uses it.
            File.WriteAllBytes(sessionPath, ProtectedData.Protect(Encode(next), ProjectEntropy(), DataProtectionScope.CurrentUser));
            session = next;
        }

        internal async Task SignOut(CancellationToken cancellation)
        {
            var token = session?.AccessToken;
            Clear(); // Always remove local credentials even when offline.
            if (token == null) return;
            using (var response = await Post("auth/v1/logout?scope=local", new EmptyRequest(), token, cancellation))
                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Unauthorized)
                    throw new IOException("Signed out on this computer. Server sign-out could not be confirmed.");
        }

        private void Clear() { session = null; if (File.Exists(sessionPath)) File.Delete(sessionPath); }

        private async Task<HttpResponseMessage> Post<T>(string path, T body, string token, CancellationToken cancellation)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, path))
            {
                request.Headers.Add("apikey", settings.PublishableKey);
                if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = new ByteArrayContent(Encode(body));
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return await http.SendAsync(request, cancellation);
            }
        }

        internal static T Decode<T>(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
        private static byte[] Encode<T>(T value)
        {
            using (var stream = new MemoryStream())
            { new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); return stream.ToArray(); }
        }
        public void Dispose() => http.Dispose();

        [DataContract] private sealed class EmptyRequest { }
        [DataContract] private sealed class OtpRequest
        {
            [DataMember(Name = "email")] public string Email { get; set; }
            [DataMember(Name = "create_user")] public bool CreateUser { get; set; }
        }
        [DataContract] private sealed class VerifyRequest
        {
            [DataMember(Name = "email")] public string Email { get; set; }
            [DataMember(Name = "token")] public string Token { get; set; }
            [DataMember(Name = "type")] public string Type { get; set; }
        }
        [DataContract] private sealed class RefreshRequest
        { [DataMember(Name = "refresh_token")] public string RefreshToken { get; set; } }
    }
}
