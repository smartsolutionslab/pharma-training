using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Keycloak's administrator signs in with a second factor (#278). kcadm.sh signs in with the password grant alone and has
/// no way to send a one-time code, and the master realm's pages, where a browser would set one up, are not published. So
/// the published image carries <c>kcadm-otp.sh</c> beside kcadm.sh: <c>enroll &lt;user&gt;</c> gives a master administrator a
/// time-based one-time code, shown once as a key for an authenticator app and saved only after the app's first code
/// matches; <c>login &lt;user&gt;</c> signs in with the password and a code and leaves kcadm.sh signed in as that user.
/// Once enrolled, the administrator can no longer sign in with the password alone: Keycloak's direct grant asks every user
/// who has a one-time code for it.
/// </summary>
public partial class KeycloakAdminOtpTests(KeycloakAdminContainer keycloak) : IClassFixture<KeycloakAdminContainer>
{
    [Fact]
    public async Task An_enrolled_administrator_signs_in_with_password_and_code_and_kcadm_works_as_them()
    {
        var (user, password) = await AdministratorAsync();
        var key = await EnrollAsync(user);

        var login = await LoginAsync(user, password, Totp.Code(key));

        login.ExitCode.ShouldBe(0, login.ToString());
        var whoAmI = await keycloak.ExecAsync($"./kcadm.sh get users -r master -q username={user} -q exact=true --fields username --format csv --noquotes");
        whoAmI.ExitCode.ShouldBe(0, whoAmI.ToString());
        whoAmI.Output.Trim().ShouldBe(user);
        var session = await keycloak.ExecAsync("grep -o '\"realm\" : \"[a-z]*\"' ~/.keycloak/kcadm.config; stat -c '%a' ~/.keycloak/kcadm.config");
        session.Output.ShouldContain("\"realm\" : \"master\"");
        session.Output.ShouldEndWith("600\n", Case.Sensitive, "kcadm's session holds the administrator's tokens; only its owner may read it.");
    }

    [Fact]
    public async Task An_enrolled_administrator_can_no_longer_sign_in_with_the_password_alone()
    {
        var (user, password) = await AdministratorAsync();
        await EnrollAsync(user);

        var signIn = await keycloak.ExecAsync(
            $"./kcadm.sh config credentials --server http://localhost:8080 --realm master --user {user}",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["KC_CLI_PASSWORD"] = password });

        signIn.ExitCode.ShouldNotBe(0, $"kcadm.sh signed in with the password alone:\n{signIn}");
        signIn.Error.ShouldContain("Invalid user credentials");
    }

    [Fact]
    public async Task Enrolment_ends_the_sessions_the_administrator_signed_in_to_with_the_password_alone()
    {
        var (user, password) = await AdministratorAsync();
        var earlier = await keycloak.ExecAsync(
            $"./kcadm.sh config credentials --config /tmp/{user}.config --server http://localhost:8080 --realm master --user {user}",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["KC_CLI_PASSWORD"] = password });
        earlier.ExitCode.ShouldBe(0, earlier.ToString());

        await EnrollAsync(user);

        var reused = await keycloak.ExecAsync($"./kcadm.sh get realms/master --config /tmp/{user}.config --fields realm");
        reused.ExitCode.ShouldNotBe(0, $"A session signed in with the password alone outlived the enrolment:\n{reused}");
    }

    [Fact]
    public async Task A_wrong_code_at_sign_in_is_refused_and_leaves_kcadms_session_as_it_was()
    {
        var (user, password) = await AdministratorAsync();
        var key = await EnrollAsync(user);
        (await keycloak.SignInAsAdministratorAsync()).ExitCode.ShouldBe(0);
        var before = await keycloak.ExecAsync("sha256sum ~/.keycloak/kcadm.config");

        var login = await LoginAsync(user, password, WrongCode(key));

        login.ExitCode.ShouldNotBe(0, $"kcadm-otp.sh signed in with a wrong code:\n{login}");
        (await keycloak.ExecAsync("sha256sum ~/.keycloak/kcadm.config")).Output.ShouldBe(before.Output, "A refused sign-in should leave kcadm's session as it was.");
    }

    /// <summary>
    /// The review of #319: master's access token lasts 60 seconds, less than an operator may take to scan the key and type
    /// the code, so enrolment saves with a token refreshed from kcadm.sh's session, not the one it started with. Here the
    /// token lasts 5 seconds and the operator takes 8.
    /// </summary>
    [Fact]
    public async Task Enrolment_saves_after_the_sessions_access_token_has_expired()
    {
        var (user, _) = await AdministratorAsync();
        (await keycloak.ExecAsync("./kcadm.sh update realms/master -s accessTokenLifespan=5")).ExitCode.ShouldBe(0);
        try
        {
            (await keycloak.SignInAsAdministratorAsync()).ExitCode.ShouldBe(0);
            string? key = null;

            var enroll = await keycloak.ConverseAsync($"./kcadm-otp.sh enroll {user}", line =>
            {
                key = KeyIn(line);
                if (key is null)
                {
                    return null;
                }

                Thread.Sleep(TimeSpan.FromSeconds(8));
                return Totp.Code(key) + "\n";
            });

            enroll.ExitCode.ShouldBe(0, $"Enrolment failed once the access token it started with had expired:\n{enroll}");
            (await CredentialTypesAsync(user)).ShouldContain("otp");
        }
        finally
        {
            (await keycloak.SignInAsAdministratorAsync()).ExitCode.ShouldBe(0);
            (await keycloak.ExecAsync("./kcadm.sh update realms/master -s accessTokenLifespan=60")).ExitCode.ShouldBe(0);
        }
    }

    /// <summary>
    /// #331: enrolment refreshes kcadm.sh's session for its own calls and writes the new tokens back, so a realm that
    /// revokes a refresh token once it is used ("Revoke Refresh Token") does not end kcadm.sh's own session.
    /// </summary>
    [Fact]
    public async Task Enrolment_leaves_kcadms_session_working_where_refresh_tokens_are_revoked()
    {
        var (user, _) = await AdministratorAsync();
        (await keycloak.ExecAsync("./kcadm.sh update realms/master -s revokeRefreshToken=true -s accessTokenLifespan=5")).ExitCode.ShouldBe(0);
        try
        {
            (await keycloak.SignInAsAdministratorAsync()).ExitCode.ShouldBe(0);

            var enroll = await keycloak.ConverseAsync($"./kcadm-otp.sh enroll {user}", line => KeyIn(line) is { } key ? Totp.Code(key) + "\n" : null);
            enroll.ExitCode.ShouldBe(0, enroll.ToString());
            await Task.Delay(TimeSpan.FromSeconds(7), TestContext.Current.CancellationToken);

            var afterwards = await keycloak.ExecAsync("./kcadm.sh get realms/master --fields realm");
            afterwards.ExitCode.ShouldBe(0, $"kcadm.sh's session ended with the refresh token enrolment used up:\n{afterwards}");
        }
        finally
        {
            (await keycloak.SignInAsAdministratorAsync()).ExitCode.ShouldBe(0);
            (await keycloak.ExecAsync("./kcadm.sh update realms/master -s revokeRefreshToken=false -s accessTokenLifespan=60")).ExitCode.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Login_keeps_kcadms_sessions_with_other_servers()
    {
        var (user, password) = await AdministratorAsync();
        var key = await EnrollAsync(user);
        var other = await keycloak.ExecAsync(
            "sed -i 's|\"endpoints\" : {|\"endpoints\" : { \"http://other.example:8080\" : { \"master\" : { \"clientId\" : \"admin-cli\", \"token\" : \"other-token\" } },|' ~/.keycloak/kcadm.config && grep -c other.example ~/.keycloak/kcadm.config");
        other.Output.Trim().ShouldBe("1", $"The test could not add another server's session:\n{other}");

        var login = await LoginAsync(user, password, Totp.Code(key));

        login.ExitCode.ShouldBe(0, login.ToString());
        var config = await keycloak.ExecAsync("cat ~/.keycloak/kcadm.config");
        config.Output.ShouldContain("http://other.example:8080", Case.Sensitive, "kcadm-otp.sh login dropped kcadm.sh's session with another server.");
        config.Output.ShouldContain("other-token");
        (await keycloak.ExecAsync($"./kcadm.sh get users -r master -q username={user} -q exact=true --fields username --format csv --noquotes")).Output.Trim().ShouldBe(user);
    }

    /// <summary>
    /// #331: with its output redirected there is no console, and the password would be read from the terminal with echo,
    /// left on the screen; kcadm-otp.sh refuses instead of asking.
    /// </summary>
    [Fact]
    public async Task Login_refuses_to_ask_for_a_password_the_terminal_would_echo()
    {
        var (user, _) = await AdministratorAsync();

        var run = await keycloak.ExecInTerminalAsync($"timeout --foreground -k 5 30 ./kcadm-otp.sh login {user} > /tmp/{user}.out; echo \"exit $?\"");

        run.Output.ShouldNotContain("exit 0");
        run.Output.ShouldNotContain("exit 124", Case.Sensitive, "kcadm-otp.sh waited for a password the terminal would have echoed.");
        run.Output.ShouldContain("echo");
    }

    [Fact]
    public async Task A_wrong_code_at_enrolment_saves_nothing()
    {
        var (user, _) = await AdministratorAsync();
        (await keycloak.SignInAsAdministratorAsync()).ExitCode.ShouldBe(0);

        string? shown = null;

        var enroll = await keycloak.ConverseAsync($"./kcadm-otp.sh enroll {user}", line => (shown ??= KeyIn(line)) is { } key ? WrongCode(key) + "\n" : null);

        shown.ShouldNotBeNull($"Enrolment showed no otpauth:// link with the key:\n{enroll}");
        enroll.ExitCode.ShouldNotBe(0, $"Enrolment saved a code the app never showed:\n{enroll}");
        (await CredentialTypesAsync(user)).ShouldNotContain("otp");
    }

    [Fact]
    public async Task Enrolment_refuses_an_administrator_who_already_has_a_code()
    {
        var (user, _) = await AdministratorAsync();
        await EnrollAsync(user);
        (await keycloak.SignInAsAdministratorAsync()).ExitCode.ShouldBe(0);

        var again = await keycloak.ConverseAsync($"./kcadm-otp.sh enroll {user}", line => KeyIn(line) is { } key ? Totp.Code(key) + "\n" : null);

        again.ExitCode.ShouldNotBe(0, $"A second code was enrolled:\n{again}");
        (await CredentialTypesAsync(user)).Count(type => type == "otp").ShouldBe(1);
    }

    [Fact]
    public async Task Enrolment_keeps_the_administrators_password_and_profile()
    {
        var (user, _) = await AdministratorAsync();
        (await keycloak.ExecAsync($"./kcadm.sh update users/$(./kcadm.sh get users -r master -q username={user} -q exact=true --fields id --format csv --noquotes) -r master -s email={user}@example.com -s firstName=Ops")).ExitCode.ShouldBe(0);

        await EnrollAsync(user);

        (await CredentialTypesAsync(user)).ShouldBe(["password", "otp"], ignoreOrder: true);
        var profile = await keycloak.ExecAsync($"./kcadm.sh get users -r master -q username={user} -q exact=true --fields email,firstName --format csv --noquotes");
        profile.Output.Trim().ShouldBe($"{user}@example.com,Ops");
    }

    private static string? KeyIn(string line) => OtpAuthKey().Match(line) is { Success: true } match ? match.Groups["key"].Value : null;

    /// <summary>A code the app shows ten periods from now, which no check of the current periods accepts.</summary>
    private static string WrongCode(string key) => Totp.Code(key, 10);

    private async Task<(string User, string Password)> AdministratorAsync()
    {
        var user = $"ops-{Guid.CreateVersion7():N}"[..16];
        var password = Guid.CreateVersion7().ToString("N");
        await keycloak.CreateAdministratorAsync(user, password);
        return (user, password);
    }

    /// <summary>Enrols the user as an operator would, signed in as the bootstrap administrator, and returns the key the app was given.</summary>
    private async Task<string> EnrollAsync(string user)
    {
        (await keycloak.SignInAsAdministratorAsync()).ExitCode.ShouldBe(0);
        string? key = null;

        var enroll = await keycloak.ConverseAsync($"./kcadm-otp.sh enroll {user}", line =>
        {
            key = KeyIn(line);
            return key is null ? null : Totp.Code(key) + "\n";
        });

        enroll.ExitCode.ShouldBe(0, enroll.ToString());
        key.ShouldNotBeNull($"Enrolment showed no otpauth:// link with the key:\n{enroll}");
        (await CredentialTypesAsync(user)).ShouldContain("otp");
        return key;
    }

    private Task<CommandRun> LoginAsync(string user, string password, string code) =>
        keycloak.ExecAsync($"./kcadm-otp.sh login {user}", input: $"{password}\n{code}\n");

    private async Task<List<string>> CredentialTypesAsync(string user)
    {
        (await keycloak.SignInAsAdministratorAsync()).ExitCode.ShouldBe(0);
        var credentials = await keycloak.ExecAsync(
            $"./kcadm.sh get users/$(./kcadm.sh get users -r master -q username={user} -q exact=true --fields id --format csv --noquotes)/credentials -r master --fields type --format csv --noquotes");
        credentials.ExitCode.ShouldBe(0, credentials.ToString());
        return [.. credentials.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    [GeneratedRegex(@"otpauth://totp/\S*[?&]secret=(?<key>[A-Z2-7]+)")]
    private static partial Regex OtpAuthKey();
}
