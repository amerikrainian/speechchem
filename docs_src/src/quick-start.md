# Quick Start

## The board

- A research reactor is a 10 by 8 grid. Coordinates are column, then row, from (1, 1) at the top left.
- Inputs on the left: alpha (α) is (1, 1) to (4, 4), beta (β) is (1, 5) to (4, 8). Outputs on the right: psi (ψ) is (7, 1) to (10, 4), omega (ω) is (7, 5) to (10, 8). Columns 5 and 6 between them are the chamber. Some levels change this.
- Two waldos, red and blue, run at the same time. Each follows only instructions of its own colour, starting from its START marker and heading the way it points.
- A cell holds, per colour, one arrow and one other instruction. A waldo crossing it runs the instruction, then turns on the arrow.
- To deliver: drop the molecule wholly inside an output zone, then pass an output instruction for that zone. Repeat until the quota is met.
- Waldos never collide. Atoms do.

## Reading the board

- Alt+A / Alt+B read the alpha / beta input, Alt+P / Alt+O the psi / omega output: what each takes or wants and how many you've delivered. Add Shift (Alt+Shift+A and so on) to open that molecule.
- M in a zone opens its molecule as a small grid. For an input, each atom reads as the reactor cell it will appear on.
- Shift+R / Shift+B: where the red / blue waldo is (its START when stopped).
- Ctrl+R / Ctrl+B: the waldo's whole path as a list, every instruction it will hit and where it loops or stops. Check it before every run. Enter jumps to a line's cell.
- [ / ] pick a category (Inputs, Outputs, Hardware, Waldos), , / . jump through its items.
- Ctrl+arrows skip cells that read the same; Shift+Backspace gives a cell's full details.

## Writing a program

- The game's palette letters place an instruction at the cursor: W A S D arrows, E grab / drop, R input, F output, G rotate counter-clockwise, and so on.
- New instructions take the *active* colour, red at first. L switches it.
- ' / ; cycle the instruction's main setting: input alpha / beta, output psi / omega, grab / drop / grab-drop, an arrow's direction.
- For any other setting, colour included: Alt+Up / Alt+Down choose the setting, Alt+Left / Alt+Right change it. In a cell with both an instruction and an arrow, Shift+Up / Shift+Down choose which one you're editing. Backspace opens the game's right-click menu if you prefer that.
- Delete, Ctrl+X, Ctrl+C, Ctrl+V work on the active colour. Shift+Space on two corners marks a rectangle for them to work on. Ctrl+Z / Ctrl+Y undo and redo.

## Running and debugging

- 1 to 4 run at the four speeds, Space pauses, \` (backquote) stops.
- 0 runs to the next cycle where something happens (an input, grab, turn, rotation, output, wall hit) and pauses.
- The Run log has one region per cycle (Ctrl+Up / Ctrl+Down between cycles).
- After a reaction error, the error's entry in the Run log is a button: Enter opens the reactor frozen at the moment of the crash.

## Production levels

Later levels put several reactors on a pipeline map, a 32 by 22 grid. Its main stops:

- **Components**: a table, one row per building, columns for its inputs and outputs (where each comes from or goes, and its molecule). Enter on a reactor opens it; Enter on an output jumps to its pipe's end on the map.
- **Map**: every cell. Enter on a pipe end starts drawing: arrows lay or take back pipe, Enter finishes.
- **Shelf**: buildings to place. Enter arms one; Enter on the map places it at the cursor.

Ctrl+X then Ctrl+V on the map moves a building, Ctrl+C then Ctrl+V copies it, Backspace opens its menu.

To survey the map faster, zoom out: Shift+Up turns the cursor into a 4 by 4 block, then 8 by 8; Shift+Down zooms back in. The blocks are fixed: at 4 by 4 they are columns 1 to 4, 5 to 8 and so on, and rows likewise, so a block is always the same place. Arrows move a whole block.
