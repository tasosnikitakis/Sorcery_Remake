# Development Walkthrough - Sorcery+ Remake
**Current Status**: Phase 1 Complete ✅
**Last Updated**: February 7, 2026
**Commit**: d4934aa - Phase 1 Refinement

---

## 🎯 Project Vision

Recreate the classic Amstrad CPC 6128 game "Sorcery" with:
- **Authentic gameplay** matching the original 1985 mechanics
- **Modern C#/MonoGame** implementation with clean ECS architecture
- **Level editor** (SorceryForge) for community content creation
- **75 rooms** of challenging platforming and exploration

---

## ✅ Phase 1: Accomplished (COMPLETE)

### Core Systems Built

#### 1. **Entity-Component-System (ECS) Architecture**
- ✅ Base `Entity` class with component management
- ✅ `IComponent` interface for all game components
- ✅ Component lifecycle (Update/Draw)
- ✅ Easy component addition/removal

**Files**: `Core/Entity.cs`, `Core/IComponent.cs`

#### 2. **Direct Velocity Physics (Amstrad Authentic)**
- ✅ Instant movement (no acceleration/damping)
- ✅ Direct velocity assignment matching 1985 Amstrad code
- ✅ Player speed: 200 px/s (tuned for modern displays)
- ✅ Gravity: 120 px/s (floaty wizard feel)
- ✅ Screen boundaries with collision

**Files**: `Physics/PhysicsComponent.cs`, `Physics/DirectVelocityComponent.cs`

#### 3. **Animation System**
- ✅ Frame-based sprite animation with looping
- ✅ Correct sprite coordinates (Row 4, Y=75 - player wizard)
- ✅ Animations: idle_front, walk_left, walk_right
- ✅ Idle animation cycles continuously
- ✅ Animation timing: 0.117s per frame (matches Python)

**Files**: `Graphics/SpriteComponent.cs`, `Graphics/SpriteConfig.cs`

#### 4. **Player Controller**
- ✅ Arrow key controls (instant response)
- ✅ Simplified animation logic (idle always cycles)
- ✅ Vertical movement: Up/Down keys + gravity
- ✅ Horizontal movement: Left/Right instant

**Files**: `Core/PlayerController.cs`

#### 5. **Rendering System**
- ✅ Python prototype dimensions: 320x144 @ 3x = 960x600
- ✅ Pixel-perfect rendering (PointClamp sampling)
- ✅ Transparent sprite backgrounds (color key)
- ✅ Info panel structure (168px dark blue bar at bottom)
- ✅ Top-left sprite origin for accurate positioning

**Files**: `Game1.cs`

### Technical Achievements
- ✅ MonoGame 3.8.1 integration
- ✅ .NET 8.0+ compatibility (works with .NET 10)
- ✅ Content pipeline setup (mgcb tool)
- ✅ Spritesheet loading with transparency
- ✅ Fixed timestep physics (60 FPS)

### Documentation Created
- ✅ Comprehensive setup guides (FIRST_RUN.md, QUICKSTART.md)
- ✅ SDK installation troubleshooting (FIX_DOTNET_SDK.md)
- ✅ VS Code testing guide (VSCODE_TESTING.md)
- ✅ GitHub setup instructions (GITHUB_SETUP.md)
- ✅ Project status tracking (STATUS.md)

---

## 📍 Where We Are Now

### What Works
✅ **Player moves smoothly** with arrow keys
✅ **Animations cycle correctly** (idle, walk_left, walk_right)
✅ **Physics feels authentic** to the Amstrad original
✅ **Window renders correctly** (960x600 with info panel)
✅ **Sprite transparency** works (no black boxes)
✅ **Boundaries prevent** player from leaving screen

### What's Missing (For Complete Game)
❌ **Tile-based rooms** (currently just black background)
❌ **Collision detection** (platforms, walls, obstacles)
❌ **Multiple rooms** (need 75 rooms total)
❌ **Room transitions** (doors, teleporters)
❌ **Inventory system** (one-item rule)
❌ **Enemies and AI** (spawners, movement, combat)
❌ **Items and pickups** (keys, weapons, potions)
❌ **Energy system** (meter, depletion, healing)
❌ **Audio** (music and sound effects)
❌ **UI/Menus** (title screen, pause, game over)

---

## 🚀 Recommended Path Forward: Phase 2

### Phase 2A: Tile-Based Room System (PRIORITY 1)

**Goal**: Load and render rooms from tile data

#### Tasks:
1. **Create Tile System**
   - Define tile types (solid, passthrough, decoration, deadly)
   - Create 8x8 tile renderer
   - Load tiles from spritesheet

2. **Room Data Format**
   - Design JSON format for room definition
   - Include: tile layout, spawn points, exits
   - Create 2-3 test rooms

3. **Room Loader**
   - Read room JSON files
   - Generate tile entities
   - Set player spawn position

**Estimated Time**: 1-2 sessions
**Files to Create**: `Tiles/TileComponent.cs`, `Tiles/TileConfig.cs`, `Rooms/RoomData.cs`, `Rooms/RoomLoader.cs`

---

### Phase 2B: Collision Detection (PRIORITY 2)

**Goal**: Pixel-perfect collision like the original

#### Tasks:
1. **Tile Collision**
   - Implement tile-based collision detection
   - Separate X and Y axis collision
   - Handle slopes and platforms

2. **Collision Masks**
   - Create collision rectangles for sprites
   - Implement bounding box tests
   - Add fine-grained pixel collision (if needed)

3. **Gap Squeezing**
   - Allow player to fit through tight spaces
   - Matches original Amstrad behavior

**Estimated Time**: 2-3 sessions
**Files to Create**: `Physics/CollisionComponent.cs`, `Physics/CollisionSystem.cs`

---

### Phase 2C: Multi-Room Navigation (PRIORITY 3)

**Goal**: Move between rooms seamlessly

#### Tasks:
1. **Room Transitions**
   - Define exit points (doors, edges, teleporters)
   - Load adjacent rooms on demand
   - Smooth camera transitions

2. **Camera System**
   - Follow player with optional smoothing
   - Clamp to room boundaries
   - Handle room-to-room transitions

3. **Room Connection Map**
   - Define which rooms connect to which
   - Set entry/exit positions
   - Handle one-way connections

**Estimated Time**: 2-3 sessions
**Files to Create**: `Rooms/RoomManager.cs`, `Rooms/RoomTransition.cs`, `Camera/CameraController.cs`

---

## 🎮 Phase 3: Gameplay Systems

### Phase 3A: Inventory System

#### Tasks:
1. **One-Item Rule**
   - Player can carry only ONE item at a time
   - Picking up new item drops current item
   - Visual indicator of held item

2. **Item Types**
   - Keys (match to specific doors)
   - Weapons (sword, axe, etc.)
   - Special items (potions, scrolls)

3. **Item-Entity Interaction**
   - Weapon effectiveness matrix (weapon vs enemy type)
   - Key-door pairing system
   - Item use/drop mechanics

**Files to Create**: `Inventory/InventoryComponent.cs`, `Items/ItemComponent.cs`, `Items/ItemConfig.cs`

---

### Phase 3B: Enemy AI

#### Tasks:
1. **Basic Enemy Types**
   - Create 3-4 enemy types from spritesheet
   - Simple "seek player" AI
   - Patrol patterns for some enemies

2. **Enemy Spawners**
   - Bone pile generators
   - Spawn rate configuration
   - Max enemy limits per room

3. **Combat System**
   - Health for enemies
   - Weapon damage calculation
   - Enemy death/respawn

**Files to Create**: `Enemies/EnemyComponent.cs`, `Enemies/AIComponent.cs`, `Enemies/SpawnerComponent.cs`

---

### Phase 3C: Resources & Hazards

#### Tasks:
1. **Energy System**
   - Energy meter (0-100%)
   - Gradual depletion over time
   - Death at 0% energy

2. **Healing Cauldrons**
   - Restore energy when used
   - Visual feedback

3. **Poisonous Cauldrons**
   - Identical appearance to healing cauldrons!
   - Reduce energy dramatically
   - Learning which is which is part of gameplay

4. **Timer System**
   - Crumbling book icon
   - Countdown to game over
   - Display in info panel

**Files to Create**: `Resources/EnergyComponent.cs`, `Hazards/CauldronComponent.cs`, `UI/TimerComponent.cs`

---

## 🔧 Phase 4: Polish & Content

### Phase 4A: Audio System

#### Tasks:
1. **Music Playback**
   - "The Sorcerer's Apprentice" theme
   - AY-3-8912 PSG emulation (optional authentic)
   - Or use modern .ogg/.mp3 version

2. **Sound Effects**
   - Door opening/closing
   - Item pickup
   - Enemy hit/death
   - Player damage
   - Explosion sounds

**Files to Create**: `Audio/AudioManager.cs`, `Audio/SoundConfig.cs`

---

### Phase 4B: UI & Menus

#### Tasks:
1. **Title Screen**
   - Game logo
   - Start/Load/Options menu
   - High scores

2. **In-Game UI**
   - Populate info panel with actual data
   - Energy bar visualization
   - Current item display
   - Timer display

3. **Pause Menu**
   - Resume/Restart/Quit options
   - Settings (volume, controls)

4. **Game Over Screen**
   - Show final stats
   - Retry option

**Files to Create**: `UI/MenuSystem.cs`, `UI/InfoPanelRenderer.cs`

---

### Phase 4C: Content Creation

#### Tasks:
1. **All 75 Rooms**
   - Design room layouts
   - Place enemies and items
   - Connect rooms logically

2. **Balance & Testing**
   - Tune difficulty curve
   - Test all room connections
   - Ensure game is beatable

3. **SorceryForge Editor** (Stretch Goal)
   - Visual room editor
   - Drag-and-drop tile placement
   - Enemy/item placement
   - Export to game format

---

## 📅 Suggested Development Schedule

### Week 1-2: Phase 2A (Tiles & Rooms)
- Set up tile rendering system
- Create room data format
- Load and display first test room

### Week 3-4: Phase 2B (Collision)
- Implement tile collision detection
- Test with platforms and walls
- Verify gap squeezing works

### Week 5-6: Phase 2C (Multi-Room)
- Room transitions
- Camera following
- Test connecting 3-5 rooms

### Week 7-8: Phase 3A (Inventory)
- One-item system
- Item pickup/drop
- Visual indicators

### Week 9-10: Phase 3B (Enemies)
- Basic enemy movement
- AI behavior
- Combat mechanics

### Week 11-12: Phase 3C (Resources)
- Energy system
- Cauldrons
- Timer

### Week 13-16: Phase 4 (Polish)
- Audio integration
- UI completion
- Full 75-room content
- Testing and balance

**Total Estimated Time**: 16 weeks (4 months)

---

## 🎯 Immediate Next Steps (This Week)

### Step 1: Create Tile System
**Goal**: Replace black background with actual tiles

1. Extract tile graphics from spritesheet
2. Define tile types in `TileConfig.cs`
3. Create `TileComponent.cs` for rendering
4. Render a simple test room (floor + walls)

### Step 2: Define Room Format
**Goal**: Design how rooms are stored

1. Create `RoomData.cs` class
2. Design JSON structure for room definition
3. Create 1 test room in JSON
4. Verify room loads and displays

### Step 3: Test Collision
**Goal**: Player can stand on platforms

1. Mark certain tiles as "solid"
2. Implement basic tile collision in `PhysicsComponent`
3. Test player standing on floor
4. Test player blocked by walls

**Expected Outcome**: Player can walk on a tiled floor and hit walls

---

## 🔑 Key Design Decisions

### Maintain Authenticity
- ✅ Keep direct velocity physics (no modern momentum)
- ✅ 320x144 base resolution @ 3x scale
- ✅ One-item inventory rule
- ✅ Cauldron surprise (identical appearance)

### Modern Improvements (Optional)
- 🤔 Save system (original had none)
- 🤔 Checkpoint system
- 🤔 Difficulty settings
- 🤔 Extended content (more than 75 rooms?)

### Technical Standards
- ✅ Keep ECS architecture
- ✅ Maintain component-based design
- ✅ Continue extensive code comments
- ✅ Update documentation as we go

---

## 📚 Resources & References

### Original Game Analysis
- **Spritesheet**: `assets/images/Amstrad CPC - Sorcery - Characters.png`
- **Python Prototype**: `main.py`, `player.py`, `settings.py`
- **Design Document**: Review original Amstrad game mechanics

### Code Structure
- **ECS Pattern**: `Core/Entity.cs`, `Core/IComponent.cs`
- **Physics**: `Physics/PhysicsComponent.cs`
- **Graphics**: `Graphics/SpriteComponent.cs`, `Graphics/SpriteConfig.cs`

### External Resources
- **MonoGame Docs**: https://docs.monogame.net/
- **Tile-based Games**: Research tile collision techniques
- **Original Amstrad**: Study CPC 6128 technical limitations

---

## ✨ Success Criteria

### Phase 2 Complete When:
✅ 5-10 rooms load from JSON data
✅ Player can walk on tile-based platforms
✅ Player collides with walls correctly
✅ Smooth transitions between rooms
✅ Camera follows player properly

### Phase 3 Complete When:
✅ Inventory system works (one item at a time)
✅ 3+ enemy types with basic AI
✅ Combat functional (damage, death)
✅ Energy system depletes and refills
✅ Cauldrons work (healing vs poisonous)

### Phase 4 Complete When:
✅ Music and sound effects play
✅ All UI elements functional
✅ Title screen and menus work
✅ All 75 rooms designed and connected
✅ Game is fully playable start-to-finish

---

## 🚨 Known Challenges Ahead

### Technical Challenges
1. **Tile Collision**: Need to handle 8x8 tiles properly
2. **Room Transitions**: Smooth loading without lag
3. **Enemy AI**: Keep it simple but fun
4. **Performance**: 75 rooms worth of content

### Design Challenges
1. **Room Layout**: Making 75 interesting rooms
2. **Difficulty Curve**: Not too easy, not too hard
3. **Visual Variety**: Limited spritesheet assets
4. **Playtesting**: Need feedback on balance

### Solutions
- Start small (3-5 rooms) and iterate
- Use JSON for easy room editing
- Keep AI simple (seek player, patrol)
- Consider room editor tool for faster iteration

---

## 💡 Tips for Implementation

### Best Practices
1. **Test frequently** - After each feature, run the game
2. **Commit often** - Save progress after each working feature
3. **Keep Python reference** - Match original behavior when unsure
4. **Profile if slow** - 60 FPS is non-negotiable
5. **Comment liberally** - Explain "why" not just "what"

### When Stuck
1. Review Python prototype for reference behavior
2. Check MonoGame docs for rendering/collision techniques
3. Break problem into smaller pieces
4. Test in isolation (create test room)

---

## 🎉 Celebration Points

Mark these milestones as you reach them:

- [ ] First tile renders correctly
- [ ] Player stands on first platform
- [ ] First room loads from JSON
- [ ] First room transition works
- [ ] First enemy spawns and moves
- [ ] First item picked up
- [ ] First 5 rooms connected
- [ ] First 10 rooms playable
- [ ] Combat system working
- [ ] Energy system functional
- [ ] Music playing
- [ ] All 75 rooms complete
- [ ] First full playthrough!

---

## 📞 Current Status Summary

**What We Have**: Solid Phase 1 foundation
**What We Need**: Everything else (but we have a clear plan!)
**Next Focus**: Phase 2A - Tile-based rooms
**Estimated Progress**: ~15% complete (foundation done)

**The exciting part begins now** - building actual gameplay on top of our solid foundation! 🎮✨

---

*Generated: February 7, 2026*
*Commit: d4934aa*
*Next Update: After Phase 2A completion*
