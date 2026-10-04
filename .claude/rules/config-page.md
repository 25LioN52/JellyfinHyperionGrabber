---
paths:
  - "src/Jellyfin.Plugin.HyperionGrabber/Configuration/*.html"
  - "src/Jellyfin.Plugin.HyperionGrabber/Configuration/*.js"
---

# Configuration page rules

- The HTML is a fragment (no `<html>`/`<body>`) with `data-role="page"` and
  `data-controller="__plugin/HyperionGrabberJs"`; the script is an ES module whose default export receives `view`.
- Use only what Jellyfin's web client provides globally: `ApiClient` (`getPluginConfiguration`,
  `updatePluginConfiguration`, `ajax`, `getUrl`) and `Dashboard` (`showLoadingMsg`, `hideLoadingMsg`,
  `processPluginConfigurationUpdateResult`, `processErrorResponse`). No frameworks, bundlers or external URLs except
  documentation links.
- Use Jellyfin's built-in elements and classes (`is="emby-input"`, `is="emby-button"`, `inputContainer`,
  `fieldDescription`, `verticalSection`) so the page follows the user's theme. No inline colors.
- Query elements through `view.querySelector('#Id')`; every id used by the script must exist in the HTML
  (`PluginTests.ConfigPage_ContainsEveryElementTheScriptUses`).
- JSON sent to and read from the API is PascalCase.
- Validate on the client for fast feedback, but the server validates too; show the server's `Message`.
- Text is user-facing: short, plain, tells the user what to do next.
