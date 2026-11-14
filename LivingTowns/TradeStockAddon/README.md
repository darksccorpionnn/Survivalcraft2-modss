# Living Towns 0.4.60 - Profession Trade Stock Addon

This is a companion addon for the user's existing **LivingTowns 0.4.59** package.

## What it changes

- Leaves the existing LivingTowns 0.4.59 DLL, villager AI, home/bed routines, WORK-sign routing, animations, entity templates, and Silver/Gold Coin block implementations untouched.
- Seeds an empty villager trade inventory the first time its trade panel is opened.
- Uses Survivalcraft 2 block registrations by name through `BlocksManager.GetBlockIndex`; no Minecraft item IDs or guessed numeric IDs are embedded.
- Uses different stock lists for Farmer, Lumberjack, Miner, Blacksmith, Guard, Healer, and Merchant.
- Writes an initialization marker to the villager's saved ValuesDictionary so depleted inventories are not refilled every time the trade panel is opened.

## Load order

The add-on declares a dependency on `darksccorpionnn.LivingTowns` version `0.4.59`, so the base villager assembly is loaded before the Harmony patch.

## Install

Keep the existing `LivingTowns-0.4.59-TRADE-RESTORED.scmod` installed. Install the generated `LivingTowns-0.4.60-TradeStockAddon.scmod` alongside it as a second mod. Do not replace or rename the existing 0.4.59 package.

## Scope and limitation

This add-on only initializes the existing 10-slot transfer inventory with profession-specific stock. It does not yet enforce silver/gold prices or add recipe-based purchase buttons; the existing 0.4.59 currency items and trade panel are left intact. Actual inventory persistence and drag/drop still need verification in an Android world.
