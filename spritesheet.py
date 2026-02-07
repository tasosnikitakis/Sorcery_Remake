# spritesheet.py

import pygame
import os # Needed for os.path.abspath in the error message

class Spritesheet:
    """
    Utility class for loading and parsing spritesheets.
    """
    def __init__(self, filename):
        """
        Load the spritesheet.
        Args:
            filename (str): The path to the spritesheet file.
        """
        try:
            self.sheet = pygame.image.load(filename).convert_alpha()
        except pygame.error as e:
            abs_path = os.path.abspath(filename) # Get absolute path for better error message
            print(f"Unable to load spritesheet image: {filename} (abs path: {abs_path})")
            print(f"Pygame Error: {e}")
            raise SystemExit(e)

    def get_image(self, x, y, width, height, scale=None):
        """
        Extract a single image (frame) from a specific location on the spritesheet.
        Args:
            x (int): The x-coordinate of the top-left corner of the frame.
            y (int): The y-coordinate of the top-left corner of the frame.
            width (int): The width of the frame.
            height (int): The height of the frame.
            scale (float, optional): Factor by which to scale the image. Defaults to None (no scaling).
        Returns:
            pygame.Surface: The extracted (and optionally scaled) image.
        """
        image = pygame.Surface([width, height], pygame.SRCALPHA) # Use SRCALPHA for transparency
        image.blit(self.sheet, (0, 0), (x, y, width, height))
        if scale:
            # Ensure dimensions are integers after scaling for transform.scale
            new_width = int(width * scale)
            new_height = int(height * scale)
            image = pygame.transform.scale(image, (new_width, new_height))
        return image

    def get_animation_frames(self, start_x, y, frame_width, frame_height, num_frames, spacing=0, scale=None):
        """
        Extracts a sequence of frames for an animation, assuming they are arranged horizontally.
        Args:
            start_x (int): The x-coordinate of the top-left corner of the first frame.
            y (int): The y-coordinate of the row of frames.
            frame_width (int): The width of a single animation frame.
            frame_height (int): The height of a single animation frame.
            num_frames (int): The total number of frames in this animation.
            spacing (int, optional): Horizontal space (in pixels) between frames. Defaults to 0.
            scale (float, optional): Factor by which to scale each frame. Defaults to None.
        Returns:
            list[pygame.Surface]: A list of pygame.Surface objects, each being a frame of the animation.
        """
        frames = []
        current_x = start_x
        for _ in range(num_frames):
            frames.append(self.get_image(current_x, y, frame_width, frame_height, scale))
            current_x += frame_width + spacing
        return frames