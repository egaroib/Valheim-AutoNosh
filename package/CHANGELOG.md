# Changelog

## 0.5.0
- Added a low-food warning when you're down to a few of the food just eaten
- Added NotifyNextFood to show which food will be eaten next once you run out

## 0.4.1
- Changed food kinds to match the icon's fork color, fixing white (balanced) foods

## 0.4.0
- Added ConsiderFeastDuration, an off-by-default option to favor long-lasting feasts

## 0.3.0
- Added a None profile that keeps re-eating what you already had (or the same kind)
- Added FallbackPriority to control which stat category wins when nothing else fits

## 0.2.1
- Fixed auto-eating fighting Bukeperries (and similar) by pausing while feeling sick

## 0.2.0
- Added EatThresholdPercent so you can wait longer before it replaces a food
- Added toggleable notifications for eating, food running low, and fallback picks

## 0.1.1
- Fixed "can't eat more X" spam when the only food on hand matched one already active

## 0.1.0
- Added automatic eating that tops up food slots before they run out
- Added five food profiles (stamina/health/eitr focused) with a hotkey to cycle them
- Added an AFK pause so food isn't wasted while you're away
- Food never eats potions or meads, and expiration speed is untouched
