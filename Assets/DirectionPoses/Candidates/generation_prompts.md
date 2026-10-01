# DirectionPose ImageGen prompt set

Each candidate was generated in a separate built-in ImageGen edit call. The common reference order was:

1. `Assets/Sprites/idle/idle_00.png` — exact Stand_Center composition target.
2. `a5960816a02c947d725965f7fbf2666d.jpg` — real face and eye identity.
3. `c449207c84aa28b2f426a02c75d755a0.jpg` — real standing/body identity.

## Common prompt constraints

> Produce a full composite transparent 2.5D desktop-pet DirectionPose. Preserve the exact same individual cat, face geometry, blue eyes, pink nose, dark facial mask, centered white blaze, white muzzle and chest, gray-beige coat distribution, white paws, long fur, fluffy gray-brown tail, age, body mass and proportions. Rerender torso, chest, shoulders, neck and head together around a vertical axis; do not merely move the eyes. Keep the natural standing action and four paw landing positions as close as physically possible to Stand_Center. Keep a square transparent canvas, identical apparent size, center, ground line, paw-sole height, neutral lighting and photographic texture. Do not mirror, skew, warp, perspective-stretch, rotate a flat sprite, beautify, stylize, add a background, add a shadow, crop, or invent limbs.

## Candidate angle/direction variants

- `Stand_Left3Q_01`: approximately 20 degrees toward screen-left.
- `Stand_Left3Q_02`: approximately 25 degrees toward screen-left.
- `Stand_Left3Q_03`: approximately 30 degrees toward screen-left; explicitly require visible chest/shoulder/head-neck perspective change.
- `Stand_Right3Q_01`: approximately 20 degrees toward screen-right.
- `Stand_Right3Q_02`: approximately 25 degrees toward screen-right.
- `Stand_Right3Q_03`: approximately 30 degrees toward screen-right; explicitly require visible chest/shoulder/head-neck perspective change.

All prompts prohibit side views and angles greater than 30 degrees.

