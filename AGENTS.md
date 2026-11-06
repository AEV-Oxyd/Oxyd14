The project makes use of a ECS-inspired arhitecture & sandboxing. The client/shared assemblies are under the scrutiny of the sandbox.
The sandbox dissallows any usage of reflection & implements sandbox safe API's that must be used.
Multithreading -> ParallelRobustJob, Parallel.ForEach

Arhitecture
Components which implement "active" behaviours , such as only processing when Y condition is fulfilled should be implemented under the Active Component pattern on the Entity System.
meaning that components that should be running behaviour/logic on EntSys Update() should be given a ActiveComponent , that the EntSys will query,  instead of iterating through all instances
of the component & checking wheter the condition is fulfilled.

UI State Management
Server sends UI states to Clients at the end of each tick, only sending the most recently assigned state.
Avoid networking data that can be built locally from prototypes, client & server have a system to sync their prototypes.

Prototypes
- Can be serialized/read from YML
- Commonly used as a way to configure behaviours / store data, a Research computer for example can keep a ProtoId referencing a TechTreePrototype that it reads for its unlock options / when building the UI.
- to be used in entity systems(as a variable on the EntSys or component) to implement declarable value SETS that shouldn't be duplicated for every instance & rarely/never change during active gameplay
 
Entity Systems
- should make use of the [SubscribeLocalEvent], [SubscribeNetworkEvent] attributes instead of doing subscriptions in Initialize() WHEN possible.
- systems can declare dependencies for other EntitySystems & EntityQueries, dependency fields are marked with [Dependency] and resolved during Initalize()
- state-data should be kept in Singleton entities.
- variable/method declarations should be done in the following order 1)EntitySystems, 2)Queries , 3)Constant prototypes ids , 4)Constant values, , 5)any variable ,6)Overrides of existing Methods , 7) Methods

Important Entity Systems
- DeviceLinkSystem , use for any behaviour that involves being aware of the state of another entity.
- EntityLookup , use for queries to find entities with certain components , raycasts

