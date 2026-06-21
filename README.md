# SmoothTube

SmoothTube is a native Windows 11 YouTube client built with WinUI 3.

## Opening the project

Open the solution file:

```text
SmoothTube/SmoothTube.slnx
```

The main app project is here:

```text
SmoothTube/SmoothTube/SmoothTube
```

## Credentials

SmoothTube does not include personal Google credentials in source control. Each developer needs to provide their own YouTube Data API key and OAuth Desktop client details in the app Settings page after cloning.

API keys, OAuth client details, access tokens, and refresh tokens are stored locally by the Windows app at runtime and should not be committed.

## Notes

Build output, Visual Studio cache files, packaged app output, local user files, and credential-style files are ignored by `.gitignore`.
