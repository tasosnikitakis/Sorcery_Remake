// ============================================================================
// COMPONENT INTERFACE
// Sorcery+ Remake - ECS Component Contract
// ============================================================================
// All components must implement this interface.
// Components are pure data + behavior attached to entities.
// ============================================================================

using Microsoft.Xna.Framework;

namespace SorceryRemake.Core
{
    /// <summary>
    /// Base interface for all ECS components.
    /// Components add specific functionality to entities.
    /// </summary>
    public interface IComponent
    {
        /// <summary>
        /// Reference to the entity that owns this component.
        /// </summary>
        Entity? Owner { get; set; }

        /// <summary>
        /// Called every frame to update component logic.
        /// </summary>
        void Update(GameTime gameTime);

        /// <summary>
        /// Called every frame to render component visuals.
        /// </summary>
        void Draw(GameTime gameTime);
    }
}
