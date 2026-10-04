---
paths:
  - "tests/**"
---

# Test rules

- xUnit v3 on Microsoft.Testing.Platform. Pass `TestContext.Current.CancellationToken` to anything that takes a token.
- Name tests `Method_Condition_ExpectedResult` (or a plain-English sentence for contracts); one behaviour per test;
  arrange / act / assert separated by blank lines.
- Never synchronize with fixed sleeps. Use `TestHelpers.WaitUntilAsync`, `FakeHyperionServer.NextRequestAsync`, or
  `FakeTimeProvider` from `Microsoft.Extensions.Time.Testing`. Short `Task.Delay` is only acceptable to prove that
  something does *not* happen.
- Network tests use `FakeHyperionServer` on 127.0.0.1 with an OS-assigned port; never fixed ports, never the internet.
- Random data uses a fixed seed so failures reproduce.
- Protocol tests verify encoded bytes with the official runtime (`OfficialHyperionCodec`), not with our own reader.
- Tests run for both `net9.0` and `net10.0`; do not condition tests on the framework unless the behaviour differs.
- Prefer real objects and the fakes in `TestSupport` over mocks; use NSubstitute only for Jellyfin interfaces.
