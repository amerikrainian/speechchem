# SpeechChem

A screen-reader accessibility mod for [SpaceChem](https://store.steampowered.com/app/92800/SpaceChem/)

## 0. WIP

This is work-in-progress. Pipeline and research have been verified and are at least usable; defense has largely been untouched.

## 1. Keys on every screen

| Key | Action |
|---|---|
| Tab / Shift+Tab | Next / previous stop (wraps) |
| Up / Down / Left / Right | Move within the stop |
| Enter, numpad Enter | Activate the focused item |
| Home / End | First / last item of the stop (grid rows: row start / end; run log: the whole log's ends) |
| Ctrl+Up / Ctrl+Down | Previous / next region (a journal issue, a run-log cycle); on grids, skip cells that read the same |
| Page Up / Page Down | Sliders only: large step up / down |
| Backspace | Secondary action (the right-click menu, Delete on a profile); in a text field it deletes a character |
| Shift+Backspace | Details of the focused item (the game's tooltip, atom info); "No details" when there are none |
| Escape | Back / cancel / close |

## 2. Reactor editor

### 2.1 Moving and reading

| Key | Where | Action |
|---|---|---|
| Arrows | Grid | Move one cell; "crossed quantum junction" on quantum levels |
| Home / End | Grid | Start / end of the row |
| Ctrl+arrows | Grid | Skip cells that read the same as this one |
| C | Grid | Coordinates |
| Shift+Backspace | Grid | Cell details: waldo state, atom info, the game's tooltip |
| Shift+Backspace | Palette | The instruction's description |
| M | Grid | The molecule of the zone under the cursor, in the molecule viewer |
| P | anywhere | Status |
| Ctrl+T | anywhere | Repeat the tutorial step |
| [ / ] | anywhere | Previous / next category: Instructions, Inputs, Outputs, Hardware, Waldos |
| , / . | anywhere | Previous / next item of the category, in reading order (moves the grid cursor; an instruction item arms it) |
| Shift+R / Shift+B | anywhere | Red / blue waldo: its start when stopped; cell, heading, holding, waiting while running |
| Ctrl+Shift+R / Ctrl+Shift+B | anywhere | The same, and move the grid cursor there |
| Ctrl+R / Ctrl+B | anywhere | Trace the red / blue waldo's path as a list |
| Enter | Molecules | Open the molecule (a chooser first when there are several) |

### 2.2 Editing

| Key | Where | Action |
|---|---|---|
| Q W E R T Y U I, A S D F G H J K | Grid | Place that palette slot's instruction at the cursor in the active colour, replacing what holds that slot of the cell. |
| Enter | Palette | Arm the instruction |
| Enter | Grid | Place the armed instruction |
| L | anywhere | Switch the active colour |
| Shift+arrows | Grid | Extend a rectangular selection |
| Delete | Grid | Remove the active colour's instructions in the cell or selection |
| Ctrl+X / Ctrl+C / Ctrl+V | Grid | Cut / copy / paste the active colour's instructions. Start markers move only by cut and paste and are never deleted or copied. Hardware (bonders, sensors, tunnels, lasers) moves only by cut and paste: Ctrl+X takes it when the active colour has nothing there (lock a layer to reach hardware under instructions); a paste that doesn't fit stays on the clipboard |
| Backspace | Grid | Context menu: the instruction's right-click menu, or the grid menu on an empty cell. Colour and icon-variant items apply on Enter and leave the menu open |
| Ctrl+Z / Ctrl+Y | anywhere | Undo / redo; the mod says what changed |

### 2.3 Running

| Key | Action |
|---|---|
| Space | Run; pauses when running |
| Backquote | Stop |
| 1 / 2 / 3 / 4 | Play at speed 1 to 4 |
| 0 | Step to the next event of the open reactor (any reactor on the pipeline screen) |
| Ctrl+0 | Step to the next event of any reactor |
| Escape | Research levels: stops a run; when stopped, opens the exit prompt. Production, defense and sandbox: returns to the pipeline, and the run keeps going |

Run events (grabs, drops, bonds, sensor hits, outputs, errors) are always logged in the Run log;
they are spoken only at speed 1 and while stepping, and inside a reactor only that reactor's events
(Ctrl+0 speaks every reactor's). Run state changes are always spoken.

## 3. Pipeline editor

Output notes on reactors are not yet supported.

Majority of keys are shared with reactor. Here's what we additionally add.

The Components stop is a table: one row per component, then a column per input and output ("N/A"
where a component has none). A column's name is spoken only when you cross into it.

| Key | Where | Action |
|---|---|---|
| Enter | Map | Start drawing from a pipe end, open a reactor, or place the armed item |
| M | Components, on a port | The port's molecule in the molecule viewer |
| Escape | while armed | Cancel the armed item |
| Ctrl+X | Components or Map | Cut the component |
| Ctrl+V | Map | Move the cut component here |
| Delete | Components or Map | Delete the component |
| Backspace | Components or Map | Component menu |
| Escape | anywhere | Exit prompt |

### 3.1 Drawing a pipe

| Key | Action |
|---|---|
| Arrows | Extend or retract the pipe |
| P | Pipe status |
| Enter / Escape / leaving the map | Finish |
