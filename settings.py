# settings.py

import os
import pygame # Needed for things like FPS constant in calculations if desired, and color constants

# --- Base Authentic Amstrad CPC "Sorcery" Dimensions ---
# Derived from the analysis of the original game (mondevlog-1.pdf)
BASE_GAME_AREA_WIDTH = 320
BASE_GAME_AREA_HEIGHT = 144
BASE_INFO_PANEL_HEIGHT = 56  # Original Amstrad screen height (200) - game area height (144)
BASE_SCREEN_WIDTH = BASE_GAME_AREA_WIDTH # Game width defines overall width
BASE_SCREEN_HEIGHT = BASE_GAME_AREA_HEIGHT + BASE_INFO_PANEL_HEIGHT # Should be 200 (320x200 overall)

# --- Global Upscaling Factor ---
# This scales the entire base game (graphics, layout) to the final window size.
# A factor of 3 makes the player 72x72 pixels (24*3) and tiles 24x24 pixels (8*3).
# The base 320x200 screen becomes a 960x600 Pygame window.
GLOBAL_SCALE_FACTOR = 3

# --- Final Pygame Window Dimensions (Derived) ---
SCREEN_WIDTH = BASE_SCREEN_WIDTH * GLOBAL_SCALE_FACTOR       # 320 * 3 = 960
SCREEN_HEIGHT = BASE_SCREEN_HEIGHT * GLOBAL_SCALE_FACTOR     # 200 * 3 = 600
FPS = 60 # Frames per second

# --- Game Layout Dimensions (Derived from base and scale factor) ---
GAME_AREA_WIDTH = BASE_GAME_AREA_WIDTH * GLOBAL_SCALE_FACTOR    # 320 * 3 = 960
GAME_AREA_HEIGHT = BASE_GAME_AREA_HEIGHT * GLOBAL_SCALE_FACTOR  # 144 * 3 = 432
INFO_PANEL_HEIGHT = BASE_INFO_PANEL_HEIGHT * GLOBAL_SCALE_FACTOR# 56 * 3 = 168
INFO_PANEL_Y_START = GAME_AREA_HEIGHT  # Info panel starts immediately below the game area (at y=432)

# --- Tile Dimensions (Derived) ---
# Original Sorcery used 40x18 tiles in its 320x144 game area.
BASE_TILE_WIDTH = BASE_GAME_AREA_WIDTH // 40  # Should be 8 (320 / 40)
BASE_TILE_HEIGHT = BASE_GAME_AREA_HEIGHT // 18 # Should be 8 (144 / 18)
# Scaled tile size for rendering in the Pygame window
TILE_WIDTH = BASE_TILE_WIDTH * GLOBAL_SCALE_FACTOR      # 8 * 3 = 24
TILE_HEIGHT = BASE_TILE_HEIGHT * GLOBAL_SCALE_FACTOR    # 8 * 3 = 24


# Colors (RGB)
BLACK = (0, 0, 0)
WHITE = (255, 255, 255)
SKY_BLUE = (135, 206, 235) # For the game area background
GREEN = (0, 200, 0)
INFO_PANEL_BG_COLOR = (0, 0, 139) 
INFO_PANEL_TEXT_COLOR = (255, 255, 0) 


#Asset paths
# your_project_root/
# |-- assets/
#     |-- images/
#         |-- Amstrad CPC - Sorcery - Characters.png
SPRITESHEET_BASENAME = "Amstrad CPC - Sorcery - Characters.png"
SPRITESHEET_FILENAME = os.path.join("assets", "images", SPRITESHEET_BASENAME)

# Player Settings
PLAYER_SPRITE_WIDTH = 24
PLAYER_SPRITE_HEIGHT = 24
# PLAYER_SCALE_FACTOR = 3
PLAYER_ANIMATION_TICKS_PER_FRAME = 7
PLAYER_ANIMATION_VELOCITY_THRESHOLD = 0.1 * GLOBAL_SCALE_FACTOR # Example: 0.3 pixels/frame at scale 3


# Player movement speeds
# Define base speeds in a way that's easy to understand (e.g., conceptual pixels per game tick if tied to FPS)
# Or directly as pixels per second. Let's use the pixels-per-second approach directly here
# based on the previous values (6 pixels/frame * 60 FPS = 360 pps)
PLAYER_SPEED_PPS = 500  # Pixels per second for horizontal and UP/DOWN key movement
PLAYER_GRAVITY_PPS = 300 # Pixels per second for gravity pull when idle or not moving up


# --- Font Settings for Info Panel ---
# Base font size can be small (e.g., 8 or 10 for pixel look), then scaled.
BASE_INFO_FONT_SIZE = 10 # A base size suitable for the original 200px high screen
INFO_FONT_SIZE = BASE_INFO_FONT_SIZE * GLOBAL_SCALE_FACTOR # Results in 30
INFO_FONT_NAME = None # Use default system font, or specify a .ttf file path (e.g., "assets/fonts/your_pixel_font.ttf")
LINE_SPACING = 2 * GLOBAL_SCALE_FACTOR # Pixels between lines of text (results in 6)
TEXT_MARGIN_X = 5 * GLOBAL_SCALE_FACTOR # Left/right margin for text (results in 15)
TEXT_MARGIN_Y = 5 * GLOBAL_SCALE_FACTOR # Top/bottom margin for text within the info panel (results in 15)