# YouTube Integration Detailed Design

Status: draft (2026-11-21). To be settled after talking through the decisions (chapter 17) and the risks (chapter 13) with the user. The system design changes (chapter 18) are made when it is settled.

## 1. Purpose

Connect the user's YouTube account to the app, so streaming can be handled inside it (the "later stage" of system design 10.11).

The first goal is showing the stream's comments on the stream (user's request, 2026-11-21; many streamers overlay their comments with outside tools).

Stages:

1. Stage 1 (this document): sign in with a Google account (OAuth), find the user's own broadcast that is live now, receive its comments, and show them in a frame on the stream.
2. Stage 2 (later): create broadcasts, set the title and privacy, start and end the broadcast through the YouTube API. It needs write access (the `youtube` or `youtube.force-ssl` scope), so it stays apart from stage 1 and asks for the added access when it is needed (incremental authorization).
3. Stage 3 (later): take the stream key from the API instead (the user no longer pastes it).

Not in this design: replying to and moderating comments (deleting, timeouts), Super Chat totals, other services' comments (Twitch and others). The comment type and the display are service-independent, though (chapter 5).

## 2. Responsibilities

| Responsibility | Owner |
|---|---|
| Signing in with a Google account (OAuth 2.0), refreshing and revoking tokens | Streaming (`YouTubeAccount`) |
| Keeping the token | ProjectData (`ICredentialStore`, Windows Credential Manager; system design 6.13) |
| Finding the user's live broadcast, receiving its comments | Streaming (`YouTubeLiveChat`) |
| Turning comments into a service-independent form (`LiveChatMessage`) | Streaming |
| Filtering comments (blocked words, how many, length) | Streaming (`ChatFilter`, pure functions) |
| Drawing the comments into an image | UI (`CommentOverlay`, a UI Toolkit panel drawn into a texture) |
| Where on the stream they go (a frame) | Stage (`CommentFrame`, an `IFrameSource`, like the avatar frame) |
| Signing in, display settings (setup screen), showing comments on or off (live screen) | UI |

## 3. Module Boundaries

- The YouTube API stays inside the Streaming module, under `Streaming/YouTube/`, as system design 10.11 places YouTube integration with streaming. No new module.
- Other modules see only `IYouTubeAccount` (the sign-in state and operations), `ILiveChat` (the flow of comments), and the value type `LiveChatMessage`. The YouTube API's JSON, tokens, and HTTP types stay inside.
- `ILiveChat` is service-independent. Twitch and others come later as further implementations of it (CLAUDE.md chapter 5).
- UI draws the comments, Stage decides where they go, Video puts them on the stream (the split CLAUDE.md chapter 6 uses for Capture). Stage does not know YouTube.
- No outside processes or Python. HTTPS goes through .NET's `HttpClient` (as Voice Lab's downloads).

## 4. Main Classes and Interfaces

| Class | Kind | Role |
|---|---|---|
| `IYouTubeAccount` | Interface (Streaming) | The sign-in state (signed out, signing in, signed in, failed), the channel name, sign in, sign out |
| `YouTubeAccount` | Class (Streaming) | OAuth 2.0 (loopback redirect and PKCE), keeping, refreshing, and revoking the token |
| `LoopbackRedirect` | Class (Streaming) | Takes Google's redirect once, on a free port of `127.0.0.1` |
| `OAuthMath` | static (Streaming) | PKCE values, the authorization URL, checking the redirect URL (matching `state`). Pure functions |
| `YouTubeApi` | Class (Streaming) | REST calls: the user's broadcasts (`liveBroadcasts.list`), the comment stream (`liveChatMessages.streamList`) |
| `ILiveChat` | Interface (Streaming) | The flow of comments: state, recent comments, start and stop |
| `YouTubeLiveChat` | Class (Streaming) | When the stream goes live, finds the user's broadcast, receives its comments, and reconnects when cut |
| `LiveChatMessage` | Record (Streaming) | A service-independent comment: ID, time, name, text, kind (normal, Super Chat, and so on), whether from the streamer, a moderator, a member |
| `ChatFilter` | static (Streaming) | Blocked words, removing control characters, length limit. Pure functions |
| `CommentOverlay` | Class (UI) | Draws recent comments into a texture with a UI Toolkit panel |
| `CommentFrame` | Class (Stage) | The comments frame in the placement editor (`IFrameSource`); puts the texture on the flat video layer |

## 5. Public API

```csharp
public interface IYouTubeAccount
{
    IReadOnlyObservable<YouTubeAccountState> State { get; } // SignedOut, SigningIn, SignedIn, Failed
    string ChannelTitle { get; }                            // The channel name when signed in; null otherwise
    Task<YouTubeAccountProblem> SignInAsync(CancellationToken cancellationToken); // Opens the default browser and waits
    Task SignOutAsync();                                    // Revokes the token at Google and deletes it
}

public interface ILiveChat
{
    IReadOnlyObservable<LiveChatState> State { get; }       // Off, Waiting (looking for the broadcast), Receiving, Unavailable
    IReadOnlyObservable<LiveChatProblem> Problem { get; }    // chapter 12
    IReadOnlyList<LiveChatMessage> Recent { get; }          // The latest after filtering (up to 50); read on the main thread
    event Action Received;                                  // After new comments; on the main thread
    void Start();                                           // Called by the composition when the stream starts
    void Stop();
}
```

- The composition calls `ILiveChat.Start` following the stream's state (`IStreamingService.State` is Live), and does not while the user has comments turned off.
- Received comments are handed from the receiving thread to the main thread (a `ConcurrentQueue` and the frame loop).

## 6. Data Model

### 6.1 Settings

Added to `stream` in `Profiles/Streaming/<id>.json` (schema version stays 1; missing fields take defaults):

```json
"youtube": {
  "credentialId": "<32 hex digits>",
  "showComments": true,
  "comments": { "max": 8, "ngWords": ["..."], "showNames": true }
}
```

- `credentialId` is the ID of the credential holding the token (the same form as the stream key; streaming detailed design chapter 11). The token itself is never in the profile.
- The frame's place and size are `screens.comments` in `stage-settings.json` (the form of stage detailed design 6.3).

### 6.2 Tokens

- Only the refresh token is kept. The access token (about an hour) is only in memory.
- The Credential Manager target name is `VirtualVesselStudio/youtube/<credentialId>`, for this user on this PC only (`CRED_PERSIST_LOCAL_MACHINE`). It fits the size limit (about 2.5 KB).

### 6.3 Registering the app (the client ID)

- A Google Cloud project holds an OAuth client of the type "Desktop app". Google states that a client secret built into a desktop app cannot be kept secret, so PKCE protects the flow (7.1).
- The client ID and secret are not in the repository. Release builds embed them from a GitHub secret. Developers put their own project's values in a development settings file outside the repository. Forks then do not use this project's quota.
- A build without them says "Not available in this build" on the YouTube screens; everything else works.

## 7. State and Lifecycle

### 7.1 Signing in (OAuth 2.0 for installed apps)

1. `LoopbackRedirect` waits on a free port of `127.0.0.1` (`TcpListener`; needs no administrator rights or URL reservation).
2. A PKCE `code_verifier` (43 to 128 random characters), its `S256` `code_challenge`, and a random `state` are made.
3. The default browser opens Google's authorization URL (the `youtube.readonly` scope only). No web view inside the app: Google forbids it, as the user cannot check the address there.
4. When the user allows it, the browser comes back to `http://127.0.0.1:<port>/?code=...&state=...`. A different `state`, an `error`, or five minutes passing is a failure. Only the first request is taken, and the waiting stops at once. The browser gets a short page saying to go back to the app.
5. The authorization code and `code_verifier` get the tokens (`https://oauth2.googleapis.com/token`).
6. The refresh token is saved in Credential Manager, and `channels.list mine=true` gives the channel name.

- Signing out first revokes the token at Google (`https://oauth2.googleapis.com/revoke`), then deletes it. When revoking fails (offline, for example), the token is deleted anyway, and the user is told it can also be removed under "Third-party access" in their Google account.
- When the refresh token stops working (the user removed the access at Google, for example), it is deleted, the account goes back to signed out, and the user is asked to sign in again.

### 7.2 Receiving comments

1. When the stream goes live, `liveBroadcasts.list` (`mine=true`, `broadcastStatus=active`) finds the user's live broadcast and its `liveChatId` (1 unit). If none is found, it looks every 30 seconds, up to 10 times (YouTube takes a while from the stream's start to the broadcast going live).
2. `liveChatMessages.streamList` opens the comment stream (5 units). The server sends new comments over the open connection.
3. When cut, it reconnects from the last `nextPageToken` after a pause (1, 2, 4 seconds... up to 60).
4. When the stream ends, it closes.

- The polled `liveChatMessages.list` is not used. Each call costs 5 units; polling every 5 seconds for two hours is about 7,200 units, and the day's quota (10,000, shared by every user; chapter 13) would soon run out.
- `streamList` works over gRPC and over HTTP. Unity's Mono `HttpClient` has no HTTP/2, so the HTTP form is used. How messages are delimited over HTTP is not documented, so a prototype checks it before implementation (chapter 17).

## 8. Main Sequences

### 8.1 Showing comments

1. `YouTubeLiveChat` receives a comment and makes a `LiveChatMessage`.
2. `ChatFilter` removes control characters and those that change text direction, cuts it to 200 characters, and drops it if it contains a blocked word.
3. On the main thread it joins `Recent`, and `Received` is raised.
4. `CommentOverlay` draws the latest `max` comments again (newest at the bottom). Names and texts are drawn as plain text, never read as rich text.
5. `CommentFrame` puts that texture in the frame set in the placement editor.

- It is drawn again only when comments arrive, not every frame.

## 9. Threads and Asynchrony

- The receiving thread (a `Task`): reads the HTTP stream, parses the JSON, and puts comments in a `ConcurrentQueue`. It never waits for the main thread.
- The main thread: takes comments from the queue in the frame loop (up to 20 a frame).
- Waiting for the sign-in is `async`, with cancellation and a five-minute limit.

## 10. Settings

As in 6.1. A "YouTube" Card on the setup screen's Streaming page (its screen spec comes separately):

- Sign in: "Sign in with Google"; when signed in, the channel name and "Sign out".
- Showing comments: on or off, how many, whether to show names, blocked words.
- The frame moves in the placement editor (as the game screen and the avatar do).

## 11. Storage

- The refresh token: Credential Manager (6.2).
- Comments: not stored. Only the latest 50 are in memory, dropped when the stream ends.
- The channel name: only in memory, for display.

## 12. Errors and Recovery

| What happened | Behavior | What the user is told |
|---|---|---|
| This build has no client ID | YouTube integration cannot be used | Not available in this build |
| Sign-in cancelled, or five minutes passed | Still signed out | Could not sign in. Try again |
| The refresh token no longer works | Deleted; back to signed out | Sign in again |
| No live broadcast found | Looks every 30 seconds; stops after 10 | No broadcast found. Check in YouTube Studio that the broadcast has started |
| The broadcast has comments off | Nothing received | This broadcast has comments turned off |
| Quota exhausted (403 `quotaExceeded`) | Nothing received that day | Comments cannot be received today |
| The connection was cut | Reconnects (7.2) | Nothing; if it goes on, "Comments cannot be received" |

- None of these stops streaming or recording (the same idea as system design 12.11).

## 13. Security and Privacy

To be talked through with the user (awaiting their decision). The mitigations are this document's proposal.

| # | Threat or risk | Mitigation | What remains |
|---|---|---|---|
| S1 | Malware running as the same user reads the refresh token from Credential Manager | The scope is read-only (`youtube.readonly`): a stolen token cannot control the stream or post. Signing out revokes it | Reading itself cannot be prevented (as with the stream key; streaming detailed design chapter 11). The channel's private information (a list of private broadcasts, for example) could be read |
| S2 | Another local program takes the authorization code from the loopback redirect | PKCE (S256), matching a random `state`, `127.0.0.1` only, a fresh free port each time, closed after one request, five-minute limit | With PKCE, the code alone is useless |
| S3 | Someone extracts the client ID and secret from the app and poses as it, or uses up its quota | Values kept out of the repository and embedded in releases only; the consent screen shows the app's name and the access asked for; quota use shown in diagnostics | A desktop app cannot fully prevent it (Google does not treat the secret as secret either). If the quota is used up, comments stop for every user |
| S4 | Attacks through comment content: rich text tags (huge text, images), very long text, characters that change text direction, floods | Drawn as plain text, control and direction characters removed, up to 200 characters, at most `max` shown, links in comments never opened | Visual trolling (strings of emoji) remains; blocked words reduce it |
| S5 | Comments that must not be on the stream (personal information, harassment) appear | Blocked words, a show-comments switch (turned off at once on the live screen), YouTube's own holding (automatic moderation) is respected | Between appearing and being removed, it is on the stream. Delaying the display is not in stage 1 (to discuss) |
| S6 | Viewers' names and comments left on disk or in logs | Not stored; logs hold counts and states only; diagnostics show no content | None |
| S7 | Tracking or image-parsing flaws from loading outside images (avatars) | Stage 1 shows no avatars | None |
| S8 | Eavesdropping or impersonation in transit | HTTPS only; certificates checked by Windows' usual validation, with no setting to skip it | None |
| S9 | Breaking the YouTube API terms (YouTube API Services Terms, Developer Policies) | Publish a privacy policy, do not store data obtained, follow the branding rules, no unofficial methods (parsing pages, internal APIs) | Reviewing the terms, and Google's verification (chapter 17) |
| S10 | Until verified, Google's consent screen says "unverified app", and use is capped at 100 users. While the publishing status is "Testing", only registered test users can use it, and refresh tokens expire after 7 days | Apply for verification (privacy policy, home page, domain ownership, a video) | Cannot be distributed widely until verified |

- Getting comments by unofficial methods (parsing YouTube's pages or its internal API) needs neither sign-in nor quota, but breaks YouTube's terms of service and can break without notice, so it is not used.

## 14. Testing Strategy

| Kind | Subject |
|---|---|
| EditMode | `OAuthMath`: PKCE values (matching RFC 7636's example), matching `state`, checking the redirect URL. `ChatFilter`: control characters, direction characters, length, blocked words. Turning API JSON samples into `LiveChatMessage`. Reconnection intervals. Keeping and deleting the token (a fake credential store). `LoopbackRedirect`: a correct redirect, a wrong `state`, a second request, the time limit |
| PlayMode | Signing in, signing out, and display settings on the setup screen. The comments frame in the placement editor. `CommentOverlay` drawing comments as plain text (a name with tags) |
| Prototype (before implementing) | How HTTP `streamList` delimits messages. With the user's own test broadcast: how long comments take to arrive, and reconnection |
| Hardware (manual) | Signing in and out with a real account, revoking from the Google account page, showing comments during a stream |

- Automated tests never reach the real Google; HTTP goes through a fake handler.

## 15. Performance

- Receiving happens off the main thread. The main thread only draws again when comments arrive.
- The texture matches the frame's size, never above the stream's resolution.
- Quota: per stream, about 1 to 10 units to find the broadcast and 5 for the stream (5 more each reconnection). With 10,000 units, about 1,000 streams a day (all users together).

## 16. Folder / Namespace

```text
Streaming/Runtime/YouTube/   IYouTubeAccount, YouTubeAccount, LoopbackRedirect, OAuthMath, YouTubeApi, YouTubeLiveChat
Streaming/Runtime/Chat/      ILiveChat, LiveChatMessage, ChatFilter
UI/Runtime/Comments/         CommentOverlay, the YouTube Card
Stage/Runtime/               CommentFrame
```

## 17. Decisions and Checks

For the user to decide:

1. Whose name the Google Cloud project and client are created under, and whether to apply for verification (privacy policy, home page, domain). Until then, use is capped at 100 users and the consent screen says "unverified app".
2. Whether to start with the quota (10,000 units a day, shared by every user) and ask for an increase as users grow.
3. Keeping stage 1's scope to `youtube.readonly` only.
4. Keeping stage 1's protection against unwanted comments to blocked words and the show-comments switch, with display delay and hiding by user later.
5. The scope of this document: starting with showing comments, and leaving creating broadcasts and the rest to stage 2.

To check before implementing:

- How HTTP `streamList` delimits messages (prototype). If it cannot be used, consider speaking gRPC over native WinHTTP (HTTP/2).
- That `streamList` accepts a `youtube.readonly` access token.
- The YouTube API Services Terms' conditions on display and storage.

## 18. System Design Changes (made when this is settled)

- 10.11: describe stage 1 (sign-in and showing comments), and stages 2 and 3.
- 10.12, 6.13: OAuth refresh tokens in Credential Manager, and the smallest scope.
- Chapter 11 (Stage): the comments frame as an element over the stream.
- Chapter 16: comments from other services such as Twitch can be added through the same `ILiveChat`.
