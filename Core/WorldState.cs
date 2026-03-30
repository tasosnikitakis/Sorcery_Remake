// ============================================================================
// WORLD STATE
// Sorcery+ Remake - Persistent Game State
// ============================================================================
// Centralizes all persistent state that survives room transitions:
// - Dead enemies, picked-up items, saved wizards, unlocked doors
// - Carried item, saved wizard count
// - Per-room enemy snapshots
//
// This is the future serialization target for save/load.
// ============================================================================

using System.Collections.Generic;

namespace SorceryRemake.Core
{
    /// <summary>
    /// All persistent game state in one place.
    /// Survives room transitions; reset on game restart.
    /// </summary>
    public class WorldState
    {
        // Permanently killed enemies (by ID)
        public HashSet<string> DeadEnemies { get; } = new HashSet<string>();

        // Permanently picked-up items (by ID)
        public HashSet<string> PickedUpItems { get; } = new HashSet<string>();

        // Permanently saved wizards (by ID)
        public HashSet<string> SavedWizards { get; } = new HashSet<string>();

        // Permanently unlocked blocked doors (by ID)
        public HashSet<string> UnlockedDoors { get; } = new HashSet<string>();

        // Saved room enemy snapshots (for room persistence)
        public Dictionary<string, List<EnemyInstance>> SavedRoomEnemies { get; } = new Dictionary<string, List<EnemyInstance>>();

        // Player inventory
        public ItemType CarriedItem { get; set; } = ItemType.None;

        // Wizard rescue progress
        public int SavedWizardCount { get; set; } = 0;

        // Counter for generating unique IDs for dropped/spawned entities
        public int SpawnCounter { get; set; } = 0;

        /// <summary>
        /// Reset all state to initial conditions (new game / restart).
        /// </summary>
        public void Reset()
        {
            DeadEnemies.Clear();
            PickedUpItems.Clear();
            SavedWizards.Clear();
            UnlockedDoors.Clear();
            SavedRoomEnemies.Clear();
            CarriedItem = ItemType.None;
            SavedWizardCount = 0;
            SpawnCounter = 0;
        }
    }
}
