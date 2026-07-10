[h1]Apparel Policy Builder[/h1]

A RimWorld mod that adds a [b]Policy Builder[/b] button to the Apparel Policy screen. Instead of hunting through a flat list of every apparel item, you write rules over the attributes apparel provides: armor, insulation, shooting accuracy, what body parts it covers, and any other stat. Apply them to toggle apparel on or off across the whole policy in one click.

Applying is just a normal edit of the vanilla policy: the mod only flips which apparel pieces are allowed, so it stays compatible with everything else that reads an apparel policy. Nothing runs on tick, and attribute data is cached once at load.

[h2]How it works[/h2]
[olist]
[*]Open [b]Apparel Policies[/b] and click [b]Policy Builder[/b].
[*]Pick an attribute from the left to add a rule about it
[*]Each rule is [b]Require[/b] or [b]Forbid[/b], and is scoped either [b]Globally[/b] or to a single apparel [b]Layer[/b] (so you can build a whole outfit layer by layer). For example:
[list]
[*][i]Forbid, Headgear, Shooting Accuracy, negative[/i] - no headgear that hurts your shooting.
[*][i]Require, Middle, Armor - Sharp, greater than 0.4[/i] - only well-armored mid-layer wear. You can even choose stuffables to evaluate in the top right.
[/list]
[*][b]Apply[/b] your rules to the policy.
[/olist]

[h2]Dependencies[/h2]
[list]
[*][url=https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077]Harmony[/url]
[*](Optional) [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3698331639]Material Filter (Harmony)[/url] - Adds a material category to the filters dropdown to let you set allowed materials through the rule engine.
[/list]

[h2]Supported versions[/h2]
[list]
[*]RimWorld 1.6
[/list]

[h2]Check Out My Other Mods![/h2]
[list]
[*][url=https://steamcommunity.com/sharedfiles/filedetails/?id=3754416811]Digital Storage[/url] - Industrial-tier shelves that store far more books than a bookcase while still granting reading bonuses.
[*][url=https://steamcommunity.com/sharedfiles/filedetails/?id=3732890624]Break Timer[/url] - See what breaks your pawns are at risk of, and find out when they'll get over it.
[*][url=https://steamcommunity.com/sharedfiles/filedetails/?id=3725970365]Pipes for Medieval Overhaul[/url] - Adds DBH water pipes to some MO and MO mod objects.
[/list]

[h3]AI Disclosure:[/h3]

This mod was partially developed with the assistance of AI tools, used by an actual programmer who understands the mod and any code it produced.

Full source available on [url=https://github.com/Skyl3lazer/Apparel-Policy-Builder]GitHub[/url]