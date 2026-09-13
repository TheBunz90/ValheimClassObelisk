# Changelog

## 1.0.3

- Fixed multiplayer class-damage/kill-XP tracking running on the wrong peer: damage is now tracked (and kill bonuses awarded) only on the machine that owns the target creature's ZDO, instead of independently and incorrectly on every peer that had it loaded.
- Fixed kill-bonus XP not reliably reaching the contributing player in multiplayer: kill XP is now broadcast to and applied on the contributing player's own client, rather than mutated on whichever peer happened to process the kill.
- Fixed active class selections not syncing to other players: selecting a class (or already having one selected from a previous session) is now broadcast to everyone, and a newly-joined player is caught up on everyone else's current selections automatically.
- Fixed kill bonus XP using a creature's un-scaled base health instead of its star-scaled max health, undercounting bonus XP from 2/3-star enemies.
- Removed the kill-bonus split across multiple eligible classes - each class a kill qualifies for now receives the full bonus rather than a divided share.
- Reworked weapon-to-class detection to use the weapon's own skill type instead of matching keywords in its name, so it correctly recognizes renamed, modded, and legendary weapons. As part of this, Crossbows now count for Archer and Polearms now count for Lancer, alongside their existing weapon types.

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
