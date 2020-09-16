﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpFont;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    public class ScalableFont : IDisposable
    {
        private static List<ScalableFont> FontList = new List<ScalableFont>();
        private static Library Lib = null;
        private static object mutex = new object();

        private string filename;
        private Face face;
        private uint size;
        private int baseHeight;
        //private int lineHeight;
        private Dictionary<uint, GlyphData> texCoords;
        private List<Texture2D> textures;
        private GraphicsDevice graphicsDevice;

        private Vector2 currentDynamicAtlasCoords;
        private int currentDynamicAtlasNextY;
        uint[] currentDynamicPixelBuffer;

        public bool DynamicLoading
        {
            get;
            private set;
        }

        public bool IsCJK
        {
            get;
            private set;
        }

        public uint Size
        {
            get
            {
                return size;
            }
            set
            {
                size = value;
                if (graphicsDevice != null) RenderAtlas(graphicsDevice, charRanges, texDims, baseChar);
            }
        }

        private uint[] charRanges;
        private int texDims;
        private uint baseChar;

        private struct GlyphData
        {
            public int texIndex;
            public Vector2 drawOffset;
            public float advance;
            public Rectangle texCoords;
        }

        public ScalableFont(XElement element, GraphicsDevice gd = null)
            : this(
                element.GetAttributeString("file", ""),
                (uint)element.GetAttributeInt("size", 14),
                gd,
                element.GetAttributeBool("dynamicloading", false),
                element.GetAttributeBool("iscjk", false))
        {
        }

        public ScalableFont(string filename, uint size, GraphicsDevice gd = null, bool dynamicLoading = false, bool isCJK = false)
        {
            lock (mutex)
            {
                if (Lib == null) Lib = new Library();
                this.filename = filename;
                this.face = null;
                foreach (ScalableFont font in FontList)
                {
                    if (font.filename == filename)
                    {
                        this.face = font.face;
                        break;
                    }
                }
                if (this.face == null)
                {
                    this.face = new Face(Lib, filename);
                }
                this.size = size;
                this.textures = new List<Texture2D>();
                this.texCoords = new Dictionary<uint, GlyphData>();
                this.DynamicLoading = dynamicLoading;
                this.IsCJK = isCJK;
                this.graphicsDevice = gd;

                if (gd != null && !dynamicLoading)
                {
                    RenderAtlas(gd);
                }

                FontList.Add(this);
            }
        }

        /// <summary>
        /// Renders the font into at least one texture atlas, which is simply a collection of all glyphs in the ranges defined by charRanges.
        /// Don't call this too often or with very large sizes.
        /// </summary>
        /// <param name="gd">Graphics device, required to create textures.</param>
        /// <param name="charRanges">Character ranges between each even element with their corresponding odd element. Default is 0x20 to 0xFFFF.</param>
        /// <param name="texDims">Texture dimensions. Default is 512x512.</param>
        /// <param name="baseChar">Base character used to shift all other characters downwards when rendering. Defaults to T.</param>
        public void RenderAtlas(GraphicsDevice gd, uint[] charRanges = null, int texDims = 1024, uint baseChar = 0x54)
        {
            if (DynamicLoading) { return; }

            if (charRanges == null)
            {
                charRanges = new uint[] { 0x20, 0xFFFF };
            }
            this.charRanges = charRanges;
            this.texDims = texDims;
            this.baseChar = baseChar;

            textures.ForEach(t => t.Dispose());
            textures.Clear();
            texCoords.Clear();

            uint[] pixelBuffer = new uint[texDims * texDims];
            for (int i = 0; i < texDims * texDims; i++)
            {
                pixelBuffer[i] = 0;
            }

            CrossThread.RequestExecutionOnMainThread(() =>
            {
                textures.Add(new Texture2D(gd, texDims, texDims, false, SurfaceFormat.Color));
            });
            int texIndex = 0;

            Vector2 currentCoords = Vector2.Zero;
            int nextY = 0;

            lock (mutex)
            {
                face.SetPixelSizes(0, size);
                face.LoadGlyph(face.GetCharIndex(baseChar), LoadFlags.Default, LoadTarget.Normal);
                baseHeight = face.Glyph.Metrics.Height.ToInt32();

                for (int i = 0; i < charRanges.Length; i += 2)
                {
                    uint start = charRanges[i];
                    uint end = charRanges[i + 1];
                    for (uint j = start; j <= end; j++)
                    {
                        uint glyphIndex = face.GetCharIndex(j);
                        if (glyphIndex == 0) continue;
                        face.LoadGlyph(glyphIndex, LoadFlags.Default, LoadTarget.Normal);
                        if (face.Glyph.Metrics.Width == 0 || face.Glyph.Metrics.Height == 0)
                        {
                            if (face.Glyph.Metrics.HorizontalAdvance > 0)
                            {
                                //glyph is empty, but char still applies advance
                                GlyphData blankData = new GlyphData();
                                blankData.advance = (float)face.Glyph.Metrics.HorizontalAdvance;
                                blankData.texIndex = -1; //indicates no texture because the glyph is empty
                                texCoords.Add(j, blankData);
                            }
                            continue;
                        }
                        //stacktrace doesn't really work that well when RenderGlyph throws an exception
                        face.Glyph.RenderGlyph(RenderMode.Normal);
                        byte[] bitmap = face.Glyph.Bitmap.BufferData;
                        int glyphWidth = face.Glyph.Bitmap.Width;
                        int glyphHeight = bitmap.Length / glyphWidth;

                        //if (glyphHeight>lineHeight) lineHeight=glyphHeight;

                        if (glyphWidth > texDims - 1 || glyphHeight > texDims - 1)
                        {
                            throw new Exception(filename + ", " + size.ToString() + ", " + (char)j + "; Glyph dimensions exceed texture atlas dimensions");
                        }

                        nextY = Math.Max(nextY, glyphHeight + 2);

                        if (currentCoords.X + glyphWidth + 2 > texDims - 1)
                        {
                            currentCoords.X = 0;
                            currentCoords.Y += nextY;
                            nextY = 0;
                        }
                        if (currentCoords.Y + glyphHeight + 2 > texDims - 1)
                        {
                            currentCoords.X = 0;
                            currentCoords.Y = 0;
                            CrossThread.RequestExecutionOnMainThread(() =>
                            {
                                textures[texIndex].SetData<uint>(pixelBuffer);
                                textures.Add(new Texture2D(gd, texDims, texDims, false, SurfaceFormat.Color));
                            });
                            texIndex++;
                            for (int k = 0; k < texDims * texDims; k++)
                            {
                                pixelBuffer[k] = 0;
                            }
                        }

                        GlyphData newData = new GlyphData
                        {
                            advance = (float)face.Glyph.Metrics.HorizontalAdvance,
                            texIndex = texIndex,
                            texCoords = new Rectangle((int)currentCoords.X, (int)currentCoords.Y, glyphWidth, glyphHeight),
                            drawOffset = new Vector2(face.Glyph.BitmapLeft, baseHeight * 14 / 10 - face.Glyph.BitmapTop)
                        };
                        texCoords.Add(j, newData);

                        for (int y = 0; y < glyphHeight; y++)
                        {
                            for (int x = 0; x < glyphWidth; x++)
                            {
                                byte byteColor = bitmap[x + y * glyphWidth];
                                pixelBuffer[((int)currentCoords.X + x) + ((int)currentCoords.Y + y) * texDims] = (uint)(byteColor << 24 | 0x00ffffff);
                            }
                        }

                        currentCoords.X += glyphWidth + 2;
                    }
                    CrossThread.RequestExecutionOnMainThread(() =>
                    {
                        textures[texIndex].SetData<uint>(pixelBuffer);
                    });
                }
            }
        }

        public void DynamicRenderAtlas(GraphicsDevice gd, uint character, int texDims = 1024, uint baseChar = 0x54)
        {
            if (System.Threading.Thread.CurrentThread != GameMain.MainThread)
            {
                CrossThread.RequestExecutionOnMainThread(() =>
                {
                    DynamicRenderAtlas(gd, character, texDims, baseChar);
                });
                return;
            }

            byte[] bitmap;
            int glyphWidth; int glyphHeight;
            Fixed26Dot6 horizontalAdvance;
            Vector2 drawOffset;

            lock (mutex)
            {
                if (texCoords.ContainsKey(character)) { return; }
                if (textures.Count == 0)
                {
                    this.texDims = texDims;
                    this.baseChar = baseChar;
                    face.SetPixelSizes(0, size);
                    face.LoadGlyph(face.GetCharIndex(baseChar), LoadFlags.Default, LoadTarget.Normal);
                    baseHeight = face.Glyph.Metrics.Height.ToInt32();
                    textures.Add(new Texture2D(gd, texDims, texDims, false, SurfaceFormat.Color));
                }

                uint glyphIndex = face.GetCharIndex(character);
                if (glyphIndex == 0) { return; }

                face.SetPixelSizes(0, size);
                face.LoadGlyph(glyphIndex, LoadFlags.Default, LoadTarget.Normal);
                if (face.Glyph.Metrics.Width == 0 || face.Glyph.Metrics.Height == 0)
                {
                    if (face.Glyph.Metrics.HorizontalAdvance > 0)
                    {
                        //glyph is empty, but char still applies advance
                        GlyphData blankData = new GlyphData();
                        blankData.advance = (float)face.Glyph.Metrics.HorizontalAdvance;
                        blankData.texIndex = -1; //indicates no texture because the glyph is empty
                        texCoords.Add(character, blankData);
                    }
                    return;
                }

                //stacktrace doesn't really work that well when RenderGlyph throws an exception
                face.Glyph.RenderGlyph(RenderMode.Normal);
                bitmap = (byte[])face.Glyph.Bitmap.BufferData.Clone();
                glyphWidth = face.Glyph.Bitmap.Width;
                glyphHeight = bitmap.Length / glyphWidth;
                horizontalAdvance = face.Glyph.Metrics.HorizontalAdvance;
                drawOffset = new Vector2(face.Glyph.BitmapLeft, baseHeight * 14 / 10 - face.Glyph.BitmapTop);
            
                if (glyphWidth > texDims - 1 || glyphHeight > texDims - 1)
                {
                    throw new Exception(filename + ", " + size.ToString() + ", " + (char)character + "; Glyph dimensions exceed texture atlas dimensions");
                }
            
                currentDynamicAtlasNextY = Math.Max(currentDynamicAtlasNextY, glyphHeight + 2);
                if (currentDynamicAtlasCoords.X + glyphWidth + 2 > texDims - 1)
                {
                    currentDynamicAtlasCoords.X = 0;
                    currentDynamicAtlasCoords.Y += currentDynamicAtlasNextY;
                    currentDynamicAtlasNextY = 0;
                }            
                //no more room in current texture atlas, create a new one
                if (currentDynamicAtlasCoords.Y + glyphHeight + 2 > texDims - 1)
                {
                    currentDynamicAtlasCoords.X = 0;
                    currentDynamicAtlasCoords.Y = 0;
                    currentDynamicAtlasNextY = 0;
                    textures.Add(new Texture2D(gd, texDims, texDims, false, SurfaceFormat.Color));
                    currentDynamicPixelBuffer = null;
                }

                GlyphData newData = new GlyphData
                {
                    advance = (float)horizontalAdvance,
                    texIndex = textures.Count - 1,
                    texCoords = new Rectangle((int)currentDynamicAtlasCoords.X, (int)currentDynamicAtlasCoords.Y, glyphWidth, glyphHeight),
                    drawOffset = drawOffset
                };
                texCoords.Add(character, newData);

                if (currentDynamicPixelBuffer == null)
                {
                    currentDynamicPixelBuffer = new uint[texDims * texDims];
                    textures[newData.texIndex].GetData<uint>(currentDynamicPixelBuffer, 0, texDims * texDims);
                }
            
                for (int y = 0; y < glyphHeight; y++)
                {
                    for (int x = 0; x < glyphWidth; x++)
                    {
                        byte byteColor = bitmap[x + y * glyphWidth];
                        currentDynamicPixelBuffer[((int)currentDynamicAtlasCoords.X + x) + ((int)currentDynamicAtlasCoords.Y + y) * texDims] = (uint)(byteColor << 24 | 0x00ffffff);
                    }
                }
                textures[newData.texIndex].SetData<uint>(currentDynamicPixelBuffer);

                currentDynamicAtlasCoords.X += glyphWidth + 2;
            }
        }

        public void DrawString(SpriteBatch sb, string text, Vector2 position, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects se, float layerDepth)
        {
            if (textures.Count == 0 && !DynamicLoading) { return; }

            int lineNum = 0;
            Vector2 currentPos = position;
            Vector2 advanceUnit = rotation == 0.0f ? Vector2.UnitX : new Vector2((float)Math.Cos(rotation), (float)Math.Sin(rotation));
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lineNum++;
                    currentPos = position;
                    currentPos.X -= baseHeight * 1.8f * lineNum * advanceUnit.Y * scale.Y;
                    currentPos.Y += baseHeight * 1.8f * lineNum * advanceUnit.X * scale.Y;
                    continue;
                }

                uint charIndex = text[i];
                if (DynamicLoading)
                {
                    DynamicRenderAtlas(graphicsDevice, charIndex);
                }

                if (texCoords.TryGetValue(charIndex, out GlyphData gd) || texCoords.TryGetValue(9633, out gd)) //9633 = white square
                {
                    if (gd.texIndex >= 0)
                    {
                        Texture2D tex = textures[gd.texIndex];
                        Vector2 drawOffset;
                        drawOffset.X = gd.drawOffset.X * advanceUnit.X * scale.X - gd.drawOffset.Y * advanceUnit.Y * scale.Y;
                        drawOffset.Y = gd.drawOffset.X * advanceUnit.Y * scale.Y + gd.drawOffset.Y * advanceUnit.X * scale.X;

                        sb.Draw(tex, currentPos + drawOffset, gd.texCoords, color, rotation, origin, scale, se, layerDepth);
                    }
                    currentPos += gd.advance * advanceUnit * scale.X;
                }
            }
        }

        public void DrawString(SpriteBatch sb, string text, Vector2 position, Color color, float rotation, Vector2 origin, float scale, SpriteEffects se, float layerDepth)
        {
            DrawString(sb, text, position, color, rotation, origin, new Vector2(scale), se, layerDepth);
        }

        public void DrawString(SpriteBatch sb, string text, Vector2 position, Color color)
        {
            if (textures.Count == 0 && !DynamicLoading) { return; }

            Vector2 currentPos = position;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    currentPos.X = position.X;
                    currentPos.Y += baseHeight * 1.8f;
                    continue;
                }

                uint charIndex = text[i];
                if (DynamicLoading)
                {
                    DynamicRenderAtlas(graphicsDevice, charIndex);
                }

                if (texCoords.TryGetValue(charIndex, out GlyphData gd) || texCoords.TryGetValue(9633, out gd)) //9633 = white square
                {
                    if (gd.texIndex >= 0)
                    {
                        Texture2D tex = textures[gd.texIndex];
                        sb.Draw(tex, currentPos + gd.drawOffset, gd.texCoords, color);
                    }
                    currentPos.X += gd.advance;
                }
            }
        }

        public void DrawStringWithColors(SpriteBatch sb, string text, Vector2 position, Color color, float rotation, Vector2 origin, float scale, SpriteEffects se, float layerDepth, List<RichTextData> richTextData)
        {
            DrawStringWithColors(sb, text, position, color, rotation, origin, new Vector2(scale), se, layerDepth, richTextData);
        }

        public void DrawStringWithColors(SpriteBatch sb, string text, Vector2 position, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects se, float layerDepth, List<RichTextData> richTextData)
        {
            if (textures.Count == 0 && !DynamicLoading) { return; }

            int lineNum = 0;
            Vector2 currentPos = position;
            Vector2 advanceUnit = rotation == 0.0f ? Vector2.UnitX : new Vector2((float)Math.Cos(rotation), (float)Math.Sin(rotation));

            int richTextDataIndex = 0;
            RichTextData currentRichTextData = richTextData[richTextDataIndex];

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lineNum++;
                    currentPos = position;
                    currentPos.X -= baseHeight * 1.8f * lineNum * advanceUnit.Y * scale.Y;
                    currentPos.Y += baseHeight * 1.8f * lineNum * advanceUnit.X * scale.Y;
                    continue;
                }

                uint charIndex = text[i];
                if (DynamicLoading && !texCoords.ContainsKey(charIndex))
                {
                    DynamicRenderAtlas(graphicsDevice, charIndex);
                }

                Color currentTextColor;

                if (currentRichTextData != null && i > currentRichTextData.EndIndex + lineNum)
                {
                    richTextDataIndex++;
                    currentRichTextData = richTextDataIndex < richTextData.Count ? richTextData[richTextDataIndex] : null;
                }

                if (currentRichTextData != null && currentRichTextData.StartIndex + lineNum <= i && i <= currentRichTextData.EndIndex + lineNum)
                {
                    currentTextColor = currentRichTextData.Color ?? color;
                    if (!string.IsNullOrEmpty(currentRichTextData.Metadata))
                    {
                        currentTextColor = Color.Lerp(currentTextColor, Color.White, 0.5f);
                    }
                }
                else
                {
                    currentTextColor = color;
                }

                if (texCoords.TryGetValue(charIndex, out GlyphData gd) || texCoords.TryGetValue(9633, out gd)) //9633 = white square
                {
                    if (gd.texIndex >= 0)
                    {
                        Texture2D tex = textures[gd.texIndex];
                        Vector2 drawOffset;
                        drawOffset.X = gd.drawOffset.X * advanceUnit.X * scale.X - gd.drawOffset.Y * advanceUnit.Y * scale.Y;
                        drawOffset.Y = gd.drawOffset.X * advanceUnit.Y * scale.Y + gd.drawOffset.Y * advanceUnit.X * scale.X;

                        sb.Draw(tex, currentPos + drawOffset, gd.texCoords, currentTextColor, rotation, origin, scale, se, layerDepth);
                    }
                    currentPos += gd.advance * advanceUnit * scale.X;
                }
            }
        }

        public Vector2 MeasureString(string text, bool removeExtraSpacing = false)
        {
            if (text == null)
            {
                return Vector2.Zero;
            }

            float currentLineX = 0.0f;
            Vector2 retVal = Vector2.Zero;

            if (!removeExtraSpacing)
            {
                retVal.Y = baseHeight * 1.8f;
            }
            else
            {
                retVal.Y = baseHeight;
            }

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    currentLineX = 0.0f;
                    retVal.Y += baseHeight * 1.8f;
                    continue;
                }
                uint charIndex = text[i];
                if (DynamicLoading && !texCoords.ContainsKey(charIndex))
                {
                    DynamicRenderAtlas(graphicsDevice, charIndex);
                }
                if (texCoords.TryGetValue(charIndex, out GlyphData gd))
                {
                    currentLineX += gd.advance;
                }
                retVal.X = Math.Max(retVal.X, currentLineX);
            }
            return retVal;
        }

        public Vector2 MeasureChar(char c)
        {
            Vector2 retVal = Vector2.Zero;
            retVal.Y = baseHeight * 1.8f;
            if (DynamicLoading && !texCoords.ContainsKey(c))
            {
                DynamicRenderAtlas(graphicsDevice, c);
            }
            if (texCoords.TryGetValue(c, out GlyphData gd))
            {
                retVal.X = gd.advance;
            }
            return retVal;
        }

        public void Dispose()
        {
            FontList.Remove(this);
            foreach (Texture2D texture in textures)
            {
                texture.Dispose();
            }
            textures.Clear();
        }
    }
}
