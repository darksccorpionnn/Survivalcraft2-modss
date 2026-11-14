# Living Towns 0.4.61 - Profession Currency Shop

Companion add-on for the user's existing **LivingTowns 0.4.59** package.

## What it changes

- Keeps the existing LivingTowns 0.4.59 DLL and its villager AI, home/bed routines, WORK-sign routing, animations, entity templates, and Silver/Gold Coin blocks untouched.
- Replaces the old free-transfer trade panel at the villager interaction callback with a read-only shop panel.
- Lists the items already held in the villager's separate persistent 10-slot trade stock.
- Uses profession-specific product prices, denominated in silver coins.
- Accepts Silver Coin and Gold Coin; one gold coin is worth ten silver coins.
- Removes the purchased item from the villager's stock and deducts payment from the player's inventory.
- Gives silver change when gold coins overpay, subject to available inventory space.
- Keeps the profession-specific stock seeding from the 0.4.60 add-on for empty inventories.

## Scope

The add-on changes the trade interaction and purchase transaction only. It does not patch the villager's daily AI routines. The player-to-villager sell feature is not included in this first shop version.

## Install

Keep the existing `LivingTowns-0.4.59-TRADE-RESTORED.scmod` installed and use the new combined `LivingTowns-0.4.61-ProfessionCurrencyShop.scmod` package, which includes the unchanged base DLL/assets plus this updated add-on DLL. Do not install the old 0.4.60 transfer-only add-on at the same time.

## Verification

GitHub Actions compiles the add-on against Survivalcraft API 1.9.5.1. The final package must still be tested in the user's Android Survivalcraft 2.4.0.0 world to verify the UI interception, coin payment, change, and persistent stock behavior.
