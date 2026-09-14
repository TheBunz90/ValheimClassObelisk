# TODO

Bugs and changes we want to track and fix one at a time. Add new items to the bottom of Open; move finished items to Done with a one-line note on the fix (and mention it in CHANGELOG.md if it's user-facing).

## Open

- [ ] **Class XP persists across character deletion / reuse of a character name.** Reported by a player: deleting a character save and creating a new one (even with a new save) still carries over previously-earned class XP. Intended behavior is XP tracked per character save, so deleting a save should delete its XP too. Suspect XP is being persisted locally (not inside the character save) and keyed by character name, so a new character reusing an old name inherits the old XP/levels and starts pre-leveled.
- [ ] **Second class slot (unlocked at level 50) can't actually be selected.** Per the design (see CHANGELOG.md 1.0.0), reaching level 50 with any class should unlock picking a second active class at the Obelisk, but there's currently no working way to do so.
- [ ] **Remove extra notifications** Currently there are a lot of notifications like "+X% Damage increase" that happen when trigger. I'd like to do a discovery dive on what all notifications are being generated throughout this mod and decide which to keep and which to remove.
- [ ] **Add Dual Class Notification** I noticed when playing with a friend once they reached max level it was not immediately apparent to them that they unlocked a second class slot. I'd like to think of some ways to make this much more apparent. Perhaps with an announcement similar to the "You've leveled up!" announcements.

## Done

<!-- - [x] Example: short description of the bug. Fixed by doing X (see commit abc1234 / CHANGELOG 1.0.5). -->
