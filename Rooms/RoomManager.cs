using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SorceryRemake.Doors;
using SorceryRemake.Tiles;
using System.Collections.Generic;

namespace SorceryRemake.Rooms
{
    public enum TransitionState
    {
        None,           // Normal gameplay
        DoorOpening,    // Door animation playing, everything frozen
        TransitionReady // Animation done, execute room switch
    }

    /// <summary>
    /// Manages rooms, doors, and transitions between rooms.
    /// </summary>
    public class RoomManager
    {
        // Current room state
        public TileMapComponent? CurrentTileMap { get; private set; }
        public List<DoorComponent> CurrentDoors { get; private set; } = new List<DoorComponent>();
        public string CurrentRoomId { get; private set; } = "";

        // Transition state
        public TransitionState State { get; private set; } = TransitionState.None;
        public bool IsGameFrozen => State != TransitionState.None;
        private DoorComponent? _activeDoor;

        // Room registry: roomId -> room builder action
        private readonly Dictionary<string, System.Action> _roomBuilders = new();

        // Shared references
        private Texture2D? _tilesetTexture;
        private Texture2D? _leftDoorTexture;
        private Texture2D? _rightDoorTexture;

        public void SetTextures(Texture2D tileset, Texture2D leftDoorSheet, Texture2D rightDoorSheet)
        {
            _tilesetTexture = tileset;
            _leftDoorTexture = leftDoorSheet;
            _rightDoorTexture = rightDoorSheet;
        }

        /// <summary>
        /// Register a room builder function by room ID.
        /// </summary>
        public void RegisterRoom(string roomId, System.Action builder)
        {
            _roomBuilders[roomId] = builder;
        }

        /// <summary>
        /// Load a room by ID and place the player at the given position.
        /// </summary>
        public void LoadRoom(string roomId, Vector2? playerSpawn = null)
        {
            if (_roomBuilders.TryGetValue(roomId, out var builder))
            {
                builder();
                CurrentRoomId = roomId;
                State = TransitionState.None;
                _activeDoor = null;
            }
        }

        /// <summary>
        /// Set the current room's tilemap (called by room builders).
        /// </summary>
        public void SetTileMap(TileMapComponent tileMap)
        {
            CurrentTileMap = tileMap;
        }

        /// <summary>
        /// Set the current room's doors (called by room builders).
        /// </summary>
        public void SetDoors(List<DoorComponent> doors)
        {
            CurrentDoors = doors;
            foreach (var door in doors)
            {
                door.Texture = door.Type == DoorType.LeftOpening
                    ? _rightDoorTexture
                    : _leftDoorTexture;
            }
        }

        /// <summary>
        /// Check if any door is triggered by the player. Call every frame during normal gameplay.
        /// </summary>
        public void CheckDoorTriggers(Vector2 playerPos, int playerWidth, int playerHeight)
        {
            if (State != TransitionState.None) return;

            foreach (var door in CurrentDoors)
            {
                if (door.IsPlayerAligned(playerPos, playerWidth, playerHeight))
                {
                    // Trigger this door
                    _activeDoor = door;
                    _activeDoor.StartOpening();
                    State = TransitionState.DoorOpening;
                    return;
                }
            }
        }

        /// <summary>
        /// Update door animation during transition. Returns target info when ready.
        /// </summary>
        public (string targetRoom, string targetDoor)? Update(float deltaTime)
        {
            if (State != TransitionState.DoorOpening || _activeDoor == null)
                return null;

            bool done = _activeDoor.Update(deltaTime);
            if (done)
            {
                State = TransitionState.TransitionReady;
                return (_activeDoor.TargetRoomId, _activeDoor.TargetDoorId);
            }
            return null;
        }

        /// <summary>
        /// Execute the room transition. Returns the player spawn position in the new room.
        /// </summary>
        public Vector2 ExecuteTransition(int playerWidth)
        {
            if (_activeDoor == null)
                return Vector2.Zero;

            string targetRoomId = _activeDoor.TargetRoomId;
            string targetDoorId = _activeDoor.TargetDoorId;

            // Load the target room
            LoadRoom(targetRoomId);

            // Find the target door and get arrival position
            foreach (var door in CurrentDoors)
            {
                if (door.DoorId == targetDoorId)
                {
                    return door.GetArrivalPosition(playerWidth);
                }
            }

            // Fallback: center of room
            return new Vector2(160, 60);
        }

        /// <summary>
        /// Draw all doors in the current room.
        /// </summary>
        public void DrawDoors(SpriteBatch spriteBatch, float scale)
        {
            foreach (var door in CurrentDoors)
            {
                door.Draw(spriteBatch, scale);
            }
        }
    }
}
