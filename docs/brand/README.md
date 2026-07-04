# Octadock Brand Notes

Status: active baseline  
Last updated: 2026-07-04

## Name

The product name is **Octadock**.

Rebranded surfaces in the clean repo include:

- solution and project names;
- namespaces and assembly names;
- app executable name: `Octadock.exe`;
- CLI executable name: `octadock.exe`;
- protocol scheme: `octadock://`;
- local app data root: `%LOCALAPPDATA%\Octadock`;
- annotation project extension: `.octadock`;
- primary environment variables: `OCTADOCK_*`.

The code keeps transitional support for selected legacy `SNAPDOCK_*` environment
variables so existing local keys can keep working while the rebrand settles.

## Logo Assets

The supplied blue Octadock image is stored and processed into transparent app
assets:

- source copy: `docs/brand/assets/octadock-logo-source.png`;
- transparent master: `docs/brand/assets/octadock-logo-transparent.png`;
- app PNG: `src/Octadock.App/Resources/Icons/octadock-256.png`;
- app ICO: `src/Octadock.App/Resources/Icons/octadock.ico`;
- verification previews:
  - `docs/brand/assets/previews/octadock-logo-checker-preview.png`;
  - `docs/brand/assets/previews/octadock-logo-dark-preview.png`.

The final app asset uses the original supplied image with border-connected
background removed into a real alpha channel. Natural white highlights on the
body remain part of the logo.

## Image Generation Note

Image GPT outputs were tested as an alternate source, but the generated files
contained a baked checkerboard pattern and no alpha channel. Those outputs were
not used for the app icon. If a future generated asset is used, verify the saved
file has actual transparency by checking the alpha channel and by compositing it
over dark and checkerboard backgrounds.

## Asset Acceptance Checklist

- Saved PNG has an alpha channel.
- Corner pixels have alpha 0.
- The asset looks clean over a dark background.
- The asset looks clean over checkerboard.
- The Windows `.ico` includes 16, 24, 32, 48, 64, 128, and 256 pixel sizes.
- App project references `Resources\Icons\octadock.ico`.

