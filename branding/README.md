# My Git: icon package

The approved My Git app icon: a near-black rounded square, a white outline crest shield, and a symmetric "Y merge" commit glyph (two branches merging into one centred commit).

![My Git icon](png/my-git-256.png)

## What's here

| Path | What it is |
|---|---|
| `my-git.ico` | Windows icon, 8 sizes (16, 20, 24, 32, 40, 48, 64, 256), each one drawn separately for that size |
| `my-git.icns` | macOS icon (16 to 1024 px) on Apple's grid: the rounded square at 824/1024 with a soft shadow, so it sits right in the Dock. `png/my-git-mac-1024.png` is the same, as a PNG |
| `png/my-git-{16…1024}.png` | Transparent PNGs, for in-app use, README, installer, store |
| `svg/my-git-icon.svg` | Master vector, for 48 px and larger |
| `svg/my-git-icon-medium.svg` | Thicker strokes, for 24–40 px |
| `svg/my-git-icon-small.svg` | Thickest strokes, no top dots, for 16–20 px |
| `my-git-outline-white.ico`, `my-git-outline-black.ico` | Outline-only shield (no background) for the window title bar, on dark and light themes |
| `png/contact-sheet.png` | All sizes side by side, to check the icon by eye |
| `render_icons.py` | Regenerates everything above (`python render_icons.py`, needs Pillow) |

## Integration (Phase 1, `src/MyGit.App`)

1. Copy `branding/my-git.ico` to `src/MyGit.App/Assets/my-git.ico`. Copy `branding/png/my-git-64.png` and `my-git-128.png` there too.
2. In `MyGit.App.csproj`:
   ```xml
   <PropertyGroup>
     <ApplicationIcon>Assets\my-git.ico</ApplicationIcon>
   </PropertyGroup>
   <ItemGroup>
     <AvaloniaResource Include="Assets\**" />
   </ItemGroup>
   ```
   `ApplicationIcon` sets the `.exe` icon (Explorer, pinned taskbar). The Avalonia resource is used at runtime.
3. In `MainWindow.axaml` set `Icon="/Assets/my-git.ico"`. This sets the title bar, the running taskbar entry, and Alt+Tab.
4. For a logo inside the app (title area, About, empty states), use `<Image Source="/Assets/my-git-64.png" Width="32" Height="32" />`. Avalonia doesn't render `.svg` files without an extra package, so use the PNGs, or convert the SVG path data to a XAML `DrawingImage` if a vector is needed.
5. Keep `branding/` in the repo as the source of truth. Don't hand-edit the generated files; change `render_icons.py` and re-run it.

## Spec (the grid is 120 × 120 units)

| Element | Value |
|---|---|
| Background | `#161615`, corner radius 26 |
| Artwork scale | Shield and glyph drawn at 70% of the grid, centred on (60, 61) (`ART_SCALE`) |
| Foreground | `#FFFFFF` |
| Shield ("crest", peaked top edge) | `M22 27 C40 27 50 22 60 14 C70 22 80 27 98 27 V58 C98 84 82 100 60 108 C38 100 22 84 22 58 Z`, round joins |
| Glyph | Branches from (48,42) and (72,42) curve into (60,68), stem down to the commit at (60,79) |

| Detail tier | Sizes | Shield stroke | Glyph stroke | Top dots | Bottom dot |
|---|---|---|---|---|---|
| Large | 48 px+ | 6 | 3 | r 4.5 | r 5.5 |
| Medium | 24–40 px | 8 | 4.5 | r 5.5 | r 6.5 |
| Small | 16–20 px | 11 | 9 | none | r 10 (branches spread 4 units wider) |

## Product family rule

This frame (background, corner radius, crest shield, stroke weights, colours) is the shared look for all of the owner's future products. Only the glyph inside the shield changes per product.
