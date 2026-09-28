# Test font

`LiberationSans-Regular.ttf` is **Liberation Sans Regular, version 2.1.5**, taken unmodified from the
official upstream release: `liberation-fonts-ttf-2.1.5.tar.gz` on
<https://github.com/liberationfonts/liberation-fonts/releases/tag/2.1.5>
(archive SHA-256 `7191c669bf38899f73a2094ed00f7b800553364f90e2637010a69c0e268f25d0`,
font SHA-256 `76d04c18ea243f426b7de1f3ad208e927008f961dc5945e5aad352d0dfde8ee8`).

It is licensed under the **SIL Open Font License, Version 1.1**, which permits the font to be bundled
and redistributed with software, including in a public repository, provided the licence travels with
it. [`LICENSE`](LICENSE) is the upstream licence file, verbatim.

## Why it is here

The renderer draws text (marker labels, place names, the attribution) with whatever typeface it is
given. Left to SkiaSharp's default, that comes from the host's installed fonts, so text-drawing tests
depended on the machine: they passed on a Windows workstation and could not pass on the CI runner,
whose image has no fonts and no fontconfig at all. The tests now point `MapsOptions.FontPath` at this
file (see `TestFonts.cs`), so every machine draws with the same font and the label tests run in CI.

Only the Regular face is included: bold label text is produced by SkiaSharp's synthetic emboldening,
not by a separate bold font file.
