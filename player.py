# player.py

import pygame
import settings # Import the whole settings module to access its constants

class Player(pygame.sprite.Sprite):
    def __init__(self, spritesheet_obj, animation_frames_data, initial_animation, position=(100,100),
                 animation_ticks_per_frame=settings.PLAYER_ANIMATION_TICKS_PER_FRAME):
        super().__init__()
        self.spritesheet = spritesheet_obj
        self.animations = {}
        # self.scale_factor = settings.PLAYER_SCALE_FACTOR # Old: Replaced by direct use of GLOBAL_SCALE_FACTOR

        # Native sprite dimensions (these are unscaled)
        self.native_sprite_width = settings.PLAYER_SPRITE_WIDTH
        self.native_sprite_height = settings.PLAYER_SPRITE_HEIGHT

        # Scaled sprite dimensions
        self.scaled_sprite_width = self.native_sprite_width * settings.GLOBAL_SCALE_FACTOR
        self.scaled_sprite_height = self.native_sprite_height * settings.GLOBAL_SCALE_FACTOR

        self.load_animations(animation_frames_data)

        self.current_animation_name = None
        self.current_frames = []
        self.current_frame_index = 0
        
        self.animation_ticks_per_frame = animation_ticks_per_frame
        self.ticks_since_last_frame_change = 0

        self.position = pygame.math.Vector2(position) # Float-based position
        self.velocity = pygame.math.Vector2(0, 0)
        
        self.speed_pps = settings.PLAYER_SPEED_PPS
        self.gravity_pps = settings.PLAYER_GRAVITY_PPS

        self.is_on_ground = False # Will be set by collision logic

        self.image = None 
        self.rect = None 
        if initial_animation and self.animations.get(initial_animation):
            self.set_animation(initial_animation) 
        else:
            print(f"Warning: Initial animation '{initial_animation}' failed for Player.")
            # Create a fallback image using scaled dimensions
            self.image = pygame.Surface([self.scaled_sprite_width, self.scaled_sprite_height], pygame.SRCALPHA)
            self.image.fill((255,0,0,128)) # Red semi-transparent
            self.rect = self.image.get_rect(topleft=position)
            if not self.animations: print("CRITICAL: Player animations empty after init fallback.")

    def load_animations(self, animation_frames_data):
        if not animation_frames_data: 
            print("CRITICAL: animation_frames_data empty in Player.load_animations.")
            return
        for name, data in animation_frames_data.items():
            # data["w"] and data["h"] should be the native (unscaled) dimensions from spritesheet
            # e.g., 24x24 for the player
            native_w = data.get("w", self.native_sprite_width) # Use data if provided, else default
            native_h = data.get("h", self.native_sprite_height)

            frames = self.spritesheet.get_animation_frames(
                data["x"], data["y"], 
                native_w, native_h, # Pass native dimensions
                data["count"], 
                data.get("spacing", 0), 
                scale=settings.GLOBAL_SCALE_FACTOR # Apply global scaling
            )
            if not frames: 
                print(f"Warning: No frames loaded for '{name}' in Player.load_animations.")
            self.animations[name] = frames
        if not self.animations: 
            print("CRITICAL: Player animations still empty after loading.")

    def set_animation(self, animation_name):
        if self.current_animation_name == animation_name and self.current_frames: 
            return
        if not self.animations: 
            print(f"CRITICAL: No animations loaded when trying to set '{animation_name}'.")
            return

        target_frames = self.animations.get(animation_name)
        if not target_frames: # Animation name not found or frames list is empty
            print(f"Warning: Animation '{animation_name}' not found or empty. Attempting fallback or keeping current.")
            if self.current_frames and self.current_animation_name and self.animations.get(self.current_animation_name):
                return # Keep current valid animation
            
            for anim_key, anim_frames_list in self.animations.items():
                if anim_frames_list: 
                    print(f"Fallback: Setting to first available animation: '{anim_key}'")
                    self.current_animation_name = anim_key
                    self.current_frames = anim_frames_list
                    break 
            else: 
                print("CRITICAL: No valid animations available for fallback in set_animation.")
                self.image = pygame.Surface([self.scaled_sprite_width, self.scaled_sprite_height], pygame.SRCALPHA)
                self.image.fill((255,255,0,128)) # Yellow
                if self.rect is None: self.rect = self.image.get_rect(topleft=self.position)
                else: self.rect.size = self.image.get_size()
                self.current_frames = []
                self.current_animation_name = animation_name 
                self.current_frame_index = 0
                self.ticks_since_last_frame_change = 0
                return
        else: # Successfully found the requested animation
            self.current_animation_name = animation_name
            self.current_frames = target_frames
        
        self.current_frame_index = 0
        self.ticks_since_last_frame_change = 0
            
        if not self.current_frames:
            print(f"CRITICAL: current_frames for '{self.current_animation_name}' is unexpectedly empty after assignment.")
            self.image = pygame.Surface([self.scaled_sprite_width, self.scaled_sprite_height], pygame.SRCALPHA)
            self.image.fill((255,0,255,128)) # Magenta
            if self.rect is None: self.rect = self.image.get_rect(topleft=self.position)
            else: self.rect.size = self.image.get_size()
            return

        new_image = self.current_frames[self.current_frame_index]
        if self.rect is not None:
            current_center = self.rect.center
            self.image = new_image
            self.rect = self.image.get_rect(center=current_center)
        else: 
            self.image = new_image
            self.rect = self.image.get_rect(topleft=self.position)

    def handle_input_and_movement(self, dt):
        keys = pygame.key.get_pressed()
        
        moving_left = keys[pygame.K_LEFT]
        moving_right = keys[pygame.K_RIGHT]
        
        target_horizontal_velocity = 0
        if moving_left and not moving_right: 
            target_horizontal_velocity = -self.speed_pps
        elif moving_right and not moving_left: 
            target_horizontal_velocity = self.speed_pps
        self.velocity.x = target_horizontal_velocity

        current_vy = self.gravity_pps # Default to applying gravity
        
        if keys[pygame.K_UP]: 
            current_vy = -self.speed_pps # Fly up
        elif keys[pygame.K_DOWN]: 
            current_vy = self.speed_pps # Fly down
        
        if self.is_on_ground and not keys[pygame.K_UP] and not keys[pygame.K_DOWN]:
            self.velocity.y = 0 
        else:
            self.velocity.y = current_vy
        
        self.position.x += self.velocity.x * dt
        self.position.y += self.velocity.y * dt

    def handle_platform_collisions(self, platforms):
        if self.rect is None: 
            if self.image: 
                self.rect = self.image.get_rect()
            else: 
                # Fallback rect if image is also None (should be rare)
                self.rect = pygame.Rect(0,0, self.scaled_sprite_width, self.scaled_sprite_height)
        
        self.rect.x = round(self.position.x)
        
        hit_list_x = pygame.sprite.spritecollide(self, platforms, False)
        for platform_hit in hit_list_x:
            if self.velocity.x > 0: 
                self.rect.right = platform_hit.rect.left
            elif self.velocity.x < 0: 
                self.rect.left = platform_hit.rect.right
            self.position.x = float(self.rect.x) 
            self.velocity.x = 0 

        self.rect.y = round(self.position.y)
        previous_is_on_ground = self.is_on_ground
        self.is_on_ground = False 

        hit_list_y = pygame.sprite.spritecollide(self, platforms, False)
        for platform_hit in hit_list_y:
            if self.velocity.y > 0: 
                self.rect.bottom = platform_hit.rect.top
                self.is_on_ground = True
            elif self.velocity.y < 0: 
                self.rect.top = platform_hit.rect.bottom
            self.position.y = float(self.rect.y)
            self.velocity.y = 0 

    def apply_screen_boundaries(self):
        if self.rect is None: 
            return # Should have a rect by now

        # Using scaled dimensions directly from self.rect which should be correctly sized
        current_rect_width = self.rect.width
        current_rect_height = self.rect.height

        # Clamp X position (Horizontal bounds use SCREEN_WIDTH from settings)
        if self.position.x < 0:
            self.position.x = 0
            if self.velocity.x < 0: self.velocity.x = 0
        elif self.position.x + current_rect_width > settings.SCREEN_WIDTH: # Use scaled SCREEN_WIDTH
            self.position.x = settings.SCREEN_WIDTH - current_rect_width
            if self.velocity.x > 0: self.velocity.x = 0
        
        # Clamp Y position (Vertical bounds use GAME_AREA_HEIGHT from settings)
        if self.position.y < 0: # Top of the game area
            self.position.y = 0
            if self.velocity.y < 0: self.velocity.y = 0 
        elif self.position.y + current_rect_height > settings.GAME_AREA_HEIGHT: # Use scaled GAME_AREA_HEIGHT
            self.position.y = settings.GAME_AREA_HEIGHT - current_rect_height
            if not self.is_on_ground: 
                self.is_on_ground = True
            if self.velocity.y > 0: self.velocity.y = 0

    def update_animation(self):
        # PLAYER_ANIMATION_VELOCITY_THRESHOLD is already scaled in settings.py
        animation_velocity_threshold = settings.PLAYER_ANIMATION_VELOCITY_THRESHOLD

        vel_x_direction = 0
        if self.velocity.x > animation_velocity_threshold : vel_x_direction = 1
        elif self.velocity.x < -animation_velocity_threshold: vel_x_direction = -1
        
        vel_y_direction = 0 
        if self.velocity.y > animation_velocity_threshold and not self.is_on_ground : vel_y_direction = 1 
        elif self.velocity.y < -animation_velocity_threshold: vel_y_direction = -1 

        target_animation = None
        if vel_x_direction > 0: target_animation = "walk_right"
        elif vel_x_direction < 0: target_animation = "walk_left"
        elif vel_y_direction != 0 : target_animation = "idle_front" 
        else: target_animation = "idle_front"
        
        if target_animation and (self.current_animation_name != target_animation or not self.current_frames):
            self.set_animation(target_animation)

        if not self.current_frames:
            if self.image is None: 
                self.image = pygame.Surface([self.scaled_sprite_width, self.scaled_sprite_height], pygame.SRCALPHA)
                self.image.fill((0,0,255,100)) # Blue placeholder
                if self.rect is None: self.rect = self.image.get_rect(topleft=self.position)
            return

        self.ticks_since_last_frame_change += 1
        if self.ticks_since_last_frame_change >= self.animation_ticks_per_frame:
            self.ticks_since_last_frame_change = 0
            self.current_frame_index = (self.current_frame_index + 1) % len(self.current_frames)
            
            if self.current_frame_index < len(self.current_frames) and self.current_frames[self.current_frame_index]:
                new_image = self.current_frames[self.current_frame_index]
                if self.rect is not None:
                    old_center = self.rect.center
                    self.image = new_image
                    self.rect = self.image.get_rect(center=old_center)
                else: 
                    self.image = new_image
                    self.rect = self.image.get_rect(topleft=self.position)

    def update(self, dt, platforms):
        self.handle_input_and_movement(dt)

        if self.rect is None:
            if self.image: 
                self.rect = self.image.get_rect(topleft=(round(self.position.x), round(self.position.y)))
            else: 
                print("Warning: Player rect was None and image was None during update. Creating default rect.")
                self.rect = pygame.Rect(round(self.position.x), round(self.position.y), 
                                        self.scaled_sprite_width, self.scaled_sprite_height)

        self.handle_platform_collisions(platforms)
        self.apply_screen_boundaries() # Uses scaled GAME_AREA_HEIGHT and SCREEN_WIDTH from settings

        if self.rect is not None: 
            self.rect.topleft = (round(self.position.x), round(self.position.y))
        
        self.update_animation()