# Tribes, alliances and empire management

Press **O while on foot or driving**, or open **I → EMPIRE [O]**. The existing UI Toolkit inventory window hosts the management menu and starts closed. O toggles it; Esc or Close hides it. O retains its existing orbital-guide function while piloting a spaceship.

## Forming an organization

An independent player can found one named tribe and becomes its leader. Invite registered unaligned players from **People**; each player must accept their own invitation in **Diplomacy**. Leaders appoint officers, remove members and explicitly transfer leadership. Officers can recruit and issue orders, but cannot change sovereign policy or negotiate independent alliances. A player belongs to one tribe at a time.

The **Realm** tab shows the command tree and its selected branch. Organizational labels grow with the number of tribes under a ruler: Tribe (one), Clan (two), Federation (three/four), Empire (five or more). These labels describe scale; they do not grant extra permissions or merge inventories.

## Two distinct diplomatic relationships

**Equal alliance:** the receiving independent tribe's leader must accept. Both rulers remain independent, gain bilateral player-hit protection and cannot issue orders to or manage each other's tribes. Treaties are bilateral: A allied with B and B allied with C does not automatically make A and C allies. A sovereign leader can end their own alliance.

**Subordination:** the receiving leader must explicitly accept an invitation to become subjects. Their name, members and existing subject branches remain intact. Their entire branch enters the new ruler's hierarchy and inherits its rules and alliances; their previous independent alliances end. The invitation UI states these consequences before acceptance. Private inventory and asset ownership stay with their owners.

The sovereign can move branches beneath other subjects and release a whole branch as independent. Cycles and structures deeper than eight levels are rejected. Subject leaders can declare independence only if the sovereign permits it. Disbanding cannot bypass that rule, and a tribe must archive its issued orders before disbanding.

## Rules and command

The **Rules** tab exposes the sovereign's inherited policies:

- Friendly fire within the realm. Off by default; equal allies remain protected regardless of this setting.
- Base, industrial and ship construction restricted to officers/leaders.
- Weapon use restricted to officers/leaders.
- Subject leaders permitted to secede.
- Plain-text standing orders (280 characters).

These are authoritative policies, not just menu labels. Industrial and ship commands enforce construction restrictions; trusted base construction transactions do too. The base preview and construction tools report the restriction. Personal weapons and ship missile launch check military policy before spending ammunition.

**Orders** supports Rally, Defend, Gather, Build, Patrol and Attack directives. Choose a recipient in your own command branch, optionally include all its subjects, and attach an objective at your current planetary position. Allies cannot be ordered as subjects. Attack directives cannot target your own realm or an ally. Orders persist while leaders disconnect; recipients can acknowledge and complete their own tribe's receipt. Their chain of command can cancel or archive resolved orders. Jurisdiction changes revoke affected directives/receipts instead of preserving another ruler's authority. Track an order to show its objective and distance in the survival HUD.

Orders are player task directives. This change does not resurrect RTS units, create civilian factions, or automatically assign the old GPU army or autonomous mining vehicles to political orders.

## Testing alone

Enable Creative in F4 crafting, then use **CREATE INDEPENDENT TEST TRIBE** in the empire menu. Test leaders are clearly marked data records, with no NPC, fleet or per-unit objects. Send an alliance or subordination invitation, then explicitly choose **CREATIVE: ACCEPT AS TEST LEADER**. This proxy action is limited to marked test leaders and cannot force a real player's tribe to accept.

The host may create at most 16 test leaders. Other visible budgets are 256 tribes, 64 members per tribe, eight hierarchy levels, 256 pending invitations and 256 retained orders. Active orders are never silently removed to meet these budgets; archive resolved orders explicitly. History retains the latest 256 events.

## Authority, combat and persistence

Government records, invites, memberships, roles, treaties, orders, receipts, policies and recent history live in the existing IndustrialWorld snapshot and idempotent transaction journal. Old snapshots initialize empty governance. The existing locked command gateway supplies actor identity; command payloads cannot impersonate another leader.

`IndustrialAuthority.ApplyPlayerCombatDamage` is a trusted server hit-resolution entry point. It checks authorization, non-aggression, distance and damage-event sequence before journaling a player hit; there is no raw client command for arbitrary damage. Authoritative damage is synchronized into local survival health without undoing ordinary healing.

This provides the survival player-governance and hit-validation foundation. It does not add a multiplayer transport or map diplomacy onto legacy GPU army factions. A future multiplayer/weapon adapter must resolve actual hits and call the trusted authority entry point; live multi-client combat has not been demonstrated by this change. No army state is mirrored on the CPU and no GPU combat shader is changed.

## Validation

- Full runtime C# compile passes with existing project warnings.
- `dotnet run --project Tools/IndustrialChecks/GovernmentChecks.csproj`: 119 governance checks pass, including consent, ranks, equal alliances, subjects, inherited policy, non-aggression, hierarchy depth/cycles, task receipts, replay protection and journal/snapshot recovery.
- Existing industrial regression suite: 280 checks pass (including its localhost concurrency fixture).
- Shipbuilding regression suite: 25 checks pass.
- An isolated UI Toolkit button-callback smoke test is available at **Synthetic Front → Diagnostics → Validate tribe and empire UI**. It has not been executed in the current Editor session because that session has not imported the new scripts. No test request remains queued.

Reports are in `TestResults/Planetary/Government`. Rendered UI layout, live multiplayer and equivalent standalone performance remain unverified. The existing army/fog acceptance targets are unchanged; this change makes no 60 FPS, 5% regression or 3 ms fog claim.
