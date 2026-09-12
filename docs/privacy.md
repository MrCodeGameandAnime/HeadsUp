---
layout: page
title: HeadsUp CI Privacy Policy
permalink: /privacy/
---

# HeadsUp CI Privacy Policy

**Last updated:** September 12, 2026

HeadsUp CI is a local desktop utility from 404 Builds that monitors GitHub
Actions for a repository and branch selected by the user.

## Information HeadsUp handles

HeadsUp does not operate a telemetry, advertising, analytics, or user-profile
service. It does not send application usage data to 404 Builds.

HeadsUp stores a small preferences file on the local device. Depending on the
features used, it can contain:

- the selected GitHub repository and branch;
- an optional local Git repository path;
- window position and size;
- appearance, polling, startup, and always-on-top preferences.

On Windows, these settings are stored under the user's local application-data
folder in `GitCiHud\settings.json`. The settings remain on the device until
the user removes them or uninstalls/cleans up the application data.

## GitHub and GitHub CLI

GitHub monitoring uses the user's installed and authenticated GitHub CLI (`gh`).
HeadsUp starts `gh` to request branch, workflow-run, and job information from
GitHub. Those requests are made under the user's GitHub authentication and are
subject to [GitHub's privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-privacy-statement).

HeadsUp does not collect or store the user's GitHub password, access token, or
other GitHub credentials. GitHub CLI manages its own authentication storage.

If a local clone is linked, HeadsUp may invoke the installed `git` executable
to read local repository status. HeadsUp does not modify the repository.

## Browser and clipboard actions

When the user chooses **Open Run**, HeadsUp opens the selected GitHub URL in the
user's default browser. When the user chooses a copy action, HeadsUp places the
user-selected SHA, run ID, URL, or CI evidence text on the system clipboard.
These actions occur only at the user's request.

## Data sharing and retention

HeadsUp does not sell, rent, or share application data with 404 Builds. It has
no built-in cloud database or remote account service. Data sent to GitHub is
handled by GitHub and the user's GitHub CLI configuration. Data copied to the
clipboard is controlled by the operating system and may be available to other
applications according to the user's system settings.

## Children's privacy

HeadsUp is a general-purpose developer utility and is not directed to children.

## Changes to this policy

This policy may be updated when HeadsUp's data practices or published
functionality changes. The effective date above will identify the latest
version.

## Contact

Questions about this policy can be raised through the
[HeadsUp repository](https://github.com/MrCodeGameandAnime/HeadsUp/issues).
