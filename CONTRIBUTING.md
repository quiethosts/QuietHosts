# Contributing to QuietHosts

Thank you for helping. You can help in three ways:

- **Report a site that QuietHosts breaks** with the [broken-site form](https://github.com/quiethosts/QuietHosts/issues/new?template=site-broke.yml). Reports like this keep Standard safe for everyone.
- **Suggest, move or remove an address** with a pull request that edits the block lists. This page explains the rules.
- **Report a bug** with the [problem form](https://github.com/quiethosts/QuietHosts/issues/new?template=bug.yml). Report security problems privately, as [SECURITY.md](SECURITY.md) describes.

Please keep personal information out of issues, pull requests and commits. Everyone who takes part follows the [code of conduct](CODE_OF_CONDUCT.md). If you need help using QuietHosts, see the [user guide](docs/README.md) and [SUPPORT.md](SUPPORT.md).

## The block lists

Pull requests edit only the files in `lists/`:

| File | What it holds |
|---|---|
| `lists/default.txt` | Standard: blocked for everyone who turns QuietHosts on. |
| `lists/strict.txt` | Strict: blocked only when Strict is chosen. Every address has a note. |
| `lists/guard.txt` | Addresses that are never blocked, even if a list contains them. |

The lists are built into QuietHosts.exe, and QuietHosts never downloads lists. So a merged list change reaches people when the maintainer ships the next release of the app and they [update](README.md#update-quiethosts) to it.

### When an address can be added

An address is added only if all of these hold:

1. It is in at least one maintained list from [CREDITS.md](CREDITS.md). Link to it in your pull request. A test page isn't enough.
2. It resolves in public DNS: `nslookup <address> 1.1.1.1` returns an IP address.
3. It is an exact name, with no wildcards, IP addresses or ports. It is not covered by `lists/guard.txt` or by the Microsoft list built into the app, and it is not in `data/reviewed-drop.txt`.
4. It passed a breakage review (below) and got a tier:
   - **Standard** (`default.txt`): no known breakage. Pages lose only ads, ad slots, tracking and "around the web" boxes.
   - **Strict** (`strict.txt`): a known, acceptable trade-off on some sites or apps, of the kinds QuietHosts lists before someone switches to Strict (see [What it does](README.md#what-it-does)), written as its note. For example: links in ads and emails that don't open; videos that don't play on some sites; a site that shows an ad-blocker message, or an error page, when it detects blocking; broken buttons, forms, sign-up or checkout on some sites; tools for advertisers and site owners; apps and developer tools that show errors or don't start. Name the sites or apps in the note where you can.
   - **Drop**: it breaks more than that: sign-in, captchas, fraud or bot checks, or security updates anywhere; pages or videos on many sites (such as video players on many sites); or apps and games that many people use. An address whose breakage outweighs what blocking it gains is dropped too. Don't add it. If it is already in a list, remove it in your pull request and say why. The maintainer records dropped addresses in `data/reviewed-drop.txt`.

Addresses on [anudeepND's whitelist](https://github.com/anudeepND/whitelist) or HaGeZi's referral allowlist (also through an entry for a parent domain), or with a global `@@` exception in the AdGuard DNS filter, are never Standard: Strict at most.

An EasyList, EasyPrivacy or uBlock Origin exception that re-allows an address only on named sites is a breakage signal: test those sites. If one breaks, the address goes to Strict with the site named in the note, unless HaGeZi Light and OISD Small both block it. Then it may stay in Standard, with a note on what breaks.

### Breakage review

1. Check those allowlists.
2. Add `0.0.0.0 <address>` to your hosts file, outside the QuietHosts block. The README shows how to [open it as administrator](README.md#open-the-hosts-file-as-administrator). Run `ipconfig /flushdns` and restart the browser.
3. Visit where you saw the address, plus a Google, Microsoft or Apple sign-in, webmail, a video site, a social network, a shop's cart and checkout, a bank login, a news video, and a game launcher or chat app.
4. List what you tested in the pull request. Then remove your test line.

### Breakage beats blocking

A reproducible "site broke" report moves the address to Strict or Drop.

### Never

- Bulk-import or script-sync addresses from another list.
- Copy a list's comments or order.
- Add DuckDuckGo Tracker Radar data (CC BY-NC-SA).
- Add Microsoft addresses.

These rules keep the lists our own work under the MIT License. [CREDITS.md](CREDITS.md) explains why.

### List format

The files are UTF-8 without BOM, with LF line endings:

```
# QuietHosts block list - MIT License - sources: CREDITS.md
# version: 2026-11-20
[ad-server]
address
address | company
address | company | note
address |  | note
```

- Lines that start with `#`, and blank lines, are comments. Leave the `# version:` line alone: the maintainer sets it when the lists ship.
- `[key]` starts a category. Put each address under the category that fits it, using one of the keys in the table below.
- An address is a lowercase name such as `ads.example.com`: letters, digits, hyphens and dots, with no trailing dot.
- The company has at most 60 characters and the note at most 80. Neither may contain `|` or control characters. Write both in plain ASCII: no curly quotes, long dashes or accented letters.
- A note says in plain words what may stop working. Every Strict address needs one.
- Put one space on each side of every `|`, and leave out empty fields at the end: write `address | company`, not `address | company |`. With a note but no company, write `address |  | note`.
- No address appears twice, within a list or across lists. Each `[key]` appears only once in a file.
- No tabs and no spaces at the end of a line, and the file ends with a line break. Keep the comment lines at the top of the file as they are, with exactly one `# version:` line.

| Key | Shown in the app as |
|---|---|
| `tag-manager` | Tag manager |
| `google-ads` | Google ads |
| `amazon-ads` | Amazon ads |
| `social-pixel` | Social network ad pixel |
| `ad-server` | Ad server |
| `ad-exchange` | Ad exchange |
| `header-bidding` | Ad auction |
| `native-ads` | "Around the web" ad box |
| `video-ads` | Video ads |
| `popunder-redirect` | Pop-under or redirect ads |
| `push-ads` | Push-notification ads |
| `anti-adblock` | Ad-blocker detection |
| `cookie-sync-identity` | Cross-site tracking |
| `ad-verification` | Ad verification |
| `affiliate` | Affiliate tracking |
| `data-broker` | Data broker |
| `analytics` | Analytics tracker |
| `session-recording` | Session recording |
| `app-ads` | App ads and attribution |
| `cryptominer` | Cryptominer |
| `fingerprinting` | Fingerprinting |
| `other` | Ads or tracking |

In `lists/guard.txt`, each line is one entry. `example.com` protects example.com and every address under it. `=www.example.com` protects only that exact address.

The tests check these rules. CI runs them on every pull request, and you can run them yourself with `dotnet test` (see [Code](#code)).

### Pull request checklist

The pull request form ([.github/pull_request_template.md](.github/pull_request_template.md)) already contains this checklist. If yours doesn't, copy it into your pull request:

```
- [ ] Each address is in a maintained list from CREDITS.md (links: ...)
- [ ] Each address resolves in public DNS (nslookup <address> 1.1.1.1)
- [ ] Exact names only; not covered by lists/guard.txt or the Microsoft guard; not in data/reviewed-drop.txt
- [ ] Breakage review done. Tier: Standard / Strict (with a note)
- [ ] What I tested: ...
- [ ] No personal information in this pull request
```

## Code

QuietHosts is a WinForms app for .NET Framework 4.8, written in C#. You need Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), version 10.0.404 or a later 10.0.4xx (`global.json` asks for that feature band).

```
dotnet build QuietHosts.slnx
dotnet test QuietHosts.slnx
```

The app is then in `src/QuietHosts/bin/Debug/net48/QuietHosts.exe`. Debug builds have two developer options, which don't exist in release builds:

- `QuietHosts.exe --sandbox` works on a copy of the hosts file in `%TEMP%\QuietHosts-sandbox` and never asks for administrator rights.
- `QuietHosts.exe --render-ui` saves a picture of every screen in `%TEMP%\QuietHosts-render`, then exits. It draws every window on a desktop of its own, so nothing appears on yours. `QUIETHOSTS_RENDER_THEMES` (for example `light,dark`), `QUIETHOSTS_RENDER_TEXT_SCALE` (100 to 225) and `QUIETHOSTS_RENDER_DPI` (96 to 288) choose the passes.

Rules for code:

- No new runtime dependencies. NuGet packages are only allowed in the test project.
- No network code. QuietHosts never goes online.
- Tests use temporary folders only. They never touch the real hosts file, anything under `C:\Windows` or `HKEY_LOCAL_MACHINE`, and they never ask for administrator rights.
- Compare and lowercase text with ordinal comparisons and `ToLowerInvariant` only. Culture-sensitive versions change some letters on some Windows languages.
- Every text the user sees goes in `src/QuietHosts/Strings.cs`.
- `dotnet build` and `dotnet test` must pass. Warnings count as errors.

## Documentation

README.md is the front door; the [user guide](docs/README.md) in `docs/` has the details.

- The guide quotes QuietHosts exactly: labels in bold as `src/QuietHosts/Strings.cs` has them, messages in quotes. When a text in the app changes, change the guide and README.md to match.
- QuietHosts links to four headings of README.md: "If something goes wrong", "Verify your download", "QuietHosts won't change your hosts file" and "Windows Defender reports HostsFileHijack" (`Strings.cs`). The release notes link to "If Windows warns you". Never rename these headings: copies of QuietHosts that are already out there link to them. The guide repeats their steps, so keep both copies the same.
- Links between files in the repository are relative. Links that need the repository's address, such as the issue forms and the releases page, use `https://github.com/quiethosts/QuietHosts`.
- The screenshots in `docs/images` come from `--render-ui` in a Debug build, with `QUIETHOSTS_RENDER_THEMES=light,dark` at 100% text and 96 DPI (set `QUIETHOSTS_RENDER_DPI=96` on a display at another scale). Each picture is cropped to the window: it has an 8 px black band on the left, right and bottom, the window's invisible frame. Then its corners are rounded (8 px) and it gets a 1 px border (#C4C4C4 for light, #474747 for dark). Pages show each pair with `<picture>`, so readers see the one that matches their GitHub theme. The images come from these scenes:

  | Image | Scene |
  |---|---|
  | `main-on` | `main-on-standard` |
  | `first-run` | `first-run` |
  | `turned-on` | `main-result-turn-on` |
  | `strict-confirm` | `strict-confirm` |
  | `fix-dialog` | `fix-strict` |
  | `fix-allow` | `fix-row-details` |
  | `fix-block-again` | `fix-allowed-selected` |
  | `notices` | `main-warnings-and-tips` |
  | `microsoft-lines` | `microsoft-lines` |
  | `error-blocked` | `error-access-denied` |
  | `undo` | `undo` |
  | `cant-change` | `main-refused-encoding` |

## License and commits

Contributions are under the MIT License, like the rest of QuietHosts. There is no CLA.

Commit with your GitHub noreply email address, so your personal email address stays private:

1. On GitHub, open **Settings > Emails** and turn on **Keep my email addresses private**. GitHub then shows your noreply address, which looks like `12345678+username@users.noreply.github.com`.
2. In your copy of the repository, run `git config user.email "<your noreply address>"`.

## Releasing (maintainers)

Releases are built only by GitHub Actions, from version tags. The tag sets the version.

Set up once, before the first `v*` tag is pushed:

- Make the repository public. On GitHub's free plans, artifact attestations only work in public repositories.
- Create the environment `release` (**Settings > Environments**) with the maintainer as a required reviewer and deployments limited to tags matching `v*`. If a tag starts the Release workflow before this environment exists, GitHub creates it without protection, and the release is published without approval.
- Turn on release immutability (**Settings > General > Releases**). GitHub creates the release attestation that `gh release verify-asset` checks only for immutable releases, and only for releases made after it is turned on.
- In the repository's security settings, turn on private vulnerability reporting (SECURITY.md links to it), Dependabot alerts and security updates, and secret scanning with push protection.
- Under **Settings > Actions > General**: allow only actions created by GitHub (every workflow uses only `actions/*`) and require actions to be pinned to a full-length commit SHA; give workflows read-only permissions; don't allow GitHub Actions to create or approve pull requests; and require approval for workflow runs from outside contributors' pull requests.
- Add rulesets (**Settings > Rules > Rulesets**): one for `v*` tags (only the maintainer may create them; no updates or deletions) and one for `main` (no force pushes or deletion; the `Build and test` check must pass; the maintainer may bypass).
- Create the labels `bug` and `breakage`, which the issue forms use. A form silently skips a label that doesn't exist.
- A fork that publishes its own releases replaces `quiethosts/QuietHosts` with its own repository in every file that contains it: `git grep -l -F "quiethosts/QuietHosts"` lists them, among them the docs in `docs/`, `.github/`, Directory.Build.props and `src/QuietHosts.Core/AppInfo.cs` (the app's fallback link). Release builds take the repository for the app's links from GitHub itself.
- Turn on reported content (**Settings > Moderation options > Reported content**), so people can report a comment privately to the maintainers, as the [code of conduct](CODE_OF_CONDUCT.md) says.
- Give the repository its description, topics and social preview image (**About** on the repository's main page, and **Settings > General > Social preview**).

For each release:

1. If anything in `lists/` changed since the last release, guard.txt too, set a new, later date in the `# version:` line of `lists/default.txt` and `lists/strict.txt`. QuietHosts offers people who update the new list only when that date is newer. Then update the address counts in README.md, and `lists=` and `hosts=` in its example block (QuietHosts computes its own; the README's are typed by hand). The Release workflow checks all of these. Several screenshots in `docs/images` show the counts and the list date too: make them again (see [Documentation](#documentation)). The user guide doesn't state the lists' counts, so its text needs no change.
2. In CHANGELOG.md, rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD`, then add a new, empty `## [Unreleased]` above it. At the bottom, add the line `[X.Y.Z]: https://github.com/quiethosts/QuietHosts/releases/tag/vX.Y.Z` and point `[Unreleased]` to `https://github.com/quiethosts/QuietHosts/compare/vX.Y.Z...HEAD`.
   - For a pre-release such as `X.Y.Z-beta.1`, leave `## [Unreleased]` as it is. Add a `## [X.Y.Z-beta.1] - YYYY-MM-DD` section below it with the text its release notes should show (a copy of the Unreleased text is fine), plus its `[X.Y.Z-beta.1]:` link line. The full text becomes `## [X.Y.Z]` at the final release. The Release workflow stops when the version's section is missing or empty.
3. Push to `main` and wait for CI to pass.
4. Tag that commit and push the tag: `git tag vX.Y.Z`, then `git push origin vX.Y.Z`.
5. The Release workflow checks the tag (it must be on `main`), the list version and the README. It builds and tests with that version. Then a separate job, which runs no test packages, builds QuietHosts.exe again and attests it. Then the workflow waits for approval.
6. Approve the `release` environment. The workflow publishes QuietHosts.exe and SHA256SUMS.txt, with release notes from CHANGELOG.md plus the verify steps. A tag that contains `-` becomes a pre-release.

If **Check the tag, build and test** or **Build QuietHosts.exe and attest it** fails, nothing is published. For a temporary problem, such as a network error, open the run and click **Re-run failed jobs**. Otherwise fix the cause on `main` and release the next version instead, for example `vX.Y.Z-beta.2` after `vX.Y.Z-beta.1`: in CHANGELOG.md, change the failed version to the new one in its section heading and the link lines, then push and tag again. Release tags can't be moved or deleted, so the failed tag stays without a release.

If publishing fails halfway, delete the draft release on GitHub and re-run the publish job.
