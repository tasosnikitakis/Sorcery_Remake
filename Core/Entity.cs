// ============================================================================
// ENTITY COMPONENT SYSTEM - BASE ENTITY
// Sorcery+ Remake - Core ECS Implementation
// ============================================================================
// This is the foundation entity class for the ECS architecture.
// All game objects (player, enemies, items) inherit from this.
// ============================================================================

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace SorceryRemake.Core
{
    /// <summary>
    /// Base entity class for all game objects.
    /// Entities are containers for components and have basic transform data.
    /// </summary>
    public class Entity
    {
        // ====================================================================
        // CORE IDENTITY
        // ====================================================================

        public Guid Id { get; private set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }

        // ====================================================================
        // TRANSFORM DATA
        // Position is stored in "Amstrad pixels" (160x200 coordinate space)
        // ====================================================================

        public Vector2 Position { get; set; }
        public Vector2 Scale { get; set; }
        public float Rotation { get; set; }

        // ====================================================================
        // COMPONENT STORAGE
        // ECS Pattern: Entities hold components, systems process them
        // ====================================================================

        private readonly Dictionary<Type, IComponent> _components;

        // ====================================================================
        // CONSTRUCTOR
        // ====================================================================

        public Entity(string name = "Entity")
        {
            Id = Guid.NewGuid();
            Name = name;
            IsActive = true;
            Position = Vector2.Zero;
            Scale = Vector2.One;
            Rotation = 0f;
            _components = new Dictionary<Type, IComponent>();
        }

        // ====================================================================
        // COMPONENT MANAGEMENT
        // ====================================================================

        /// <summary>
        /// Add a component to this entity.
        /// </summary>
        public void AddComponent<T>(T component) where T : IComponent
        {
            var type = typeof(T);
            if (_components.ContainsKey(type))
            {
                throw new InvalidOperationException(
                    $"Entity '{Name}' already has component of type {type.Name}");
            }

            component.Owner = this;
            _components[type] = component;
        }

        /// <summary>
        /// Get a component from this entity.
        /// </summary>
        public T? GetComponent<T>() where T : class, IComponent
        {
            var type = typeof(T);
            return _components.TryGetValue(type, out var component)
                ? component as T
                : null;
        }

        /// <summary>
        /// Check if this entity has a specific component.
        /// </summary>
        public bool HasComponent<T>() where T : IComponent
        {
            return _components.ContainsKey(typeof(T));
        }

        /// <summary>
        /// Remove a component from this entity.
        /// </summary>
        public void RemoveComponent<T>() where T : IComponent
        {
            _components.Remove(typeof(T));
        }

        // ====================================================================
        // LIFECYCLE
        // ====================================================================

        public virtual void Update(GameTime gameTime)
        {
            if (!IsActive) return;

            // Update all components
            foreach (var component in _components.Values)
            {
                component.Update(gameTime);
            }
        }

        public virtual void Draw(GameTime gameTime)
        {
            if (!IsActive) return;

            // Components handle their own rendering if needed
            foreach (var component in _components.Values)
            {
                component.Draw(gameTime);
            }
        }
    }
}
