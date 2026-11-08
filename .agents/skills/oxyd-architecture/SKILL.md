---
name: oxyd-architecture
description: >-
  USE FOR: All C# Coding tasks
---
- **`RobustToolbox/` must not have anything inside modified.
- **Sandboxing Scrutiny**: The project uses an ECS-inspired architecture where `Content.Client` and `Content.Shared` assemblies are under strict sandboxing enforcement (`Robust.Shared.Sandboxing`) and
 any usage of reflection (`System.Reflection`, `Type.GetMethod`, `BindingFlags`) is strictly disallowed.
- **Multithreading**: Use sandbox-safe primitives: `ParallelRobustJob`, `Parallel.ForEach`.
- **Do NOT use scripts** to perform code changes or replacements.
- **Semantic Solution Refactorings**: For renaming symbols across the solution, extracting methods/interfaces, or deleting dead code, use Rider's semantic MCP tools (`rename_refactoring`, `extract_method`, `extract_interface`, `change_api_signature`, `safe_delete`).
## 4. Architecture: Active Component Pattern
- Components implementing "active" behaviors (processing only when condition Y is met) MUST follow the **Active Component pattern**.
- Do NOT iterate through all instances of a component in `EntitySystem.Update()` to check whether a condition is fulfilled if it can be done event-based.
- Instead, give entities an active marker component (e.g., `ActiveFooComponent`) when the condition is met, and have the system query *only* the active component in `Update()`.
## 5. UI State Management
- The server sends UI states to clients at the end of each tick, transmitting only the most recently assigned state.
- **Avoid Networking Redundant Data**: Do not network data that can be reconstructed locally from prototypes. Client and server have a built-in prototype synchronization system.
## 6. Prototypes
- Prototypes are serialized and deserialized from YAML (`Resources/Prototypes/`).
- Commonly used to configure behaviors and store static data sets (e.g., a research computer referencing a `TechTreePrototype` for unlock options).
- Stored as `ProtoId<T>` on entity systems or components for immutable sets that should not be duplicated across instances and rarely or never change during active gameplay.
## 7. Entity Systems Conventions
- **Event Subscriptions**: Use `[SubscribeLocalEvent]` and `[SubscribeNetworkEvent]` attributes on handler methods instead of manual subscriptions in `Initialize()` whenever possible.
- **Dependencies**: Declare system dependencies using `[Dependency]`. Fields marked `[Dependency]` must NOT be `readonly` (to satisfy engine analyzer `RA0051`).
- **Singleton State**: State data should be stored in Singleton entities.
- **Member Declaration Order**: Strictly organize class members in the following order:
  1. `EntitySystem` dependencies
  2. `EntityQuery` dependencies
  3. Constant prototype IDs (`ProtoId<...>`)
  4. Constant values (`const ...`)
  5. Variables and fields
  6. Overrides of existing base methods (`Initialize`, `Update`, etc.)
  7. Methods
## 8. Important Entity Systems
- **`DeviceLinkSystem`**: Use for any behavior that requires tracking or reacting to the state of another linked entity.
- **`EntityLookup`**: Use for spatial queries to find entities with specific components, raycasts, or range checks.
- **`HandsSystem`**: Read its API & Use to create properly predicted interactions when they include hands (e.g., picking up, dropping, or interacting with items).
## 9. Prediction
- There are 2 prediction models in the project, one for player interactions through Tick-based rollback and a custom-implementation for preserving/stopping the overwrite of states
- Future ticks & current ticks can be discerned by IGameTiming.IsFirstTimePredicted.
- Player Interaction Prediction follows a Tick-based model where the server is authoritative and clients predict actions locally by rerunning PredictiveEvents every future tick, for example a player inserting a item into a backpack will raise MoveItem for X ticks in the future and it is expected for the StorageSystem to handle it each tick (both current & predicted)
- Not all events are predictive. Only events that are raised using `RaisePredictiveEvent` are predictive.
## 10. UI Controls
- There is an IInputRelay interface that must be implemented for any new control!
- Make use of Content implemented controls whenever possible, most have Content pre-appended to the Engine one they're replacing.
- Create new controls only when necessary and avoid duplicating existing functionality.

