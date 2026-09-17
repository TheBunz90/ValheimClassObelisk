# Changelog

## 1.1.1

- Fixed Executioner's Woodsman's Carry (Level 20), Rending Rhythm (Level 30), Hemorrhage (Level 40), and Execute's low-health bonus (Level 50) not applying at all - the class was missing a required registration attribute, so those four perks silently never activated since Executioner was first introduced. Only the flat "+8%/+15% axe damage" portions of Chopper's Training and Execute were ever actually working. All five perk tiers now function as described.

## 1.1.0

**⚠️ SPOILERS AHEAD.** This release is a complete rework of every class's perks, plus two brand-new classes. Every perk below is listed in full detail. If you'd rather discover them for yourself in-game, stop reading now and skip to 1.0.5.

Every class's perk kit has been redesigned from the ground up onto a consistent 5-tier structure (a passive damage bonus at 10, a defensive/utility perk at 20, an active proc at 30, a bonus elemental damage type at 40, and a capstone at 50). Along the way this fixed a number of long-standing bugs: several classes' Level 10 damage bonuses were silently being applied twice, several movement-penalty-removal and weight perks were mutating the shared item data of every copy of that weapon in the world (rather than just the wearer's), and several buffs (combo counters, Rage, elemental damage tracking) were tracked globally instead of per-player, so one player's damage could trigger another player's buff.

Two new classes have joined the roster, bringing the total from 8 to 10:
- **Executioner** (Axes & Battleaxes) - axes have been split out of Sword Master into their own dedicated class.
- **Warlock** (Blood Magic Staves) - Blood Magic has been split out of Wizard into its own class built around health-sacrifice casting, Bloodwell charges, and life-stealing summons. Wizard is now a pure Elemental Magic specialist.

Also: Warlock's Bloodwell and Sanguine Reclamation buffs and Wizard's Storm Strider and Verdant Aura buffs now have their own dedicated icons instead of falling back to your currently equipped weapon's icon, and Storm Strider now also grants +20% jump height on top of its movement speed bonus.

### Sword Master (Swords)
- Level 10 - Blade Training: +7% sword damage.
- Level 20 - Duelist's Balance: Sword stamina costs are reduced by 10%, and swords impose no movement speed penalties.
- Level 30 - Riposte Training: One-handed sword hits within 2s after a parry deal +30% damage. Greatsword special attacks within 2s after a parry deal +30% damage and +20% stagger.
- Level 40 - Searing Edge: Sword attacks deal bonus fire damage equal to 12% of weapon damage.
- Level 50 - Dancing Steel: +15% sword attack speed. Parrying grants an additional +10% sword damage for 5s.

### Executioner (Axes & Battleaxes) — NEW CLASS
- Level 10 - Chopper's Training: +8% axe damage with one-handed axes and battleaxes.
- Level 20 - Woodsman's Carry: Axes weigh 50% less and impose no movement speed penalties.
- Level 30 - Rending Rhythm: One-handed axe hits against the same target build up to 3 stacks; each stack grants +5% damage to that target for 5s. Battleaxe special attacks apply all 3 stacks at once.
- Level 40 - Hemorrhage: Axe attacks apply bleed, dealing slash damage over time equal to 12% of weapon damage over 5s. Reapplying bleed refreshes the duration.
- Level 50 - Execute: +15% axe damage. Axe attacks deal an additional +10% damage against enemies below 40% health.

### Archer (Bows & Crossbows)
- Level 10 - Practiced Aim: +7% bow and crossbow damage.
- Level 20 - Magic Shot: 25% chance to not consume arrows or bolts.
- Level 30 - Combat Rhythm: Bow hits grant Arrow Slinger for 6s, reducing draw time by 25%. Crossbow hits grant Quick Crank for 6s, reducing reload time by 25%.
- Level 40 - Storm Fletching: Bow and crossbow attacks deal bonus lightning damage equal to 12% of weapon damage.
- Level 50 - Deadeye: +15% bow and crossbow damage. Hits beyond 25m gain an additional +10% damage.

### Crusher (Maces & Hammers)
- Level 10 - Bonebreaker: +8% blunt damage with clubs, maces, and two-handed hammers.
- Level 20 - Colossus: Blunt weapons weigh 50% less and impose no movement speed penalties.
- Level 30 - Thundering Blows: One-handed mace heavy attacks create a 3m shockwave for 25% weapon damage. Two-handed hammer attacks create a 3m aftershock on direct enemy hits for 20% weapon damage.
- Level 40 - Cold Steel: Blunt attacks deal bonus frost damage equal to 12% of weapon damage.
- Level 50 - Earthshaker: +15% blunt damage. Staggering an enemy grants +10% attack speed with blunt weapons for 5s.

### Assassin (Knives)
- Level 10 - Cutthroat: +7% knife damage.
- Level 20 - Silent Hands: Knife attacks and stealth movement consume 15% less stamina. Additionally, you move at normal movement speed while crouched.
- Level 30 - Assassination: First knife hit from stealth deals +50% damage. After a stealth hit, knife attack speed is increased by 20% for 5s.
- Level 40 - Venom Coating: Knife hits apply stacking poison damage over time, up to 3 stacks. Poison damage scales with knife skill.
- Level 50 - Twist the Knife: +15% knife damage against poisoned targets. Poisoned enemies deal 10% less damage to you.

### Brawler (Unarmed)
- Level 10 - Bare-Knuckle Training: +8% unarmed damage.
- Level 20 - Light on Your Feet: Unarmed attacks consume 10% less stamina and jumping while unarmed consumes 10% less stamina.
- Level 30 - One-Two Combo: Every 3rd consecutive unarmed hit deals +50% damage and restores 5% stamina. Combo resets after 4s without an unarmed hit.
- Level 40 - Iron Fist: Unarmed attacks deal bonus spirit damage equal to 10% of weapon damage equivalent.
- Level 50 - Rage: After taking damage, enter Rage for 5s: +10% unarmed damage and +20% attack speed. 20s cooldown.

### Bulwark (Shields)
- Level 10 - Shield Wall: +8% block power and -8% block stamina cost.
- Level 20 - Shield Bearer: Shields impose no movement speed penalties.
- Level 30 - Perfect Guard: Blocking within the first moment of an incoming hit restores 8 stamina and empowers your next attack or shield bash within 4s for +20% stagger.
- Level 40 - Thorns: Blocked attacks return pierce damage equal to 20% of the original blocked damage.
- Level 50 - Reverb!: After blocking 200 damage, release a 5m shockwave dealing 200 blunt damage and high stagger. 10s cooldown.

### Lancer (Spears & Polearms)
- Level 10 - Reach Advantage: +8% pierce damage with spears and polearms.
- Level 20 - Balanced Grip: Thrown spears automatically return to the player after reaching their target, and polearms impose no movement penalties.
- Level 30 - Spear Storm: Spear hits build up to 5 stacks; each stack grants +3% attack speed for 5s. Polearm hits build up to 5 stacks; each stack grants +3% stagger damage for 5s.
- Level 40 - Stormpoint: Spear and polearm attacks deal bonus lightning damage equal to 12% of weapon damage.
- Level 50 - Impaling Momentum: +15% spear and polearm damage. Thrown spear hits beyond 15m and polearm special attacks gain an additional +10% damage.

### Wizard (Elemental Magic Staves)
- Level 10 - Eitr Weave: +7% elemental magic damage.
- Level 20 - Arcane Nourishment: Eitr granted by food is increased by 15%.
- Level 30 - Arcane Surge: Dealing elemental magic damage grants +25% Eitr regeneration for 6s. Additional elemental magic damage refreshes the duration.
- Level 40 - Arcane Efficiency: Elemental magic weapon Eitr costs are reduced by 10%.
- Level 50 - Archmage: Dealing 500 damage of a single elemental affinity (fire, frost, lightning, or poison/nature) triggers its matching aura - Immolation Aura, Frost Armor, Storm Strider, or Verdant Aura - for 20s. Only one Archmage aura may be active at a time, and damage dealt while one is active isn't tracked.

### Warlock (Blood Magic Staves) — NEW CLASS
- Level 10 - Forbidden Knowledge: +7% blood magic damage and summoned creature damage.
- Level 20 - Blood Feast: Health granted by food and food-based health regeneration are increased by 15%.
- Level 30 - Blood Pact: Sacrificing health with a blood magic weapon grants Blood Pact for 8s, increasing blood magic and summoned creature damage by 20%. Additional qualifying sacrifices refresh the duration.
- Level 40 - Dark Efficiency: Blood magic weapon Eitr costs are reduced by 10%. Health sacrifice costs are unchanged.
- Level 50 - Sanguine Reclamation: Health-sacrificing blood magic casts build Bloodwell charges. At 4 charges, gain Sanguine Reclamation for 20s: blood magic and summoned creature damage restores 5% of damage dealt as health, capped at 5 health per second.

Warlock starts at level 0 for everyone - there's no XP carried over from the old combined Wizard, the same way Executioner started fresh when it split out of Sword Master.

## 1.0.5

- Fixed leveling up only ever advancing one level per XP award, even when a kill granted enough XP to cross multiple level thresholds at once - a class's level now gets set to whatever its total XP actually corresponds to. Perk-unlock notifications are also fixed to announce every perk tier crossed in a multi-level jump (e.g. going from 38 to 42 now still announces the level-40 perk), not just whether the final level happens to land exactly on one.
- Fixed Archer's Arrow Slinger (and the equivalent Lancer spear perk) proccing from unrelated hits - e.g. a Greydwarf's thrown rock hitting some other nearby creature could incorrectly trigger it, even with no bow involved. The code determining who fired a hit projectile could fall back to "whichever player is standing nearby" when it couldn't otherwise identify an owner, which any monster-thrown projectile hits. Ownership and weapon type are now read directly from the projectile itself instead of guessed.
- Fixed Assassin's Venom Coating poison roughly double-applying damage by fighting the game's own poison system instead of just using it. Poison duration now scales with the total damage applied rather than always lasting a fixed 10 seconds. Also fixed the Envenomous movement slow incorrectly stacking on top of itself across poison stacks, and fixed it applying before the Level 30 perk that's supposed to unlock it.
- Removed the flat +0.5% weapon damage bonus every class was passively getting per level, on top of its named tier perks. Each class's damage bonus now comes only from its advertised perks.

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
