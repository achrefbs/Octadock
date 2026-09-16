# Code signing

Status, 16 September 2026: **not enabled**. The published 0.3.0-alpha.2 installer and desktop payload are unsigned. SignPath Foundation is the selected application route; acceptance and production signing remain pending. Nothing in this document claims Foundation participation or a signed release.

## Prepared locally

- `.signpath/payload.xml` is a draft artifact configuration for a ZIP rooted at `publish/octadock`. It targets only `Octadock.exe` and `cli/octadock.exe`, with product/original-filename restrictions. It does not sign bundled third-party DLLs, the installer or its uninstaller. The provider must validate the configuration with a real sample.
- `build/verify-signatures.ps1` verifies embedded signatures, Windows trust, the expected certificate and timestamps. It returns evidence only when every supplied file passes. It does not sign files or change the trust store.

Example after the approved certificate is known and signed files have been returned:

```powershell
$files = @(
    '<signed-payload>/Octadock.exe',
    '<signed-payload>/cli/octadock.exe',
    '<signed-installer>/Octadock-<new-version>-Setup.exe',
    '<isolated-test-install>/unins000.exe'
)
./build/verify-signatures.ps1 -Files $files `
    -ExpectedThumbprint '<approved-certificate-thumbprint>' `
    -SignToolPath '<Windows-SDK>/x64/signtool.exe' |
    ConvertTo-Json -Depth 5 | Set-Content '<new-output>/signature-verification.json'
```

Obtain the expected thumbprint from the approved provider certificate, independently of the downloaded release. A thumbprint is public metadata, not a private key. Update the approved value deliberately when the certificate changes. Verification is evidence about these files on this machine at this time; it is not a prediction of SmartScreen reputation.

## Provider onboarding still required

1. Obtain Foundation acceptance and the actual organization/project/policy identifiers.
2. Confirm maintainer account MFA, assign reviewer/approver roles and require manual release approval.
3. Review the GitHub App's requested permissions for this repository. The GitHub open-source integration requires GitHub-hosted builds. The previously observed GitHub account billing restriction must be rechecked now that the repository is public.
4. Import and validate the artifact configuration. Configure the approved certificate and timestamp service in the signing policy. Keep API credentials in a restricted GitHub environment or repository secret; never commit them.
5. Add the required Foundation attribution, roles and code-signing policy to the download site after acceptance and before distributing signed releases.

## Build integration still required

The existing release workflow only builds unsigned portable packages. It has not been changed to submit signing requests, and there are no placeholder credentials or automatic publication steps.

For a new signed release, build and test the exact tagged source, upload artifacts for origin verification, submit the payload signing request and retrieve approved signed files. Sign before creating final portable ZIPs and SHA-256 manifests.

The Inno Setup installer requires separate treatment: sign its generated uninstaller and embed that signed uninstaller when building Setup, then sign Setup itself. Signing only the outer installer does not sign its contents. Implement and verify that flow with the provider; do not assume the generic PE configuration rewrites an Inno archive. Also derive `VersionInfoVersion` from release metadata instead of retaining the current fixed `0.3.0.2`.

Verify the app, CLI, Setup and installed uninstaller. Only then emit a signed release manifest, regenerate final checksums, and test installation, upgrade and uninstall in an isolated profile. Keep the installer AppId and user-data paths stable. Publish a new version; do not overwrite the existing alpha.2 artifacts with different bytes.

## References

- [Foundation eligibility and obligations](https://signpath.org/terms.html)
- [Verified GitHub build integration](https://docs.signpath.io/trusted-build-systems/github)
- [Artifact configuration syntax](https://docs.signpath.io/artifact-configuration/syntax)
- [Inno Setup signing](https://jrsoftware.org/ishelp/topic_setup_signtool.htm) and [signed uninstallers](https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm)
- [Microsoft SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)
