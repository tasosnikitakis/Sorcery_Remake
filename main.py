# main.py

import pygame
import os
import settings # Import the whole settings module

# Import the classes from their respective files
from spritesheet import Spritesheet
from player import Player
from platform import Platform

# --- Pygame Initialization ---
try:
    pygame.init()
except Exception as e:
    print(f"Error initializing Pygame: {e}")
    exit()

# --- Screen Setup ---
# Dimensions are now derived in settings.py from BASE dimensions and GLOBAL_SCALE_FACTOR
try:
    screen = pygame.display.set_mode((settings.SCREEN_WIDTH, settings.SCREEN_HEIGHT), pygame.DOUBLEBUF)
    pygame.display.set_caption("Sorcery Game - Recreated")
except pygame.error as e:
    print(f"Error setting up the screen: {e}")
    pygame.quit()
    exit()

clock = pygame.time.Clock()

# --- Font for Info Panel ---
info_font = None
try:
    # INFO_FONT_NAME and INFO_FONT_SIZE are now scaled in settings.py
    info_font = pygame.font.Font(settings.INFO_FONT_NAME, settings.INFO_FONT_SIZE)
except Exception as e:
    print(f"Warning: Could not load font '{settings.INFO_FONT_NAME}'. Using default system font. Error: {e}")
    # SysFont size might also need to be settings.INFO_FONT_SIZE or slightly adjusted
    info_font = pygame.font.SysFont(None, settings.INFO_FONT_SIZE + int(4 * settings.GLOBAL_SCALE_FACTOR))


# --- Game Assets and Setup ---

# Animation data for the wizard
# 'w' and 'h' should be the NATIVE (unscaled) dimensions of the sprite on the spritesheet
wizard_animations_data = {
    "walk_left":  { "x": 0,   "y": 75, "w": settings.PLAYER_SPRITE_WIDTH, "h": settings.PLAYER_SPRITE_HEIGHT, "count": 4, "spacing": 1},
    "idle_front": { "x": 100, "y": 75, "w": settings.PLAYER_SPRITE_WIDTH, "h": settings.PLAYER_SPRITE_HEIGHT, "count": 4, "spacing": 1},
    "walk_right": { "x": 200, "y": 75, "w": settings.PLAYER_SPRITE_WIDTH, "h": settings.PLAYER_SPRITE_HEIGHT, "count": 4, "spacing": 1}
}
if not wizard_animations_data:
    print("Warning: wizard_animations_data is empty before Player creation.")

# Load Spritesheet
try:
    my_spritesheet = Spritesheet(settings.SPRITESHEET_FILENAME)
except SystemExit:
    print("Aborting: Failed to initialize Spritesheet in main.py.")
    pygame.quit()
    exit()
except Exception as e:
    print(f"Unexpected error loading spritesheet: {e}")
    pygame.quit()
    exit()

# --- Create Sprite Groups ---
all_sprites = pygame.sprite.Group()
platforms = pygame.sprite.Group()

# --- Create Player Instance ---
wizard = None
try:
    # --- MODIFIED START POSITION: Start player in the air at a specific tile coordinate ---
    player_start_tile_col = 5  # Start at the 6th tile column (0-indexed)
    player_start_tile_row = 5  # Start at the 6th tile row (0-indexed)

    player_start_pos_x = player_start_tile_col * settings.TILE_WIDTH
    player_start_pos_y = player_start_tile_row * settings.TILE_HEIGHT
    # --- END OF MODIFIED START POSITION ---
    
    wizard = Player(my_spritesheet, wizard_animations_data,
                    initial_animation="idle_front",
                    position=(player_start_pos_x, player_start_pos_y),
                    animation_ticks_per_frame=settings.PLAYER_ANIMATION_TICKS_PER_FRAME)
    
    if not wizard.animations:
        print("CRITICAL: Player object created, but its animations dictionary is empty.")
        raise ValueError("Player animations not loaded.")

    all_sprites.add(wizard)

except ValueError as ve:
    print(ve)
    pygame.quit()
    exit()
except Exception as e:
    print(f"Error creating Player instance: {e}")
    pygame.quit()
    exit()


# --- Create Platforms (Tile-Based Approach) ---
platform_definitions = [
    # (tile_col_start, tile_row_start, num_tiles_wide, num_tiles_high, color)
    # Ground platform - spans the entire width at the bottom-most tile row
    (0, (settings.BASE_GAME_AREA_HEIGHT // settings.BASE_TILE_HEIGHT) - 1, settings.BASE_GAME_AREA_WIDTH // settings.BASE_TILE_WIDTH, 1, settings.GREEN), 
    # Example floating platforms (adjust tile_row_start to be above the ground)
    (15, (settings.BASE_GAME_AREA_HEIGHT // settings.BASE_TILE_HEIGHT) - 4, 8, 1, settings.WHITE), # 3 tiles above ground from bottom
    (4, (settings.BASE_GAME_AREA_HEIGHT // settings.BASE_TILE_HEIGHT) - 7, 6, 1, (100, 100, 100)), # 6 tiles above ground from bottom
]

if wizard: 
    for p_def in platform_definitions:
        tile_x, tile_y, tiles_w, tiles_h, color = p_def
        
        pixel_x = tile_x * settings.TILE_WIDTH
        pixel_y = tile_y * settings.TILE_HEIGHT
        pixel_width = tiles_w * settings.TILE_WIDTH
        pixel_height = tiles_h * settings.TILE_HEIGHT
        
        platform = Platform(pixel_x, pixel_y, pixel_width, pixel_height, color)
        platforms.add(platform)
        all_sprites.add(platform)
else:
    print("Skipping platform creation as player failed to initialize.")

# Player should now start in a clear space, so nudging is not needed.

# --- Game State Variables for Info Panel ---
current_location = "in the woods" 
carrying_item = "nothing"    
energy_level = 99            

def draw_info_panel(surface):
    # Draw background for info panel
    info_panel_rect = pygame.Rect(0, settings.INFO_PANEL_Y_START, settings.SCREEN_WIDTH, settings.INFO_PANEL_HEIGHT)
    pygame.draw.rect(surface, settings.INFO_PANEL_BG_COLOR, info_panel_rect)

    # Text lines
    line1_text = f"you are {current_location},"
    line2_text = f"carrying {carrying_item}."
    line3_text = f"energy....{energy_level}%"

    texts_to_render = [line1_text, line2_text, line3_text]
    # Margins and spacing are now scaled in settings.py
    current_y = settings.INFO_PANEL_Y_START + settings.TEXT_MARGIN_Y

    for text_content in texts_to_render:
        if info_font:
            text_surface = info_font.render(text_content, True, settings.INFO_PANEL_TEXT_COLOR)
            surface.blit(text_surface, (settings.TEXT_MARGIN_X, current_y))
            current_y += text_surface.get_height() + settings.LINE_SPACING
        else: # Fallback if font failed to load
            pygame.draw.rect(surface, (255,0,0), (settings.TEXT_MARGIN_X, current_y, 200, 20)) # Draw red box as error
            current_y += 20 + settings.LINE_SPACING


# --- Game Loop ---
running = True

while running:
    dt = clock.tick(settings.FPS) / 1000.0

    # Event Handling
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            running = False
        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_ESCAPE:
                running = False
    
    # Update Game State
    if wizard is not None:
        # The Group.update() method will call wizard.update(dt, platforms)
        # because Player.update is defined to accept these arguments.
        all_sprites.update(dt, platforms)
    else:
        print("Error: wizard object is None, cannot update.")

    # Draw / Render
    # 1. Fill the entire screen (this will be the background for the game area too)
    screen.fill(settings.BLACK) # Or settings.SKY_BLUE for the game area if preferred

    # 2. Draw all game sprites (player, platforms) onto the screen
    # These sprites are positioned within the GAME_AREA_WIDTH and GAME_AREA_HEIGHT
    if wizard is not None:
        all_sprites.draw(screen)
    else:
        # Fallback rendering if player missing
        font = pygame.font.Font(None, 36) # A generic font for error message
        text_surface = font.render("Error: Player missing. Cannot draw game.", True, settings.WHITE)
        text_rect = text_surface.get_rect(center=(settings.SCREEN_WIDTH/2, settings.SCREEN_HEIGHT/2))
        screen.blit(text_surface, text_rect)
        
    # 3. Draw Info Panel on top of everything at the bottom
    draw_info_panel(screen)
        
    pygame.display.flip()

# --- Cleanup ---
pygame.quit()
print("Game exited cleanly.")