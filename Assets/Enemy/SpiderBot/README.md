# Prototype Spider Bot

This folder owns the complete six-legged hybrid-physics test enemy.

- `Definitions/PrototypeSpiderBot.asset` contains health, movement, boid, leg, attack, and presentation tuning.
- `Prefabs/PrototypeSpiderBot.prefab` is reusable and may be placed multiple times. Nearby copies automatically apply separation, cohesion, and alignment steering.
- `Scripts/ProceduralSpiderLegs.cs` builds six physical three-body leg chains. Powered ConfigurableJoints balance and propel the chassis through colliding, high-grip feet; death simply releases those motors into a ragdoll.
- `Scripts/SpiderBotController.cs` supplies gait intent and boid steering without directly sliding or hovering the chassis. It provides a leg stab, physical leap, and burn-cone attack.
- `Scripts/SpiderBotHealth.cs` receives melee and projectile weapon damage.

The body uses a tall industrial utility-box silhouette with side power cells, a front visor, hip housings, and antennae. Entering Play Mode also rebuilds this appearance at runtime, so older prefab serialization cannot leave the obsolete horizontal shell visible.

Regenerate the prototype through `Tools > Enemies > Build and Place Prototype Spider Bot`.

The definition/controller separation is intentional groundwork for a later general enemy creator.
