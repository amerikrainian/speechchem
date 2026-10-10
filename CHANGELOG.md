# Changelog

## V0.1.2

- Fixed a bug in one of the later defense levels with mislabeling graph signatures.
- First attempt at cutting down on verbosity.
- Bonders now speak priority if the option to show them is turned on.

## V0.1.1

- We now allow you to switch to a different reactor with ctrl+tab/ctrl+shift+tab, or directly to reactor with that number with ctrl+corresponding digit.
- In production levels, closing the error overlay is now done with the ` key or by editing a reactor/pipeline as opposed to escape (latter still works on research levels). This should hopefully give greater visibility into things like pipe occupancy and allow you to actually view the pipeline mid-run.
- We now tell you what molecule(s) are in your pipe(s) when scrolling over them as opposed to generic counts.
- We now have configuration for what gets spoken per-event. It's under options. ... it might also be borderline psychotic and far too granular, we'll see.

## V0.1.0

- Removed instructions from categories; they had shortcut keys already.
- Added ctrl+G to check progress, ctrl+q to check reactor quota, ctrl+s to check symbols, cycles and reactors, alt a/b for alpha and beta inputs, alt p/o for psi/omega outputs. Alt+shift a/b/p/o now open the molecule viewer for that input/output, ctrl+shift p/o edit the psi/omega output notes.
- Drawing pipes no longer locks you to the grid.
- Added pipeline shelf as a category to place buildings while remaining on a grid.
- Added zoom to the pipeline map: shift up/down zoom out and in through 4x4 and 8x8 blocks, ctrl+shift up/down jump straight to 8x8 or back to 1x1. Blocks tell you what's in them, shift+backspace for the details.
- p on a pipe now tells you where it goes, and how many molecules are in it during a run.
- Added alt+1 to jump to the grid/map, alt+` to the pipeline table, alt+2 to the palette/shelf, alt+3 to the tools, alt+4 to the run log, alt+5 to the layers or the enemy. Alt+1 from a table row and alt+` from a building on the map take you to that building. Alt+backspace takes you back to where you were before the jump.
- Getting an error now immediately opens the crashed reactor's state. Editing said state or hitting escape closes the dialogue.

## V0.0.9

- Added ; and ' keys to cycle instruction primary parameter.

## V0.0.8

- We now include tooltips for buildings on the map.
- When starting a research level, you're told the research objective.

## V0.0.7

- Rectangle selection now works by having you place corners with shift+space, Ctrl+space clears the rectangle.
- Shift left/right cycle through the instruction colors on the given cell. Shift up/down cycle over the instructions of that color. Alt up/down cycle through instruction parameters, e.g., colors, output type. Alt left/right change parameter values.
- We have a draft of the manual now guiding you through the first two tutorials.

## V0.0.6

- Mutually exclusive instruction parameter options are now rendered as horizontal rows as opposed to one vertical list.

## V0.0.5

- We now announce when you're leaving an input/output zone.

## V0.0.4

- 0 no longer steps by cycles, but rather by an event(s) we log.
- 0 always steps by the fastest possible speed so we get instant feedback when tracing.
- 0 Respects your currently open reactor. Ctrl+0 steps by any event, regardless of reactor.
- We now support output notes.
- We now include inputs/outputs when browsing reactors on shelves.
- First pass at the defense screen.
- We now allow you to customize how events are spoken and give more keys for you to configure for stepping (see settings)

## V0.0.3

- Reduce verbosity of pipelines; label components as tables for clarity in pipelines.

## V0.0.2

- More coverage of screens: pipeline editor, custom puzzle journal, DLC.

## V0.0.1

- First release
