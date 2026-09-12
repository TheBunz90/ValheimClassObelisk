# Changelog

## 1.0.2

- Fixed the Class Obelisk permanently showing the workbench's build-range circle and a floating "in use" hammer icon. Both are now forced off when the piece is built, since the vanilla `CraftingStation` component that normally hides them is removed from this piece.

## 1.0.1

- Fixed a crash/spam of `ArgumentOutOfRangeException` errors when opening the Hammer build menu, caused by Valheim 1.0.7 overhauling `PieceTable` internals. Requires Jotunn 2.30.0+.

## 1.0.0

Initial release.

- Craftable Class Obelisk piece (Hammer, Crafting category) for selecting a combat class.
- 8 classes: Sword Master, Archer, Crusher, Assassin, Brawler, Wizard, Lancer, Bulwark, each tied to a weapon type and dealing bonus damage with it.
- Classes level from combat XP (1-50), unlocking a passive perk every 10 levels.
- A second class slot unlocks once any class reaches level 50.
- "Active Classes" entry in the Valheim Compendium (raven icon) showing each active class's level and perks, with locked perks masked as "???".
