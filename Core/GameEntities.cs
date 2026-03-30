// ============================================================================
// GAME ENTITIES
// Sorcery+ Remake - Runtime Entity Types
// ============================================================================
// Inner classes extracted from Game1.cs:
// - EnemyInstance: spawned enemy with death state
// - ItemInstance: placed item in a room
// - CaptiveWizard: rescuable wizard with animation
// - BlockedDoorInstance: locked door requiring a key item
// - Projectile: shooting star projectile
// ============================================================================

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SorceryRemake.Graphics;
using SorceryRemake.Physics;

namespace SorceryRemake.Core
{
    /// <summary>
    /// Enemy type identifier — replaces string-based Id.Contains() checks.
    /// </summary>
    public enum EnemyType
    {
        Guard,
        Mask,
        Boar,
        Eye,
        Wraith
    }

    /// <summary>
    /// Tracks a spawned enemy with its ID, type, and death state.
    /// </summary>
    public class EnemyInstance
    {
        public string Id;
        public EnemyType Type;
        public Entity Entity;
        public bool IsDying;

        public EnemyInstance(string id, EnemyType type, Entity entity)
        {
            Id = id;
            Type = type;
            Entity = entity;
            IsDying = false;
        }
    }

    /// <summary>
    /// Tracks a placed item in the room.
    /// </summary>
    public class ItemInstance
    {
        public string Id;
        public ItemType Type;
        public Vector2 Position;
        public Texture2D Texture;
        public Rectangle SourceRect;

        public ItemInstance(string id, ItemType type, Vector2 pos, Texture2D tex, Rectangle src)
        {
            Id = id;
            Type = type;
            Position = pos;
            Texture = tex;
            SourceRect = src;
        }
    }

    /// <summary>
    /// A captive wizard that can be rescued by the player.
    /// Animates idle, then transforms to a star and flies upward when saved.
    /// </summary>
    public class CaptiveWizard
    {
        public string Id;
        public Vector2 Position;
        public Texture2D Texture;
        public Rectangle[] AnimFrames;
        public float FrameTime;
        public int CurrentFrame;
        public float FrameTimer;
        public bool IsSaving;
        public bool CountedAsSaved;

        public CaptiveWizard(string id, Vector2 pos, Texture2D tex, Rectangle[] frames, float frameTime)
        {
            Id = id;
            Position = pos;
            Texture = tex;
            AnimFrames = frames;
            FrameTime = frameTime;
            CurrentFrame = 0;
            FrameTimer = 0;
            IsSaving = false;
            CountedAsSaved = false;
        }

        public Rectangle CurrentSourceRect => AnimFrames[CurrentFrame];

        public void UpdateAnimation(float dt)
        {
            FrameTimer += dt;
            if (FrameTimer >= FrameTime)
            {
                FrameTimer -= FrameTime;
                CurrentFrame++;
                if (CurrentFrame >= AnimFrames.Length)
                    CurrentFrame = 0;
            }
        }
    }

    /// <summary>
    /// A locked door that requires a specific item to open.
    /// </summary>
    public class BlockedDoorInstance
    {
        public string Id;
        public Vector2 Position;
        public Texture2D Texture;
        public Rectangle SourceRect;
        public ItemType RequiredItem;

        public BlockedDoorInstance(string id, Vector2 pos, Texture2D tex, Rectangle src, ItemType required)
        {
            Id = id;
            Position = pos;
            Texture = tex;
            SourceRect = src;
            RequiredItem = required;
        }

        public Rectangle GetHitbox()
        {
            return new Rectangle(
                (int)Position.X + SpriteConfig.BLOCKED_DOOR_HITBOX_OFFSET_X,
                (int)Position.Y,
                SpriteConfig.BLOCKED_DOOR_HITBOX_WIDTH,
                SpriteConfig.BLOCKED_DOOR_HITBOX_HEIGHT);
        }
    }

    /// <summary>
    /// A shooting star projectile — a single pixel moving in one direction.
    /// </summary>
    public class Projectile
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public Color Color;

        public Projectile(Vector2 pos, Vector2 vel, Color color)
        {
            Position = pos;
            Velocity = vel;
            Color = color;
        }
    }
}
