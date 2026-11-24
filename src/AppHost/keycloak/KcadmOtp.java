import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ArrayNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import java.io.BufferedReader;
import java.io.Console;
import java.io.IOException;
import java.io.InputStreamReader;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.nio.file.attribute.PosixFilePermissions;
import java.security.GeneralSecurityException;
import java.security.SecureRandom;
import java.util.Map;
import java.util.stream.Collectors;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/**
 * A one-time code for Keycloak's administrators (#278, docs/adr/0005). kcadm.sh signs in with the password grant alone and
 * has no way to send a code, and the master realm's pages, where a browser would set one up, are not published. Run inside
 * Keycloak's container, through kcadm-otp.sh:
 *
 * <ul>
 *   <li>{@code enroll <user>}: signed in to kcadm.sh as an administrator, gives a master realm user a time-based one-time
 *       code (HMAC-SHA1, 6 digits, 30 seconds, as every authenticator app computes it). The key is shown once, as text and
 *       as an otpauth:// link, and saved only after the app's code matches; a user who has a code already is refused. Saving ends the user's
 *       sessions, which were signed in to without the code.
 *   <li>{@code login <user>}: asks for the password and a code, signs in to master's admin-cli with both, and writes
 *       kcadm.sh's session (~/.keycloak/kcadm.config, readable by its owner alone) as {@code kcadm.sh config credentials}
 *       would. A refused sign-in leaves the session as it was.
 * </ul>
 *
 * Keycloak's direct grant asks every user who has a code for it, so once enrolled the password alone no longer signs in.
 * Passwords and codes are read without echo where there is a terminal, and never taken as arguments.
 */
public final class KcadmOtp {
    private static final String SERVER = "http://localhost:8080";
    private static final String REALM = "master";
    private static final String CLIENT = "admin-cli";
    private static final String ISSUER = "Keycloak master";
    private static final String LABEL = "kcadm-otp";
    private static final String ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private static final String BASE32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private static final int SECRET_LENGTH = 20;
    private static final int DIGITS = 6;
    private static final int PERIOD = 30;
    private static final int ATTEMPTS = 3;
    private static final Path CONFIG = Path.of(System.getProperty("user.home"), ".keycloak", "kcadm.config");
    private static final ObjectMapper JSON = new ObjectMapper();
    private static final HttpClient HTTP = HttpClient.newHttpClient();
    private static final Console CONSOLE = System.console();
    private static final BufferedReader INPUT = new BufferedReader(new InputStreamReader(System.in, StandardCharsets.UTF_8));

    private KcadmOtp() {
    }

    public static void main(String[] arguments) throws Exception {
        if (arguments.length != 2 || !(arguments[0].equals("enroll") || arguments[0].equals("login"))) {
            System.err.println("Usage: kcadm-otp.sh enroll <user>   give a master administrator a one-time code (signed in to kcadm.sh)");
            System.err.println("       kcadm-otp.sh login <user>    sign kcadm.sh in with the password and a code");
            System.exit(2);
        }

        try {
            if (arguments[0].equals("enroll")) {
                enroll(arguments[1]);
            } else {
                login(arguments[1]);
            }
        } catch (Refusal refusal) {
            System.err.println(refusal.getMessage());
            System.exit(1);
        }
    }

    private static void enroll(String user) throws Exception {
        var token = sessionToken();
        var id = userId(token, user);
        for (var credential : send(token, "GET", "/users/" + id + "/credentials", null)) {
            if (credential.path("type").asText().equals("otp")) {
                throw new Refusal(user + " has a one-time code already (" + credential.path("userLabel").asText() + "). To replace it, delete it first:"
                    + " ./kcadm.sh delete users/" + id + "/credentials/" + credential.path("id").asText() + " -r " + REALM);
            }
        }

        var secret = randomSecret();
        var key = base32(secret.getBytes(StandardCharsets.US_ASCII));
        System.out.println("Add this key to " + user + "'s authenticator app (time-based, SHA1, " + DIGITS + " digits, " + PERIOD + " seconds):");
        System.out.println("  " + key.replaceAll("(.{4})(?!$)", "$1 "));
        System.out.println("or open this link with it:");
        System.out.println("  otpauth://totp/" + encode(ISSUER + ":" + user) + "?secret=" + key + "&issuer=" + encode(ISSUER)
            + "&algorithm=SHA1&digits=" + DIGITS + "&period=" + PERIOD);
        System.out.flush();

        for (var attempt = 1; attempt <= ATTEMPTS; attempt++) {
            var code = ask("Code the app shows now: ", false);
            if (code == null) {
                throw new Refusal("No code was entered; nothing was saved.");
            }

            if (matches(secret, code.strip())) {
                // The token enrolment started with may have expired while the operator scanned the key.
                save(sessionToken(), id, secret);
                System.out.println("Saved. " + user + " now signs in with ./kcadm-otp.sh login " + user + "; the password alone no longer does.");
                return;
            }

            System.out.println("That code does not match the key; check the app's clock and the key, and try again.");
        }

        throw new Refusal("No code matched in " + ATTEMPTS + " attempts; nothing was saved.");
    }

    private static void login(String user) throws Exception {
        var password = ask("Password of " + user + ": ", true);
        var code = password == null ? null : ask("Code the app shows now: ", false);
        if (code == null) {
            throw new Refusal("No password or code was entered; kcadm.sh's session is as it was.");
        }

        var response = token(Map.of("grant_type", "password", "client_id", CLIENT, "username", user, "password", password, "otp", code.strip()));
        var answer = JSON.readTree(response.body());
        if (response.statusCode() != 200) {
            throw new Refusal("Keycloak refused the sign-in (" + answer.path("error_description").asText(answer.path("error").asText("HTTP " + response.statusCode()))
                + "); kcadm.sh's session is as it was.");
        }

        var now = System.currentTimeMillis();
        var endpoint = JSON.createObjectNode()
            .put("clientId", CLIENT)
            .put("token", answer.path("access_token").asText())
            .put("refreshToken", answer.path("refresh_token").asText())
            .put("grantTypeForAuthentication", "password")
            .put("expiresAt", now + (answer.path("expires_in").asLong() * 1000))
            .put("refreshExpiresAt", now + (answer.path("refresh_expires_in").asLong() * 1000));
        var config = JSON.createObjectNode().put("serverUrl", SERVER).put("realm", REALM);
        config.putObject("endpoints").putObject(SERVER).set(REALM, endpoint);
        writeSession(JSON.writerWithDefaultPrettyPrinter().writeValueAsString(config));
        System.out.println("Signed in as " + user + "; ./kcadm.sh works as " + user + " until the session ends.");
    }

    /**
     * A fresh access token for kcadm.sh's current session with master, from its refresh token: master's access tokens last
     * 60 seconds, less than an operator may take between starting enrolment and typing the code (the review of #319), so
     * each use asks for a new one. The session itself is left as it is.
     */
    private static String sessionToken() throws Exception {
        var signIn = "Sign in to kcadm.sh as an administrator first: ./kcadm.sh config credentials --server " + SERVER + " --realm " + REALM + " --user <administrator>";
        if (!Files.exists(CONFIG)) {
            throw new Refusal(signIn);
        }

        var session = JSON.readTree(CONFIG.toFile()).path("endpoints").path(SERVER).path(REALM);
        var refreshToken = session.path("refreshToken").asText();
        if (refreshToken.isEmpty() || !session.path("clientId").asText(CLIENT).equals(CLIENT)) {
            throw new Refusal(signIn + " (kcadm.sh has no session with master's " + CLIENT + " on " + SERVER + ").");
        }

        var response = token(Map.of("grant_type", "refresh_token", "client_id", CLIENT, "refresh_token", refreshToken));
        var answer = JSON.readTree(response.body());
        if (response.statusCode() != 200) {
            throw new Refusal(signIn + " (kcadm.sh's session has ended: " + answer.path("error_description").asText(answer.path("error").asText("HTTP " + response.statusCode())) + ").");
        }

        return answer.path("access_token").asText();
    }

    /** Asks master's token endpoint with the form given. */
    private static HttpResponse<String> token(Map<String, String> form) throws IOException, InterruptedException {
        var request = HttpRequest.newBuilder(URI.create(SERVER + "/realms/" + REALM + "/protocol/openid-connect/token"))
            .header("Content-Type", "application/x-www-form-urlencoded")
            .POST(HttpRequest.BodyPublishers.ofString(form.entrySet().stream()
                .map(entry -> encode(entry.getKey()) + "=" + encode(entry.getValue()))
                .collect(Collectors.joining("&"))))
            .build();
        return HTTP.send(request, HttpResponse.BodyHandlers.ofString());
    }

    private static String userId(String token, String user) throws Exception {
        var users = send(token, "GET", "/users?exact=true&username=" + encode(user), null);
        if (users.size() != 1) {
            throw new Refusal("The master realm has no user " + user + ".");
        }

        return users.get(0).path("id").asText();
    }

    /**
     * Adds the code to the user's credentials, sending the user as it is so the update changes nothing else, and ends the
     * user's sessions, which were signed in to without it.
     */
    private static void save(String token, String id, String secret) throws Exception {
        var user = (ObjectNode) send(token, "GET", "/users/" + id, null);
        var data = JSON.createObjectNode().put("subType", "totp").put("digits", DIGITS).put("counter", 0).put("period", PERIOD).put("algorithm", "HmacSHA1");
        var credential = JSON.createObjectNode()
            .put("type", "otp")
            .put("userLabel", LABEL)
            .put("secretData", JSON.writeValueAsString(JSON.createObjectNode().put("value", secret)))
            .put("credentialData", JSON.writeValueAsString(data));
        ArrayNode credentials = user.putArray("credentials");
        credentials.add(credential);
        send(token, "PUT", "/users/" + id, user);

        var saved = false;
        for (var stored : send(token, "GET", "/users/" + id + "/credentials", null)) {
            saved |= stored.path("type").asText().equals("otp");
        }

        if (!saved) {
            throw new Refusal("Keycloak accepted the code but does not list it among the user's credentials; nothing was saved.");
        }

        // A session someone signed in to with the password alone, before the code, would outlive it: end them all.
        send(token, "POST", "/users/" + id + "/logout", null);
    }

    private static JsonNode send(String token, String method, String path, JsonNode body) throws Exception {
        var request = HttpRequest.newBuilder(URI.create(SERVER + "/admin/realms/" + REALM + path))
            .header("Authorization", "Bearer " + token)
            .header("Content-Type", "application/json")
            .method(method, body == null ? HttpRequest.BodyPublishers.noBody() : HttpRequest.BodyPublishers.ofString(JSON.writeValueAsString(body)))
            .build();
        var response = HTTP.send(request, HttpResponse.BodyHandlers.ofString());
        if (response.statusCode() / 100 != 2) {
            throw new Refusal(method + " " + path + " answered " + response.statusCode() + ": " + response.body());
        }

        return response.body().isBlank() ? JSON.nullNode() : JSON.readTree(response.body());
    }

    /** Writes kcadm.sh's session readable by its owner alone, replacing the old one only once the new one is complete. */
    private static void writeSession(String content) throws IOException {
        Files.createDirectories(CONFIG.getParent());
        var temporary = Files.createTempFile(CONFIG.getParent(), "kcadm", ".config", PosixFilePermissions.asFileAttribute(PosixFilePermissions.fromString("rw-------")));
        Files.writeString(temporary, content, StandardCharsets.UTF_8);
        Files.move(temporary, CONFIG, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
    }

    private static String ask(String prompt, boolean hidden) throws IOException {
        if (CONSOLE != null) {
            if (hidden) {
                var answer = CONSOLE.readPassword("%s", prompt);
                return answer == null ? null : new String(answer);
            }

            return CONSOLE.readLine("%s", prompt);
        }

        System.out.print(prompt);
        System.out.flush();
        var answer = INPUT.readLine();
        System.out.println();
        return answer;
    }

    /** Whether the code is the one for this period or the one before or after, the drift Keycloak itself allows. */
    private static boolean matches(String secret, String code) throws GeneralSecurityException {
        if (!code.matches("\\d{" + DIGITS + "}")) {
            return false;
        }

        var counter = System.currentTimeMillis() / 1000 / PERIOD;
        for (var drift = -1; drift <= 1; drift++) {
            if (code(secret, counter + drift).equals(code)) {
                return true;
            }
        }

        return false;
    }

    private static String code(String secret, long counter) throws GeneralSecurityException {
        var mac = Mac.getInstance("HmacSHA1");
        mac.init(new SecretKeySpec(secret.getBytes(StandardCharsets.US_ASCII), "HmacSHA1"));
        var hash = mac.doFinal(java.nio.ByteBuffer.allocate(8).putLong(counter).array());
        var offset = hash[hash.length - 1] & 0x0f;
        var value = ((hash[offset] & 0x7f) << 24) | ((hash[offset + 1] & 0xff) << 16) | ((hash[offset + 2] & 0xff) << 8) | (hash[offset + 3] & 0xff);
        return String.format("%0" + DIGITS + "d", value % (int) Math.pow(10, DIGITS));
    }

    /** As Keycloak makes one: letters and digits, about 119 bits. */
    private static String randomSecret() {
        var random = new SecureRandom();
        var secret = new StringBuilder(SECRET_LENGTH);
        for (var index = 0; index < SECRET_LENGTH; index++) {
            secret.append(ALPHABET.charAt(random.nextInt(ALPHABET.length())));
        }

        return secret.toString();
    }

    private static String base32(byte[] bytes) {
        var encoded = new StringBuilder();
        var buffer = 0;
        var bits = 0;
        for (var value : bytes) {
            buffer = (buffer << 8) | (value & 0xff);
            bits += 8;
            while (bits >= 5) {
                bits -= 5;
                encoded.append(BASE32.charAt((buffer >> bits) & 0x1f));
            }
        }

        if (bits > 0) {
            encoded.append(BASE32.charAt((buffer << (5 - bits)) & 0x1f));
        }

        return encoded.toString();
    }

    private static String encode(String value) {
        return URLEncoder.encode(value, StandardCharsets.UTF_8).replace("+", "%20");
    }

    /** A refusal the operator can act on, printed without a stack trace. */
    private static final class Refusal extends Exception {
        Refusal(String message) {
            super(message);
        }
    }
}
