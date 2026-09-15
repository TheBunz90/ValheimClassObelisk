# Changelog

## Unreleased

- Fixed leveling up only ever advancing one level per XP award, even when a kill granted enough XP to cross multiple level thresholds at once - a class's level now gets set to whatever its total XP actually corresponds to. Perk-unlock notifications are also fixed to announce every perk tier crossed in a multi-level jump (e.g. going from 38 to 42 now still announces the level-40 perk), not just whether the final level happens to land exactly on one.
- Fixed Archer's Arrow Slinger (and the equivalent Lancer spear perk) proccing from unrelated hits - e.g. a Greydwarf's thrown rock hitting some other nearby creature could incorrectly trigger it, even with no bow involved. The code determining who fired a hit projectile could fall back to "whichever player is standing nearby" when it couldn't otherwise identify an owner, which any monster-thrown projectile hits. Ownership and weapon type are now read directly from the projectile itself instead of guessed.

## 1.0.4

- Rebalanced class leveling so max level (50) is reachable in roughly 3-4 biomes of normal play instead of an extreme end-game grind. The old curve required 1,089,861 total XP to hit level 50 and grew 625x from level 1 to level 50, badly outpacing how much tougher enemies actually get across biomes - the last ~15 levels alone ate over 70% of the total XP requirement. The new curve tops out at 100,000 XP with a much gentler, still-smoothly-increasing growth rate that tracks enemy health scaling far more closely.
- Fixed class-level damage bonuses being applied twice: two independent, fully redundant systems were both patching combat damage with the same "+0.5% per level" scaling (one of them an orphaned earlier implementation left in place after being superseded), compounding into noticeably more bonus damage than intended. Only one system now applies the bonus.
- Fixed the second active class slot (meant to unlock once any class reaches level 50) being impossible to actually select: the Obelisk's class-selection UI only ever supported picking one class at a time, and the one selection method that was wired up always replaced your active class instead of adding to it. Selecting a class at the Obelisk now activates/deactivates it directly - swapping your one active class if you haven't unlocked a second slot yet, or freely activating/deactivating either of your two slots once you have (including running two non-max-level classes if you choose to deactivate your level-50 one).
- Removed the frequent per-hit combat notifications ("Weapon Mastery: +X% damage!", Fencer's Footwork, Weakpoint, Long Shot, Adrenaline Rush, Magic Shot, Cold Steel, Venom Applied) that could pop up on nearly every attack. The perks themselves are unchanged - only the chat spam is gone. Level Up!, Perk Unlocked!, and per-kill XP notifications are unaffected.
- Fixed a new character sometimes starting with class XP/levels it never earned, inherited from a different character (including one you'd already deleted). Cause: the main menu's character-preview model doesn't have a real player ID, and briefly previewing an existing leveled character there could get its class data cached and then baked into the next character you created in the same session. This only prevents the leak going forward - a save that already inherited the wrong XP before this fix isn't retroactively corrected.
- Added a one-time "Dual Class Unlocked!" announcement the moment any class first reaches level 50, and made the Compendium's "second class slot available" note more prominent (moved to the top of the entry, bold/gold instead of gray) - previously this milestone was communicated the same way as any other level-up, so it was easy to miss.

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
