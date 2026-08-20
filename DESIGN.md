<!-- SEED: re-run $impeccable document once the production shell has real tokens and components. -->
---
name: AI Drawer
description: A lightweight native workspace for returning to the right AI context.
---

# Design System: AI Drawer

## Overview

**Creative North Star: "The Quiet Pull"**

AI Drawer should feel like a compact tool drawer sliding into reach on a calm Windows desktop: immediate, ordered, and present only when needed. The interface combines Raycast's keyboard confidence, Arc's treatment of work contexts, and the earned familiarity of Windows 11 native controls. It is modern without looking like a generic AI showcase.

Motion is responsive rather than choreographed. Provider content remains visually dominant; native structure explains navigation, lifecycle, compatibility, permissions, recovery, and external boundaries without competing for attention.

**Key Characteristics:**

- restrained native surfaces;
- one quiet brand accent;
- compact navigation with generous content space;
- clear workspace state and keyboard focus;
- no decorative AI spectacle.

## Current MVP Visual Baseline

This direction is provisionally approved for the first implementation and should remain stable unless a later design review explicitly replaces it.

- Use the latest black-and-white minimal AI Drawer mark as the temporary application logo. Its exact silhouette and final brand color remain open for later refinement.
- Use one compact native workspace bar across the top of the window. It contains the temporary product mark, open workspace names, a borderless add action, a borderless overflow action, and standard Windows window controls.
- Let the active provider WebView fill the remaining window. Do not add a persistent home page, sidebar, provider-card grid, browser address bar, status dashboard, or secondary navigation layer.
- Distinguish the active workspace through surface brightness, a compact soft shadow, and at most one small state accent. Unselected workspaces remain plain text.
- Use spacing, tonal surfaces, and light before lines. Decorative separators, active underlines, and boxed toolbar controls are not part of the approved baseline.

## Colors

Use a restrained strategy built from neutral Windows surfaces and at most one brand accent occupying no more than 10% of a screen. The first moss-green exploration was rejected, so the final hue remains intentionally open until the mark and shell hierarchy are approved. Temporary concepts may use a small solid blue-violet state accent to test hierarchy, but that color is not yet a brand decision. Never turn the accent into a gradient wash or decorative glow.

**The One Accent Rule.** Brand color is reserved for the app mark, current selection, primary action, focus, and meaningful active state.

**The Provider Neutrality Rule.** Provider brand colors and marks never become AI Drawer's own visual identity.

## Typography

Use a single humanist sans direction, led by the Windows system type family. The hierarchy is compact and functional: clear titles, readable body copy, quiet labels, and no display typography inside the application shell. Exact font roles and sizes will be resolved against real WinUI controls.

**The Native Rhythm Rule.** Typography must feel at home beside Windows system UI, not like a web dashboard embedded in a desktop frame.

## Elevation

The system uses quiet ambient light, tonal surface differences, and a very small elevation range instead of drawing borders around every region. The workspace bar sits one level above provider content; the selected workspace rises one additional level through a lighter surface and compact diffuse shadow. Menus and dialogs rise further, but no ordinary surface combines a decorative border with a wide shadow. Motion runs for state acknowledgement, selection, pane changes, and loading transitions only.

**The Light Before Line Rule.** Use surface tone, spacing, and soft elevation before introducing a divider. Lines are reserved for keyboard focus, errors, structural boundaries that cannot be communicated otherwise, and high-contrast mode.

## Do's and Don'ts

### Do:

- **Do** let provider content occupy most of the window.
- **Do** use familiar Windows navigation, tabs, dialogs, focus states, and keyboard behaviors.
- **Do** represent AI through subtle active-state energy integrated into the drawer metaphor.
- **Do** keep workspace labels user-controlled rather than scraping provider page titles.
- **Do** make compatibility, purchase, privacy, and recovery boundaries explicit in native UI.

### Don't:

- **Don't** build a general-purpose browser clone with an address bar, bookmarks, and unbounded tabs.
- **Don't** create a colorful marketplace grid that presents providers as collectible AI products.
- **Don't** use neon gradients, glowing orbs, glass cards, robot imagery, or decorative sparkles as generic AI branding.
- **Don't** create a dense enterprise dashboard with analytics or unrelated account-management chrome.
- **Don't** imply that AI Drawer owns provider accounts, subscriptions, conversations, or payment flows.
- **Don't** use display fonts, oversized radii, nested cards, or decorative motion inside the product shell.
