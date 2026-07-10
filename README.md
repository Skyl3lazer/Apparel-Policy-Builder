# Apparel Attribute Filter

A RimWorld mod that adds an **Attribute Filter** button to the Apparel Policy screen. Instead of hunting through a flat list of every apparel item, you write rules over the attributes apparel provides: armor, insulation, shooting accuracy, what body parts it covers, and any other stat. Apply them to toggle apparel on or off across the whole policy in one click.

Applying is just a normal edit of the vanilla policy: the mod only flips which apparel pieces are allowed, so it stays compatible with everything else that reads an apparel policy. Nothing runs on tick, and attribute data is cached once at load.

## How it works

1) Open **Apparel Policies** and click **Attribute Filter**.
2) Pick an attribute from the left to add a rule about it
3) Each rule is **Require** or **Forbid**, and is scoped either **Globally** or to a single apparel **Layer** (so you can build a whole outfit layer by layer). For example:

- *Forbid, Headgear, Shooting Accuracy, negative* - no headgear that hurts your shooting.
- *Require, Middle, Armor - Sharp, greater than 0.4* - only well-armored mid-layer wear. You can even choose stuffables to evaluate in the top right.

4) **Apply** your rules to the policy.

## Dependencies

- [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)

## Supported versions

- RimWorld 1.6

## Check Out My Other Mods!

[Digital Storage](https://steamcommunity.com/sharedfiles/filedetails/?id=3754416811) - Industrial-tier shelves that store far more books than a bookcase while still granting reading bonuses.

[Break Timer](https://steamcommunity.com/sharedfiles/filedetails/?id=3732890624) - See what breaks your pawns are at risk of, and find out when they'll get over it.

[Pipes for Medieval Overhaul](https://steamcommunity.com/sharedfiles/filedetails/?id=3725970365) - Adds DBH water pipes to some MO and MO mod objects.

### AI Disclosure:

This mod was partially developed with the assistance of AI tools, used by an actual programmer who understands the mod and any code it produced.