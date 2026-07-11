# Image mockups

Octadock can turn a selected part of a screenshot into an AI-generated UI
variant without replacing the normal capture workflow or opening a generic AI
workspace.

## Use it

1. Open a capture in the annotation editor.
2. Draw or select a rectangle around the UI region to change.
3. Choose **Mockup** in the editor toolbar.
4. Describe the exact visual delta, review the named OpenAI disclosure, and
   generate the variant.
5. Compare the original and variant. Approving returns the variant to the
   Capture Shelf and links it to the original in History.

Octadock uses the installed, signed-in Codex CLI. Verify it once in a terminal:

```powershell
codex --version
codex
```

No API key is required or read by this workflow. Image generation counts against
the signed-in Codex account's included usage, and Octadock does not silently
switch providers.

## Safety boundary

- Only the selected crop plus 32 pixels of surrounding visual context is sent.
- The dialog names the destination and warns that visible text, code, or secrets
  may be present; generation remains disabled until that send is approved.
- The provider receives a mask for the selected region.
- The full screenshot is never attached separately; only the bounded context crop
  is sent. A selection that covers most of a small screenshot can naturally make
  that crop include most of the source, which is why the dialog confirms every send.
- Octadock composites provider pixels only inside the selected rectangle. The
  rest of the approved image stays pixel-for-pixel identical to the local source.
- The review reports the exact number of pixels changed inside and outside the
  selection. Outside must be zero by construction.
- Rejected variants remain in memory only. The latest approved variant follows
  the source capture's retention/deletion lifecycle.

The adapter invokes `$imagegen` non-interactively with the crop and mask attached
through `codex exec --image`. Codex's built-in image generation uses
`gpt-image-2`; the workflow is documented in the
[Codex image-generation guide](https://learn.chatgpt.com/docs/image-generation).

## Current boundary

The deterministic crop/composite and retention paths are covered by local tests.
No live provider request is part of the automated test suite; a real request
still requires a signed-in Codex CLI and explicit confirmation in the dialog.
