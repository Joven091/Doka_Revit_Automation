using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace MyRevitAddin.Access
{
    // Modal dispatcher keeps all Revit command execution on its original thread.
    // Only HTTP/authentication runs asynchronously; no Revit API is used here.
    internal sealed class AccessWindow : Window
    {
        // Process memory only: restarting Revit always requires a fresh approval.
        // AccessWindow entry points and callbacks run on the UI thread.
        private static bool approvedForRevitSession;
        private readonly AccessClient client;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly StackPanel controls = new StackPanel();
        private readonly TextBox email = new TextBox { Margin = new Thickness(0, 4, 0, 12) };
        private readonly TextBox code = new TextBox { Margin = new Thickness(0, 4, 0, 12) };
        private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 10) };
        private string requestedEmail;
        private bool closed;

        internal static bool Require(Window owner = null)
        {
            if (approvedForRevitSession) return true;
            approvedForRevitSession = Open(owner, true);
            return approvedForRevitSession;
        }
        internal static void Manage(Window owner) => Open(owner, false);

        private static bool Open(Window owner, bool checkExisting)
        {
            try
            {
                using (var client = AccessClient.Load())
                {
                    AccessDecision decision = null;
                    if (checkExisting && owner != null && client.HasSession)
                    {
                        decision = CheckSilently(client, owner);
                        if (decision.Allowed) return true;
                    }
                    var window = new AccessWindow(client, checkExisting, decision);
                    if (owner != null) { window.Owner = owner; window.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
                    return window.ShowDialog() == true;
                }
            }
            catch
            {
                MessageBox.Show("Access could not be checked. Ask the tool owner to check this installation's access settings, then retry.",
                    "Doka Revit Automation", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
        }

        // Keep the synchronous caller on the Revit thread while HTTP runs through
        // a nested dispatcher, as with ShowDialog, but without displaying a form.
        private static AccessDecision CheckSilently(AccessClient client, Window owner)
        {
            var frame = new DispatcherFrame();
            var decision = new AccessDecision { Reason = "service_unavailable" };
            bool enabled = owner.IsEnabled;
            var cursor = Mouse.OverrideCursor;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                try
                {
                    owner.IsEnabled = false;
                    Mouse.OverrideCursor = Cursors.Wait;
                    owner.Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        try { decision = await client.Check(timeout.Token); }
                        catch { /* Show the access form with a retry message. */ }
                        finally { frame.Continue = false; }
                    }));
                    Dispatcher.PushFrame(frame);
                    return decision;
                }
                finally
                {
                    timeout.Cancel();
                    Mouse.OverrideCursor = cursor;
                    owner.IsEnabled = enabled;
                }
            }
        }

        private AccessWindow(AccessClient client, bool checkExisting, AccessDecision initialDecision)
        {
            this.client = client;
            Title = "Doka Revit Automation — Access";
            Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var panel = new StackPanel { Margin = new Thickness(24) };
            panel.Children.Add(new TextBlock { Text = "Sign in to use the tools", FontSize = 22, Margin = new Thickness(0, 0, 0, 16) });
            controls.Children.Add(new TextBlock { Text = "Email address" });
            controls.Children.Add(email);
            AddButton("Send sign-in code", async () =>
            {
                string address = email.Text.Trim();
                if (string.IsNullOrWhiteSpace(address)) { status.Text = "Enter your email address."; return; }
                approvedForRevitSession = false;
                await client.SendCode(address, cancellation.Token);
                requestedEmail = address;
                code.Clear();
                status.Text = "Check your email for a sign-in code.";
                code.Focus();
            });
            controls.Children.Add(new TextBlock { Text = "Sign-in code", Margin = new Thickness(0, 12, 0, 0) });
            controls.Children.Add(code);
            AddButton("Sign in", async () =>
            {
                if (requestedEmail == null || !string.Equals(requestedEmail, email.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                { status.Text = "Request a code for this email address first."; return; }
                if (string.IsNullOrWhiteSpace(code.Text)) { status.Text = "Enter the code from your email."; return; }
                approvedForRevitSession = false;
                await client.VerifyCode(requestedEmail, code.Text.Trim(), cancellation.Token);
                code.Clear();
                await Check();
            });
            AddButton("Retry access check", Check);
            AddButton("Sign out / switch account", async () =>
            {
                approvedForRevitSession = false;
                requestedEmail = null; code.Clear();
                await client.SignOut(cancellation.Token);
                status.Text = "Signed out. Enter your email to sign in.";
            });
            panel.Children.Add(controls);
            panel.Children.Add(status);
            var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 6, 10, 6) };
            cancel.Click += (sender, args) => Close();
            panel.Children.Add(cancel);
            Content = panel;
            Closed += (sender, args) => { closed = true; cancellation.Cancel(); };
            Loaded += async (sender, args) =>
            {
                if (initialDecision != null) ShowDecision(initialDecision);
                else if (client.HasSession && checkExisting) await Run(Check);
                else status.Text = client.HasSession ? "You are signed in. You can sign out or switch accounts here."
                    : "Enter your email to receive a one-time sign-in code. Access requires the tool owner's approval.";
            };
        }

        private void AddButton(string label, Func<Task> action)
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(10, 6, 10, 6) };
            button.Click += async (sender, args) => await Run(action);
            controls.Children.Add(button);
        }

        private async Task Run(Func<Task> action)
        {
            controls.IsEnabled = false;
            status.Text = "Please wait…";
            try { await action(); }
            catch (OperationCanceledException) { if (!closed) status.Text = "The request timed out. Please retry."; }
            catch (InvalidOperationException ex) { if (!closed) status.Text = ex.Message; }
            catch { if (!closed) status.Text = "Access cannot be checked right now. Check your connection and retry."; }
            finally { if (!closed) controls.IsEnabled = true; }
        }

        private async Task Check()
        {
            approvedForRevitSession = false;
            var result = await client.Check(cancellation.Token);
            if (closed) return;
            if (result.Allowed) { approvedForRevitSession = true; DialogResult = true; return; }
            ShowDecision(result);
        }

        private void ShowDecision(AccessDecision result)
        {
            switch (result.Reason)
            {
                case "sign_in_required": status.Text = "Sign in with an email code to continue."; break;
                case "verified_email_required": status.Text = "Please sign out and verify your email with a new code."; break;
                case "access_not_granted": status.Text = "Access hasn't been granted. Contact the tool owner for approval, then retry."; break;
                case "access_expired": status.Text = "Your access has expired. Contact the tool owner."; break;
                default: status.Text = "Access cannot be checked right now. Please retry."; break;
            }
        }
    }
}
