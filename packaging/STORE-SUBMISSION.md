# HeadsUp Microsoft Store Submission

Use this checklist for the `HeadsUp-msix` release artifact. Do not submit the
package until every required validation item is complete.

## Restricted capability justification

HeadsUp is a desktop GitHub Actions monitoring utility built with Avalonia/.NET. It requires `runFullTrust` because it is a packaged desktop application that launches local command-line tools (`gh`, and optionally `git`), reads and writes its local application settings, opens GitHub workflow URLs in the user's default browser, and performs normal desktop window operations. HeadsUp does not elevate privileges, install drivers, modify protected system locations, or execute downloaded code.

Manifest declaration:

```xml
<rescap:Capability Name="runFullTrust" />
```

## Certification notes

HeadsUp monitors GitHub Actions for an exact repository/branch commit SHA.

GitHub CLI (`gh`) must be installed and authenticated for GitHub functionality.

To configure the app:

1. Launch HeadsUp.
2. Open Settings using the gear button.
3. Enter a GitHub repository URL.
4. Load and select a branch.
5. HeadsUp will display the GitHub Actions run associated with the exact current branch SHA.

A local Git repository is optional and is only used for local clone status.

The Microsoft Store build intentionally does not expose the unpackaged
registry-based "Start with Windows" feature.

## Manual Partner Center checklist

- [ ] Pricing and availability
- [ ] Free
- [ ] Public/discoverable
- [ ] Markets
- [ ] Release schedule
- [ ] Category
- [ ] Age ratings
- [ ] Package upload (`HeadsUp-msix`)
- [ ] Store description
- [ ] Screenshots
- [ ] Store logo
- [ ] Keywords/features
- [ ] Copyright
- [ ] Restricted capability explanation
- [ ] Certification notes
- [ ] Publishing behavior

## Final validation checklist

Before submission:

- [ ] Install the final MSIX on a clean/test Windows environment
- [ ] Launch HeadsUp from the Start menu
- [ ] Verify the icon and package identity
- [ ] Verify GitHub CLI detection and actionable setup guidance
- [ ] Verify authenticated GitHub monitoring for an exact branch SHA
- [ ] Verify settings persistence
- [ ] Verify close and reopen behavior
- [ ] Verify uninstall and clean removal
- [ ] Run the Windows App Certification Kit (WACK)
- [ ] Resolve or formally assess the `Tmds.DBus.Protocol` vulnerability

Do not submit until all items above are complete. In particular, confirm that
the package identity is `404Builds.HeadsUpCI`, the publisher is
`CN=2BB531F8-77DD-4D82-AA5F-230F26EB09EE`, and the Store package version uses a
valid `Major.Minor.Build.0` value.
