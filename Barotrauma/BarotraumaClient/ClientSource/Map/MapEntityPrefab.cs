﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    abstract partial class MapEntityPrefab : IPrefab, IDisposable
    {
        public readonly Dictionary<string, List<DecorativeSprite>> UpgradeOverrideSprites = new Dictionary<string, List<DecorativeSprite>>();
        
        public virtual void UpdatePlacing(Camera cam)
        {
            if (PlayerInput.SecondaryMouseButtonClicked())
            {
                selected = null;
                return;
            }
            
            Vector2 placeSize = Submarine.GridSize;

            if (placePosition == Vector2.Zero)
            {
                Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);
                
                if (PlayerInput.PrimaryMouseButtonHeld()) placePosition = position;
            }
            else
            {
                Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

                if (ResizeHorizontal) placeSize.X = position.X - placePosition.X;
                if (ResizeVertical) placeSize.Y = placePosition.Y - position.Y;
                
                Rectangle newRect = Submarine.AbsRect(placePosition, placeSize);
                newRect.Width = (int)Math.Max(newRect.Width, Submarine.GridSize.X);
                newRect.Height = (int)Math.Max(newRect.Height, Submarine.GridSize.Y);

                if (Submarine.MainSub != null)
                {
                    newRect.Location -= MathUtils.ToPoint(Submarine.MainSub.Position);
                }

                if (PlayerInput.PrimaryMouseButtonReleased())
                {
                    CreateInstance(newRect);
                    placePosition = Vector2.Zero;
                    selected = null;
                }

                newRect.Y = -newRect.Y;
            }
        }

        public virtual void DrawPlacing(SpriteBatch spriteBatch, Camera cam)
        {
            if (placePosition == Vector2.Zero)
            {
                Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

                GUI.DrawLine(spriteBatch, new Vector2(position.X - GameMain.GraphicsWidth, -position.Y), new Vector2(position.X + GameMain.GraphicsWidth, -position.Y), Color.White, 0, (int)(2.0f / cam.Zoom));
                GUI.DrawLine(spriteBatch, new Vector2(position.X, -(position.Y - GameMain.GraphicsHeight)), new Vector2(position.X, -(position.Y + GameMain.GraphicsHeight)), Color.White, 0, (int)(2.0f / cam.Zoom));
            }
            else
            {
                Vector2 placeSize = Submarine.GridSize;
                Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

                if (ResizeHorizontal) placeSize.X = position.X - placePosition.X;
                if (ResizeVertical) placeSize.Y = placePosition.Y - position.Y;

                Rectangle newRect = Submarine.AbsRect(placePosition, placeSize);
                newRect.Width = (int)Math.Max(newRect.Width, Submarine.GridSize.X);
                newRect.Height = (int)Math.Max(newRect.Height, Submarine.GridSize.Y);

                if (Submarine.MainSub != null)
                {
                    newRect.Location -= Submarine.MainSub.Position.ToPoint();
                }

                newRect.Y = -newRect.Y;
                GUI.DrawRectangle(spriteBatch, newRect, Color.DarkBlue);
            }
        }

        public virtual void DrawPlacing(SpriteBatch spriteBatch, Rectangle drawRect, float scale = 1.0f, SpriteEffects spriteEffects = SpriteEffects.None)
        {
            if (Submarine.MainSub != null)
            {
                drawRect.Location -= Submarine.MainSub.Position.ToPoint();
            }
            drawRect.Y = -drawRect.Y;
            GUI.DrawRectangle(spriteBatch, drawRect, Color.White);
        }
        public void DrawListLine(SpriteBatch spriteBatch, Vector2 pos, Color color)
        {
            GUI.Font.DrawString(spriteBatch, originalName, pos, color);
        }
    }
}
