# Wizard & Warlock Implementation Notes

This document expands on the concise perk descriptions in `class-perks-rework-updated.json`. The values below are intended as the initial implementation/balance targets for the two magic classes.

## Class Split

### Wizard
Wizard is exclusively tied to **Elemental Magic**. Its combat identity is sustained spellcasting, Eitr management, and adapting to the elemental affinity of the spell being used.

Associated weapons:
- Staff of Embers
- Staff of Frost
- Staff of Fracturing
- Dundr
- Staff of the Wild
- Lightning Strike

### Warlock
Warlock is exclusively tied to **Blood Magic**. Its combat identity is health sacrifice, summons, risk/reward, and eventually recovering life by aggressively using Blood Magic after paying its health costs.

Associated weapons:
- Staff of Protection
- Dead Raiser
- Trollstav
- Echo Spike
- Northern Vengeance
- Spirit Caller

---

# Wizard Details

## Level 10 - Eitr Weave
**Effect:** +7% Elemental Magic damage.

This is intentionally a small, always-on reward and should apply to damage originating from Elemental Magic weapons.

## Level 20 - Arcane Nourishment
**Effect:** Eitr granted by food is increased by 15%.

Implementation notes:
- Apply the bonus to the food's Eitr contribution, not to the player's base Eitr.
- The bonus should scale each qualifying food independently.
- Example: a food that grants 80 Eitr should grant 92 Eitr to a Wizard with this perk.
- This perk should not directly modify Eitr regeneration.

## Level 30 - Arcane Surge
**Effect:** Dealing Elemental Magic damage grants +25% Eitr regeneration for 6 seconds.

Implementation notes:
- Any successful damage event from an Elemental Magic weapon can activate the buff.
- Additional qualifying damage refreshes the duration back to 6 seconds rather than stacking the regeneration bonus.
- Recommended behavior: one active Arcane Surge instance per player.
- The intent is to reward continuous offensive casting without directly lowering cast costs before level 40.

## Level 40 - Arcane Efficiency
**Effect:** Elemental Magic weapon Eitr costs are reduced by 10%.

Implementation notes:
- Apply the reduction to Eitr cost only.
- Do not alter attack damage, elemental damage type, durability use, or any other weapon-specific behavior.
- This reduction should apply consistently to all Wizard-associated Elemental Magic weapons.
- If Valheim's Elemental Magic skill already reduces Eitr costs, this perk should multiply against the final/skill-adjusted cost rather than replacing the vanilla skill scaling.

## Level 50 - Archmage
**Core mechanic:** Elemental affinity damage fills a separate affinity meter. Reaching **500 accumulated damage** for an affinity activates that affinity's aura for **20 seconds**.

### General Archmage rules
- Track **Fire, Frost, Lightning, and Nature/Poison** damage separately.
- Only damage dealt with Wizard-associated Elemental Magic weapons should count toward these meters.
- Reaching 500 damage in one affinity activates that affinity's aura.
- Activating an aura resets that affinity's meter to 0.
- Other affinity meters keep their accumulated progress.
- Only **one Archmage aura may be active at a time**.
- Triggering a different aura replaces the currently active aura and starts the new aura at its full 20-second duration.
- Damage dealt while an aura is active may continue filling affinity meters unless playtesting shows this causes excessive aura chaining.

### Fire - Immolation Aura
**Theme:** Offensive proximity damage.

Recommended effect:
- The Wizard is surrounded by flames for 20 seconds.
- Nearby hostile creatures periodically take Fire damage.
- Recommended radius: **5 meters**.
- Recommended pulse rate: **once per second**.
- Damage should be meaningful but secondary to the player's actual spell damage. It should not become the primary damage source.

Important note:
- Fire no longer needs to grant movement speed. Moving that utility to Lightning gives each affinity a cleaner identity.

### Frost - Frost Armor
**Theme:** Defensive elemental transformation.

Recommended effect:
- Increase player armor for 20 seconds.
- Grant immunity to Fire damage for the duration.

Recommended starting armor bonus:
- **+25% armor**, or a flat armor value if percentage armor proves awkward with Valheim's armor formula.

Important note:
- Fire immunity should mean immunity to Fire damage while the aura is active, but it does not need to cleanse unrelated status effects unless intentionally implemented that way.

### Lightning - Stormstride
**Theme:** Speed and mobility.

Recommended effect:
- **+20% movement speed for 20 seconds.**

Optional secondary effect if the aura feels too plain in testing:
- +10% jump height.

Recommendation:
- Start with movement speed only. It is already a powerful and immediately noticeable benefit in Valheim.

### Nature / Poison - Verdant Aura
**Theme:** Regeneration and support.

Recommended effect:
- Heal the Wizard and nearby friendly entities for **2 HP per second**.
- Recommended radius: **8 meters**.
- Duration: 20 seconds.

Eligible friendly targets should include:
- The Wizard
- Nearby allied players
- Tamed creatures
- Friendly player-owned summons, where technically practical

Important notes:
- Use regeneration/pulses rather than one large instant heal.
- Do not heal hostile or neutral creatures.
- If poison and nature are represented by separate internal damage types/events, they should feed the same Nature/Poison Archmage meter.

---

# Warlock Details

## Level 10 - Forbidden Knowledge
**Effect:** +7% Blood Magic damage and summoned creature damage.

Implementation notes:
- Direct Blood Magic attacks should receive the damage bonus.
- Player-owned creatures summoned through Blood Magic should receive the summon damage bonus.
- If retaliation damage from a Blood Magic weapon can be reliably attributed to the Warlock, it should also be treated as Blood Magic damage for class perk purposes.

## Level 20 - Blood Feast
**Effect:** Health granted by food and food-based health regeneration are increased by 15%.

Implementation notes:
- Increase each food item's maximum-health contribution by 15%.
- Increase the health regeneration supplied by active food by 15%.
- Do not increase the player's unrelated/base health regeneration unless it is explicitly part of Valheim's food regeneration calculation.
- Example: a food granting 100 health should grant 115 health to a Warlock with this perk.

Design intent:
- A larger health pool gives the Warlock more room to survive health-sacrificing casts.
- Increased food regeneration helps recover from those sacrifices without removing the sacrifice mechanic itself.

## Level 30 - Blood Pact
**Effect:** Sacrificing health with a Blood Magic weapon grants Blood Pact for 8 seconds, increasing Blood Magic and summoned creature damage by 20%.

Implementation notes:
- Blood Pact should trigger only from casts that actually use the weapon's health-sacrifice mechanic.
- Echo Spike does not need to trigger Blood Pact if it has no health-sacrifice cost.
- Additional qualifying sacrifices refresh Blood Pact to 8 seconds rather than stacking multiple +20% bonuses.
- The buff should apply to direct Blood Magic damage and player-owned Blood Magic summons.
- Existing summons should benefit while Blood Pact is active, not only summons created during the buff window.

Design intent:
- Sacrifice health first, then capitalize on the temporary offensive window.
- Echo Spike becomes especially useful as a low-commitment attack weapon during an already-active Blood Pact.

## Level 40 - Dark Efficiency
**Effect:** Blood Magic weapon Eitr costs are reduced by 10%. Health sacrifice costs are unchanged.

Implementation notes:
- Only reduce Eitr cost.
- **Do not reduce health sacrifice.** Health sacrifice is central to the Warlock's class loop and to Sanguine Reclamation.
- If the Blood Magic skill already reduces Eitr cost, this perk should multiply against the final/skill-adjusted Eitr cost rather than replacing vanilla scaling.

---

# Level 50 - Sanguine Reclamation

Sanguine Reclamation is the Warlock's mastery mechanic. Repeated health-sacrificing casts fill a small hidden or visible resource called the **Bloodwell**. When the Bloodwell reaches its threshold, the Warlock gains a temporary life-stealing state.

## Bloodwell Charges

Recommended charge rules:

| Cast Type | Bloodwell Charge |
|---|---:|
| Standard health-sacrificing Blood Magic cast | +1 |
| Very-high-sacrifice cast such as Trollstav | +2 |
| Blood Magic cast with no health sacrifice, such as Echo Spike | +0 |

**Activation threshold:** 4 Bloodwell charges.

Examples:
- Staff of Protection (+1) -> Spirit Caller (+1) -> Dead Raiser (+1) -> Staff of Protection (+1) = **4 charges**
- Trollstav (+2) -> Trollstav (+2) = **4 charges**

## Why charges should not use raw HP lost
Blood Magic skill can alter the amount of health paid by a cast. If Sanguine Reclamation required a fixed amount of actual HP to be lost, improving Blood Magic skill could accidentally make the mastery perk slower to trigger.

Using cast-based charges keeps the perk predictable regardless of the player's maximum health or Blood Magic skill.

## Trigger Behavior
When Bloodwell reaches 4 charges:
1. Consume/reset Bloodwell to 0 charges.
2. Activate **Sanguine Reclamation** for 20 seconds.
3. While active, qualifying Blood Magic damage heals the Warlock for **5% of damage dealt**.
4. Healing is capped at **5 HP per second**.

## Qualifying Damage
Recommended sources:
- Direct Blood Magic weapon damage
- Echo Spike damage
- Damage dealt by player-owned Blood Magic summons
- Blood Magic retaliation damage, if ownership can be reliably attributed to the Warlock

Non-qualifying sources:
- Normal melee/ranged weapon damage
- Environmental damage
- Damage from another player's summons
- Unrelated status effects that cannot reliably be tied to a Blood Magic attack

## Existing Summons
Summons that were created before Sanguine Reclamation activates should still be able to heal their owner through qualifying damage while the effect is active.

This is important for Dead Raiser, Trollstav, and Spirit Caller gameplay because their creatures may remain active across multiple casts and perk states.

## Healing Cap
Recommended starting cap: **5 HP per second**.

The cap prevents high-damage summons or multiple simultaneous attackers from instantly erasing the health cost that defines Blood Magic. Healing from multiple sources during the same one-second window should share the same 5 HP cap.

Example:
- Echo Spike damage produces 3 HP of healing.
- A summoned creature then produces another theoretical 4 HP during the same one-second window.
- Only 2 additional HP is restored because the player has reached the 5 HP/sec cap.

## Interaction With Blood Pact
Blood Pact and Sanguine Reclamation should be allowed to overlap.

This creates the intended late-game loop:
1. Spend health to cast Blood Magic.
2. Gain/refresh Blood Pact and build Bloodwell charges.
3. Reach 4 Bloodwell charges.
4. Enter Sanguine Reclamation.
5. Use the Blood Pact damage window, direct Blood Magic, Echo Spike, and existing summons to reclaim health aggressively.

## Re-triggering While Active
Recommended initial rule:
- Bloodwell charges may **not** accumulate while Sanguine Reclamation is active.

Reason:
- This guarantees a recovery gap between activations and prevents high-level Warlocks from chaining nearly permanent lifesteal through repeated sacrifice casts.

If playtesting shows the perk is too difficult to maintain or feels sluggish, a later alternative is to allow charges to accumulate during the aura but prevent activation until the current Sanguine Reclamation expires.

---

# Suggested UI / Feedback

These mechanics will be easier for players to understand if they have visible feedback.

### Wizard
- Small affinity meter or four compact affinity indicators for Fire, Frost, Lightning, and Nature/Poison.
- Distinct visual aura for each Archmage state.
- Optional short status-effect name showing remaining aura duration.

### Warlock
- Bloodwell charge indicator showing **0/4 through 4/4**.
- Brief pulse or sound when a health-sacrificing cast adds a charge.
- Strong crimson/dark visual effect when Sanguine Reclamation activates.
- Status-effect timer for Blood Pact and Sanguine Reclamation.

---

# Balance Knobs to Expose in Config

If practical, make the following values configurable so they can be tuned without recompiling:

### Wizard
- Level 10 damage bonus: 7%
- Level 20 food Eitr bonus: 15%
- Level 30 Eitr regeneration bonus: 25%
- Level 30 duration: 6s
- Level 40 Eitr cost reduction: 10%
- Archmage trigger damage: 500
- Archmage duration: 20s
- Immolation radius / damage / pulse rate
- Frost Armor bonus
- Stormstride movement-speed bonus
- Verdant Aura radius / healing-per-second

### Warlock
- Level 10 damage bonus: 7%
- Level 20 food health bonus: 15%
- Level 20 food regeneration bonus: 15%
- Blood Pact damage bonus: 20%
- Blood Pact duration: 8s
- Level 40 Eitr cost reduction: 10%
- Bloodwell threshold: 4 charges
- Standard sacrifice charge value: 1
- High-sacrifice charge value: 2
- Sanguine Reclamation duration: 20s
- Lifesteal percentage: 5%
- Healing cap: 5 HP/sec
