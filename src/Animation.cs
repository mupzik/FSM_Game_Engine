using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameProj.src
{
    // ========================================================================
    // АНИМИРОВАННЫЙ СПРАЙТ
    // ========================================================================
    public class AnimatedSprite
    {
        public string SpriteSheetPath { get; private set; }
        public int FrameCount { get; private set; }
        public int FrameWidth { get; private set; }
        public int FrameHeight { get; private set; }
        public double FrameDuration { get; private set; }

        public AnimatedSprite(string spriteSheetPath, int frameCount, int frameWidth, int frameHeight, double frameDuration)
        {
            if (string.IsNullOrEmpty(spriteSheetPath))
                throw new ArgumentNullException(nameof(spriteSheetPath));
            SpriteSheetPath = spriteSheetPath;
            FrameCount = frameCount;
            FrameWidth = frameWidth;
            FrameHeight = frameHeight;
            FrameDuration = frameDuration;
        }

        public FrameInfo GetFrameInfo(int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= FrameCount) frameIndex = 0;
            return new FrameInfo
            {
                SpriteSheetPath = SpriteSheetPath,
                X = frameIndex * FrameWidth,
                Y = 0,
                Width = FrameWidth,
                Height = FrameHeight
            };
        }
    }

    public class FrameInfo
    {
        public string SpriteSheetPath { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }





}