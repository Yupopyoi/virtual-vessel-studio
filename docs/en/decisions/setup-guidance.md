# Guiding New Users: Decisions and History

How first-time users are guided through the settings (system design 14.32): why there is no first-run guide screen, and the settings screen's help explains things instead.

## 2026-11-22: No first-run guide screen; the settings screen's help explains instead (system design 14.32)

**What started it:** when the features in the design documents but not built were listed, the guide at the first start (system design 14.32) was among them. The user judged a guide necessary and asked for its design.

**The draft design:** a layer over the whole screen showing, in order, a welcome page, six steps (avatar, camera, microphone, game screen, voice, streaming), and a done page. Each step showed the same Card as the setup screen. It could be skipped on its first page, closed partway, and opened again from the setup screen. The voice was "later", without starting Voice Lab's work. A mock-up was made too.

**Decision (user, 2026-11-22):** the guide screen is not built. The help of each row on the settings screen (`components.md` 3.28) explains things thoroughly instead.

**Reason:** having seen the mock-up, the user judged that this much explanation is not needed. The settings screen divides its items into pages and Cards, and help on each row is enough to explain them.

**What is kept from the guide's draft:**

- "Use the sample avatar": a button registering the sample model that ships with the application, without choosing a file. It sits on the avatar Card (`docs/en/ui/screens/setup.md` chapter 5).

**When to reconsider:** when changing the Data Root and choosing projects exist, and there is more to choose first. The draft's full text was discarded in the work on the branch that added this record and is not kept; it would be rebuilt from the points above.
