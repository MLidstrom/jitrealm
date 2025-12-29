using JitRealm.Mud;

var baseDir = AppContext.BaseDirectory;
var worldDir = Path.Combine(baseDir, "World");

var state = new WorldState
{
    Objects = new ObjectManager(worldDir, new SystemClock()),
    Player = new Player("you")
};

var startRoom = await state.Objects.LoadAsync<IRoom>("Rooms/start.cs", state);
state.Player.LocationId = startRoom.Id;

var loop = new CommandLoop(state);
await loop.RunAsync();
