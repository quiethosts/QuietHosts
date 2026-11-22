# Security policy

## Supported versions

Security fixes go into the latest release only. Please check that you use it before you report a problem.

## Report a vulnerability

Please don't report security problems in public issues. Report them privately on GitHub instead:

1. Open the [Security tab](https://github.com/quiethosts/QuietHosts/security) of this repository.
2. Click **Report a vulnerability**. This is the [direct link](https://github.com/quiethosts/QuietHosts/security/advisories/new).
3. Describe the problem: your QuietHosts version, your Windows version, the steps to reproduce it, and what an attacker could do with it.

What happens next:

- You get a reply within 7 days.
- A confirmed problem is fixed in a patch release and published as a security advisory.
- You are credited in the advisory if you want.

## In scope

- **Running code as administrator through QuietHosts.** For example, making its administrator run (`QuietHosts.exe --elevated ...`) do anything other than its one change through what that run reads: its command line, its registry settings, and the hosts file and its backups. This includes a standard user who steers the run that way.
- **Writing outside its block**, or losing or changing other lines in the hosts file.
- **Tampered releases or CI.** For example, a release file that doesn't match SHA256SUMS.txt or its attestation, or a weakness in the GitHub Actions workflows that build and publish releases.

## Known limits

- **The administrator run trusts the folder that holds QuietHosts.exe, and your account's environment**, like any program you run as administrator. Windows and .NET load DLL and .config files from that folder before QuietHosts' own code starts. So a program already running on your account, or anyone who can write to that folder, can make the next administrator run do what it wants, for example by replacing QuietHosts.exe or putting a DLL next to it. When you say yes to its Windows prompt, Windows runs whatever is there.
- A folder of its own protects against files that land in Downloads by themselves. It can't protect against programs already running as you. On a PC with standard accounts, keep QuietHosts.exe in a folder that only administrators can change, such as `C:\Program Files\QuietHosts`. The README explains this in [Download and start](README.md#download-and-start), and the user guide in [Put QuietHosts in a folder of its own](docs/getting-started.md#put-quiethosts-in-a-folder-of-its-own).

## Out of scope

- Sites that break, and ads that still show. Please use the [issue forms](https://github.com/quiethosts/QuietHosts/issues/new/choose) for those. The user guide's [Fix a site or app](docs/fix-a-site.md) and [Troubleshooting](docs/troubleshooting.md) pages may solve them first.
- Attacks that already need administrator rights. An administrator can change the hosts file without QuietHosts.
- Attacks that need a program already running on the account that starts QuietHosts, or write access to the folder that holds QuietHosts.exe, such as planting a DLL or .config file there or changing that account's environment variables. Such a program could replace QuietHosts.exe itself, wherever it is (see Known limits).

## How QuietHosts limits risk

- The window runs without administrator rights. Each change is one hidden administrator run that trusts nothing from the window, makes one change and exits.
- QuietHosts never goes online. Its block list is built into the exe.
- It writes only the hosts file, its backups next to it, and its own registry settings.
- It never blocks Microsoft addresses, or the services that sign-in, payments and security updates need.
- GitHub Actions builds every release from a version tag, with SHA256SUMS.txt and a build-provenance attestation. The job that signs the attestation builds only QuietHosts.exe, so the test project's packages never run there. The README explains how to [verify your download](README.md#verify-your-download).

The user guide's [How it works](docs/how-it-works.md) explains these safeguards for everyone who uses QuietHosts.
