# YouTube Integration: Decisions and History

Why integration with the YouTube API (the "later stage" of system design 10.11) is not built now: what was looked into, the methods compared, the reasons for holding off, and the groundwork that makes it easier to build later.

## 2026-11-21: Integration through the API (sign-in, showing comments) is put on hold (system design 10.11)

**What started it:** the user wanted the stream's comments shown on the stream (many streamers do this with outside tools). A draft design was made for a first stage: sign in with a Google account, receive the comments of the user's own broadcast, and show them in a frame of the placement editor.

**Decision (user, 2026-11-21):** integration through the YouTube API is not built now. Only service-independent groundwork (below) is to be prepared, so that a developer can build a comments feature easily later.

**Reasons:**

- **Google's verification is needed.** Signing in through the official API (OAuth) needs a project and a client registered in Google Cloud by the developer. YouTube's scopes are subject to verification; until verified, the consent screen says "unverified app" and use is capped at 100 users. While the publishing status is "Testing", only registered test users can use it, and sign-ins expire after 7 days. Verification needs a privacy policy page, a home page, proof of domain ownership, and a video of the app.
- **The quota is shared by every user.** The daily limit (10,000 units by default) is per project: the total of all the app's users.
- **Abuse cannot be fully prevented.** A client ID and secret built into a desktop app can be extracted (Google does not treat them as secret either). Someone who extracts them and uses up the quota stops comments for every user.
- For an app developed by one person and given away for free, keeping up the verification and the quota is a large burden.

**What was looked into (2026-11-21, the YouTube Data API documentation):**

| Topic | Findings |
|---|---|
| Receiving comments | `liveChatMessages.list` (polled; 5 units a call) and `liveChatMessages.streamList` (the connection stays open and new comments are sent; 5 units a call). `streamList` works over gRPC and HTTP |
| Authorization | `streamList` takes an API key or an OAuth access token (the documentation's example). Finding the user's own broadcast with `liveBroadcasts.list` (`mine=true`) needs OAuth with the `youtube.readonly`, `youtube`, or `youtube.force-ssl` scope |
| Quota | 10,000 units a day by default. Polling `list` every 5 seconds for two hours is about 7,200 units, nearly all of it in one stream. With `streamList`, about 6 to 10 units a stream (about 1,000 streams a day for all users together) |
| OAuth on the desktop | A loopback redirect (`127.0.0.1`) and PKCE. No web view inside the app (the default browser is used) |
| Networking in Unity | Unity's Mono `HttpClient` has no HTTP/2, so gRPC cannot be used. How HTTP `streamList` delimits its messages is not documented and would need a prototype |

**Points of the draft design** (kept as a starting point; the full text is in the history of the branch that added this record):

- Sign-in: loopback and PKCE, the `youtube.readonly` scope only. Only the refresh token is kept, in Windows Credential Manager (as the stream key). Signing out also revokes it at Google.
- When the stream goes live, `liveBroadcasts.list` finds the user's broadcast and `streamList` receives its comments, reconnecting after a pause when cut.
- Comments are drawn as plain text (never read as rich text), with control characters and direction-changing characters removed, a length limit, and blocked words. No avatar images are loaded.
- Viewers' names and comments are never left on disk or in logs.
- The client ID and secret are not in the repository; only release builds embed them (so forks do not use the quota).
- The YouTube API's types stay inside the Streaming module.

**Compared and not chosen:**

- An API key in the app, with the user pasting the broadcast's URL: no sign-in, but the same shared quota, and the same abuse when the key is extracted.
- Users creating their own Google Cloud projects: no verification and no shared quota, but normal users would be asked to follow developer steps (CLAUDE.md 3.1).
- Unofficial methods (parsing YouTube's pages, or its internal API): neither sign-in nor quota, but against YouTube's terms of service, and they break without notice.

**What can be used now instead:** capture the window of YouTube's pop-out chat, opened in a browser, with the same-PC window capture (capture detailed design), and place it on the stream in the placement editor. It needs neither sign-in nor quota, and only shows YouTube's own page. It looks like the chat as it is, and cannot filter by blocked words.

**Groundwork (to make a comments feature easier to build later; the user decides when):**

Only the service-independent parts, with no YouTube or Google code. When receiving from YouTube or Twitch is added later, writing that one receiving part should be enough for comments to reach the screen.

1. The comment type and flow: `LiveChatMessage` (ID, time, name, text, kind, whether from the streamer, a moderator, a member) and `ILiveChat` (state, recent comments, starting and stopping).
2. Filtering: removing control and direction characters, a length limit, blocked words (pure functions).
3. Display: drawing comments into a texture (UI), and a comments frame in the placement editor (Stage; an `IFrameSource`, as the avatar frame).
4. A trial source: in developer mode, fixed lines or typed text flow in as comments, so the display and the frame can be checked without any real source.

**What reconsidering it would need:**

- A Google Cloud project in the user's (developer's) name, and applying for verification (privacy policy, home page, domain, video).
- A plan for asking for more quota.
- A prototype of how HTTP `streamList` delimits messages; if it cannot be used, speaking gRPC over native WinHTTP (HTTP/2).
- Checking the display and storage conditions of the YouTube API Services Terms and Developer Policies.

References: [liveChatMessages.streamList](https://developers.google.com/youtube/v3/live/docs/liveChatMessages/streamList), [Streaming Live Chat](https://developers.google.com/youtube/v3/live/streaming-live-chat), [liveChatMessages.list](https://developers.google.com/youtube/v3/live/docs/liveChatMessages/list), [liveBroadcasts.list](https://developers.google.com/youtube/v3/live/docs/liveBroadcasts/list), [YouTube Data API Overview](https://developers.google.com/youtube/v3/getting-started)
