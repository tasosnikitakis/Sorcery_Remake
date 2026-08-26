// ============================================================================
// IMGUI RENDERER
// SorceryForge — the whole of the Dear ImGui <-> MonoGame binding
// ============================================================================
// WHY THIS FILE EXISTS RATHER THAN A NUGET PACKAGE
//
// The obvious move is a MonoGame-flavoured ImGui package. Every one on NuGet
// was looked at, in November of the survey, and every one was rejected:
//
//   MonoGame.ImGuiNet 1.0.5     ships its assembly at the PACKAGE ROOT instead
//                               of under lib/<tfm>/, so referencing it compiles
//                               against nothing. It also fails to declare a
//                               MonoGame dependency at all.
//   Monogame.Imgui.Renderer     pinned to ImGui.NET 1.87.3 (2022).
//   ImGuiHandler.MonoGame       pinned to ImGui.NET 1.75.0 (2020).
//   ImGui.NET.Monogame-with-    depends on MonoGame.Framework.PORTABLE 3.7.1,
//   types                       a different framework flavour from the
//                               DesktopGL 3.8.1 this repo runs on.
//
// So the dependency is ImGui.NET itself — the binding everything else wraps,
// 5.6M downloads, MIT, and the only one of them that ships a first-class
// net8.0 target plus win-x64/win-arm64/linux/osx native cimgui. Pinned exactly
// at 1.91.6.1 in SorceryForge.csproj; the C# surface is generated from cimgui
// and moves with it, so a floating version is a build that breaks on its own.
//
// What a wrapper package would have supplied is what follows: a font atlas as
// a Texture2D, a vertex/index path for ImDrawData, and MonoGame input pumped
// into ImGui's IO. Roughly 450 lines, all of it mechanical, none of it moving.
//
// ---------------------------------------------------------------------------
// FRAME SHAPE — deliberately split across Update and Draw
//
//   Update:  BeginFrame(gt)   -> pump input, ImGui.NewFrame()
//            ... the chrome is BUILT here, and its callbacks fire here ...
//            EndFrame()       -> ImGui.Render(), draw data is now recorded
//   Draw:    ... every SpriteBatch pass (canvas, map, crop image) ...
//            RenderDrawData() -> the chrome paints on top
//
// The chrome is built in Update, not Draw, for one reason: every editor state
// mutation in this project happens in Update, and an ImGui button's callback IS
// a state mutation. Building the UI in Draw — which most MonoGame samples do —
// would put half the editor's writes in the render pass and leave the discard
// guard, the dirty flags and the room loader firing at a different point in the
// frame from every other write. The draw data recorded by ImGui.Render() stays
// valid until the next NewFrame, so handing it to Draw costs nothing.
// ============================================================================

using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NVector2 = System.Numerics.Vector2;

namespace SorceryForge.UI
{
    public sealed class ImGuiRenderer : IDisposable
    {
        private readonly Game _game;
        private readonly GraphicsDevice _device;

        // ---- GPU plumbing --------------------------------------------------

        private BasicEffect? _effect;

        // ScissorTestEnable is the whole point: ImGui hands us per-command clip
        // rectangles, and a panel that scrolls relies on them being honoured.
        private readonly RasterizerState _rasterizer = new()
        {
            CullMode = CullMode.None,
            DepthBias = 0,
            FillMode = FillMode.Solid,
            MultiSampleAntiAlias = false,
            ScissorTestEnable = true,
            SlopeScaleDepthBias = 0,
        };

        // ImDrawVert is 20 bytes: pos (Vector2) @0, uv (Vector2) @8, col
        // (packed RGBA) @16. MonoGame has no built-in vertex type with that
        // layout — VertexPositionColorTexture is 24 bytes in a different order
        // — so we describe ImGui's own layout and copy its buffers in verbatim
        // rather than transcoding several thousand vertices a frame.
        //
        // The colour order matches by luck and by design: ImGui's IM_COL32
        // packs R in the low byte unless IMGUI_USE_BGRA_PACKED_COLOR is defined
        // (it is not), and MonoGame's Color.PackedValue does the same.
        private const int DrawVertSize = 20;
        private static readonly VertexDeclaration DrawVertDeclaration = new(
            DrawVertSize,
            new VertexElement(0,  VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
            new VertexElement(8,  VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(16, VertexElementFormat.Color,   VertexElementUsage.Color, 0));

        private byte[] _vertexData = Array.Empty<byte>();
        private DynamicVertexBuffer? _vertexBuffer;
        private int _vertexBufferSize;

        private byte[] _indexData = Array.Empty<byte>();
        private DynamicIndexBuffer? _indexBuffer;
        private int _indexBufferSize;

        // ---- Texture registry ----------------------------------------------
        // ImGui refers to textures by an opaque IntPtr. We hand out small
        // integers and keep the Texture2D on this side. The font atlas takes
        // one; every sprite sheet the palette draws icons from takes another.
        private readonly Dictionary<IntPtr, Texture2D> _boundTextures = new();
        private int _nextTextureId = 1;
        private IntPtr _fontTextureId;
        private Texture2D? _fontTexture;

        // ---- Input ---------------------------------------------------------

        private int _scrollWheelValue;
        private int _horizontalScrollWheelValue;
        private readonly List<char> _pendingChars = new();

        /// <summary>
        /// True once BeginFrame has run at least once. Draw can legally happen
        /// before the first Update on some platforms; rendering draw data that
        /// was never recorded would read a dangling pointer.
        /// </summary>
        private bool _frameStarted;
        private bool _frameRecorded;

        public ImGuiRenderer(Game game)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
            _device = game.GraphicsDevice;

            ImGui.CreateContext();
            ImGui.StyleColorsDark();

            var io = ImGui.GetIO();

            // RendererHasVtxOffset lets ImGui emit more than 64k vertices per
            // draw list by offsetting rather than splitting. We honour
            // cmd.VtxOffset in RenderCommandLists, so we may claim it — and at
            // seventy-five inspector sections we will want it.
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

            // Keyboard nav stays OFF. With it on, ImGui claims the arrow keys
            // and Enter/Escape for widget navigation — which are the canvas's
            // pan keys, the crop step's confirm, and the editor's discard
            // guard. Every one of those is a documented keybind of this editor
            // and none of them may be intercepted.
            io.ConfigFlags &= ~ImGuiConfigFlags.NavEnableKeyboard;

            // No imgui.ini. Every chrome window is positioned and sized by code
            // on every frame (SetNextWindowPos/Size with Always), so the file
            // would record nothing that is ever read back — and a file written
            // but never read beside a source tree is exactly the silt the
            // EditorSettings header argues against. The day a free-floating
            // window appears, point this at .sorceryforge/imgui.ini, which is
            // already gitignored.
            unsafe { io.NativePtr->IniFilename = null; }

            // Character input for any future text field. Wired now rather than
            // later because the alternative is a text box that silently eats
            // nothing and a puzzled half hour finding out why.
            _game.Window.TextInput += OnTextInput;

            RebuildFontAtlas();
        }

        // ====================================================================
        // TEXTURES
        // ====================================================================

        /// <summary>
        /// Register a MonoGame texture with ImGui and get back the handle to
        /// pass to ImGui.Image / drawList.AddImage.
        /// </summary>
        public IntPtr BindTexture(Texture2D texture)
        {
            var id = new IntPtr(_nextTextureId++);
            _boundTextures.Add(id, texture);
            return id;
        }

        public void UnbindTexture(IntPtr textureId) => _boundTextures.Remove(textureId);

        /// <summary>
        /// Build (or rebuild) the font atlas into a Texture2D. Called once at
        /// construction; a later PR that loads a TTF calls it again.
        /// </summary>
        public void RebuildFontAtlas()
        {
            var io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixelData, out int width, out int height, out int bytesPerPixel);

            var pixels = new byte[width * height * bytesPerPixel];
            Marshal.Copy(pixelData, pixels, 0, pixels.Length);

            var texture = new Texture2D(_device, width, height, false, SurfaceFormat.Color);
            texture.SetData(pixels);

            if (_fontTextureId != IntPtr.Zero) UnbindTexture(_fontTextureId);
            _fontTexture?.Dispose();

            _fontTexture = texture;
            _fontTextureId = BindTexture(texture);
            io.Fonts.SetTexID(_fontTextureId);

            // The CPU-side copy has served its purpose; ImGui keeps only the
            // handle from here on.
            io.Fonts.ClearTexData();
        }

        // ====================================================================
        // FRAME
        // ====================================================================

        /// <summary>
        /// Pump input into ImGui and open a frame. Everything the chrome draws
        /// must happen between this and <see cref="EndFrame"/>.
        /// </summary>
        public void BeginFrame(GameTime gameTime)
        {
            var io = ImGui.GetIO();

            io.DisplaySize = new NVector2(
                _device.PresentationParameters.BackBufferWidth,
                _device.PresentationParameters.BackBufferHeight);
            io.DisplayFramebufferScale = new NVector2(1f, 1f);
            io.DeltaTime = Math.Max(1f / 1000f, (float)gameTime.ElapsedGameTime.TotalSeconds);

            PumpInput(io);

            ImGui.NewFrame();
            _frameStarted = true;
            _frameRecorded = false;
        }

        /// <summary>Close the frame and record its draw data for Draw to paint.</summary>
        public void EndFrame()
        {
            if (!_frameStarted) return;
            ImGui.Render();
            _frameStarted = false;
            _frameRecorded = true;
        }

        /// <summary>
        /// Paint the frame recorded by <see cref="EndFrame"/>. Call from Draw,
        /// AFTER every SpriteBatch pass — the chrome sits on top of the canvas.
        /// </summary>
        public void RenderDrawData()
        {
            if (!_frameRecorded) return;
            RenderDrawData(ImGui.GetDrawData());
        }

        // ====================================================================
        // INPUT
        // ====================================================================
        // ImGui is TOLD about input here; whether the editor then ACTS on that
        // same input is decided by EditorChrome, which reads WantCaptureMouse /
        // WantCaptureKeyboard right after NewFrame. This method never decides
        // anything — it only reports.
        // ====================================================================

        private void OnTextInput(object? sender, TextInputEventArgs e)
        {
            // '\t' arrives here as well as through the key path, and feeding it
            // as a character would insert a tab into any focused field while
            // Tab is also the map-mode toggle.
            if (e.Character == '\t') return;
            _pendingChars.Add(e.Character);
        }

        private void PumpInput(ImGuiIOPtr io)
        {
            var mouse = Mouse.GetState();
            var keyboard = Keyboard.GetState();

            io.AddMousePosEvent(mouse.X, mouse.Y);
            io.AddMouseButtonEvent(0, mouse.LeftButton == ButtonState.Pressed);
            io.AddMouseButtonEvent(1, mouse.RightButton == ButtonState.Pressed);
            io.AddMouseButtonEvent(2, mouse.MiddleButton == ButtonState.Pressed);

            // SDL reports ~120 units per notch; ImGui wants notches.
            int wheelDelta = mouse.ScrollWheelValue - _scrollWheelValue;
            int wheelDeltaH = mouse.HorizontalScrollWheelValue - _horizontalScrollWheelValue;
            _scrollWheelValue = mouse.ScrollWheelValue;
            _horizontalScrollWheelValue = mouse.HorizontalScrollWheelValue;
            if (wheelDelta != 0 || wheelDeltaH != 0)
                io.AddMouseWheelEvent(wheelDeltaH / 120f, wheelDelta / 120f);

            foreach (var (xnaKey, imKey) in KeyMap)
                io.AddKeyEvent(imKey, keyboard.IsKeyDown(xnaKey));

            io.AddKeyEvent(ImGuiKey.ModCtrl,
                keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl));
            io.AddKeyEvent(ImGuiKey.ModShift,
                keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
            io.AddKeyEvent(ImGuiKey.ModAlt,
                keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt));
            io.AddKeyEvent(ImGuiKey.ModSuper,
                keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows));

            for (int i = 0; i < _pendingChars.Count; i++) io.AddInputCharacter(_pendingChars[i]);
            _pendingChars.Clear();
        }

        // MonoGame Keys -> ImGuiKey. Only the keys ImGui has a name for; the
        // editor's own shortcuts are read straight from KeyboardState as they
        // always were, so nothing here is load-bearing for them.
        private static readonly (Keys, ImGuiKey)[] KeyMap =
        {
            (Keys.Tab, ImGuiKey.Tab),
            (Keys.Left, ImGuiKey.LeftArrow), (Keys.Right, ImGuiKey.RightArrow),
            (Keys.Up, ImGuiKey.UpArrow), (Keys.Down, ImGuiKey.DownArrow),
            (Keys.PageUp, ImGuiKey.PageUp), (Keys.PageDown, ImGuiKey.PageDown),
            (Keys.Home, ImGuiKey.Home), (Keys.End, ImGuiKey.End),
            (Keys.Insert, ImGuiKey.Insert), (Keys.Delete, ImGuiKey.Delete),
            (Keys.Back, ImGuiKey.Backspace), (Keys.Space, ImGuiKey.Space),
            (Keys.Enter, ImGuiKey.Enter), (Keys.Escape, ImGuiKey.Escape),
            (Keys.LeftControl, ImGuiKey.LeftCtrl), (Keys.RightControl, ImGuiKey.RightCtrl),
            (Keys.LeftShift, ImGuiKey.LeftShift), (Keys.RightShift, ImGuiKey.RightShift),
            (Keys.LeftAlt, ImGuiKey.LeftAlt), (Keys.RightAlt, ImGuiKey.RightAlt),
            (Keys.LeftWindows, ImGuiKey.LeftSuper), (Keys.RightWindows, ImGuiKey.RightSuper),
            (Keys.Apps, ImGuiKey.Menu),

            (Keys.D0, ImGuiKey._0), (Keys.D1, ImGuiKey._1), (Keys.D2, ImGuiKey._2),
            (Keys.D3, ImGuiKey._3), (Keys.D4, ImGuiKey._4), (Keys.D5, ImGuiKey._5),
            (Keys.D6, ImGuiKey._6), (Keys.D7, ImGuiKey._7), (Keys.D8, ImGuiKey._8),
            (Keys.D9, ImGuiKey._9),

            (Keys.A, ImGuiKey.A), (Keys.B, ImGuiKey.B), (Keys.C, ImGuiKey.C),
            (Keys.D, ImGuiKey.D), (Keys.E, ImGuiKey.E), (Keys.F, ImGuiKey.F),
            (Keys.G, ImGuiKey.G), (Keys.H, ImGuiKey.H), (Keys.I, ImGuiKey.I),
            (Keys.J, ImGuiKey.J), (Keys.K, ImGuiKey.K), (Keys.L, ImGuiKey.L),
            (Keys.M, ImGuiKey.M), (Keys.N, ImGuiKey.N), (Keys.O, ImGuiKey.O),
            (Keys.P, ImGuiKey.P), (Keys.Q, ImGuiKey.Q), (Keys.R, ImGuiKey.R),
            (Keys.S, ImGuiKey.S), (Keys.T, ImGuiKey.T), (Keys.U, ImGuiKey.U),
            (Keys.V, ImGuiKey.V), (Keys.W, ImGuiKey.W), (Keys.X, ImGuiKey.X),
            (Keys.Y, ImGuiKey.Y), (Keys.Z, ImGuiKey.Z),

            (Keys.F1, ImGuiKey.F1), (Keys.F2, ImGuiKey.F2), (Keys.F3, ImGuiKey.F3),
            (Keys.F4, ImGuiKey.F4), (Keys.F5, ImGuiKey.F5), (Keys.F6, ImGuiKey.F6),
            (Keys.F7, ImGuiKey.F7), (Keys.F8, ImGuiKey.F8), (Keys.F9, ImGuiKey.F9),
            (Keys.F10, ImGuiKey.F10), (Keys.F11, ImGuiKey.F11), (Keys.F12, ImGuiKey.F12),

            (Keys.OemQuotes, ImGuiKey.Apostrophe), (Keys.OemComma, ImGuiKey.Comma),
            (Keys.OemMinus, ImGuiKey.Minus), (Keys.OemPeriod, ImGuiKey.Period),
            (Keys.OemQuestion, ImGuiKey.Slash), (Keys.OemSemicolon, ImGuiKey.Semicolon),
            (Keys.OemPlus, ImGuiKey.Equal), (Keys.OemOpenBrackets, ImGuiKey.LeftBracket),
            (Keys.OemPipe, ImGuiKey.Backslash), (Keys.OemCloseBrackets, ImGuiKey.RightBracket),
            (Keys.OemTilde, ImGuiKey.GraveAccent),

            (Keys.CapsLock, ImGuiKey.CapsLock), (Keys.Scroll, ImGuiKey.ScrollLock),
            (Keys.NumLock, ImGuiKey.NumLock), (Keys.PrintScreen, ImGuiKey.PrintScreen),
            (Keys.Pause, ImGuiKey.Pause),

            (Keys.NumPad0, ImGuiKey.Keypad0), (Keys.NumPad1, ImGuiKey.Keypad1),
            (Keys.NumPad2, ImGuiKey.Keypad2), (Keys.NumPad3, ImGuiKey.Keypad3),
            (Keys.NumPad4, ImGuiKey.Keypad4), (Keys.NumPad5, ImGuiKey.Keypad5),
            (Keys.NumPad6, ImGuiKey.Keypad6), (Keys.NumPad7, ImGuiKey.Keypad7),
            (Keys.NumPad8, ImGuiKey.Keypad8), (Keys.NumPad9, ImGuiKey.Keypad9),
            (Keys.Decimal, ImGuiKey.KeypadDecimal), (Keys.Divide, ImGuiKey.KeypadDivide),
            (Keys.Multiply, ImGuiKey.KeypadMultiply), (Keys.Subtract, ImGuiKey.KeypadSubtract),
            (Keys.Add, ImGuiKey.KeypadAdd),
        };

        // ====================================================================
        // RENDER
        // ====================================================================

        private void RenderDrawData(ImDrawDataPtr drawData)
        {
            // Everything SpriteBatch left behind is restored on the way out —
            // the crop overlay's LinearClamp pass, the canvas's scissor, all of
            // it. A renderer that silently changes global device state is a
            // renderer that breaks the next frame's first SpriteBatch.Begin.
            var lastViewport = _device.Viewport;
            var lastScissor = _device.ScissorRectangle;
            var lastBlendFactor = _device.BlendFactor;
            var lastBlendState = _device.BlendState;
            var lastRasterizer = _device.RasterizerState;
            var lastDepthStencil = _device.DepthStencilState;
            var lastSampler = _device.SamplerStates[0];

            _device.BlendFactor = Color.White;
            _device.BlendState = BlendState.NonPremultiplied;   // ImGui emits straight alpha
            _device.RasterizerState = _rasterizer;
            _device.DepthStencilState = DepthStencilState.DepthRead;

            // PointClamp, not the LinearClamp most ImGui backends use. Two
            // reasons, both about this editor: the default ImGui font is a
            // bitmap face rendered 1:1, so point sampling is exact rather than
            // blurry; and the palette draws real game sprites through
            // ImGui.Image, which are pixel art and were point-sampled by the
            // SpriteBatch chrome this replaces.
            _device.SamplerStates[0] = SamplerState.PointClamp;

            drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

            _device.Viewport = new Viewport(0, 0,
                _device.PresentationParameters.BackBufferWidth,
                _device.PresentationParameters.BackBufferHeight);

            UpdateBuffers(drawData);
            RenderCommandLists(drawData);

            _device.Viewport = lastViewport;
            _device.ScissorRectangle = lastScissor;
            _device.BlendFactor = lastBlendFactor;
            _device.BlendState = lastBlendState;
            _device.RasterizerState = lastRasterizer;
            _device.DepthStencilState = lastDepthStencil;
            _device.SamplerStates[0] = lastSampler;
        }

        private void UpdateBuffers(ImDrawDataPtr drawData)
        {
            if (drawData.TotalVtxCount == 0) return;

            // Grow by 1.5x rather than exactly: a panel that gains a row must
            // not reallocate two GPU buffers every frame it is resized.
            if (drawData.TotalVtxCount > _vertexBufferSize)
            {
                _vertexBuffer?.Dispose();
                _vertexBufferSize = (int)(drawData.TotalVtxCount * 1.5f);
                _vertexBuffer = new DynamicVertexBuffer(_device, DrawVertDeclaration,
                                                        _vertexBufferSize, BufferUsage.None);
                _vertexData = new byte[_vertexBufferSize * DrawVertSize];
            }

            if (drawData.TotalIdxCount > _indexBufferSize)
            {
                _indexBuffer?.Dispose();
                _indexBufferSize = (int)(drawData.TotalIdxCount * 1.5f);
                _indexBuffer = new DynamicIndexBuffer(_device, IndexElementSize.SixteenBits,
                                                      _indexBufferSize, BufferUsage.None);
                _indexData = new byte[_indexBufferSize * sizeof(ushort)];
            }

            int vtxOffset = 0, idxOffset = 0;
            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                ImDrawListPtr cmdList = drawData.CmdLists[n];

                Marshal.Copy(cmdList.VtxBuffer.Data, _vertexData,
                             vtxOffset * DrawVertSize, cmdList.VtxBuffer.Size * DrawVertSize);
                Marshal.Copy(cmdList.IdxBuffer.Data, _indexData,
                             idxOffset * sizeof(ushort), cmdList.IdxBuffer.Size * sizeof(ushort));

                vtxOffset += cmdList.VtxBuffer.Size;
                idxOffset += cmdList.IdxBuffer.Size;
            }

            _vertexBuffer!.SetData(_vertexData, 0, drawData.TotalVtxCount * DrawVertSize);
            _indexBuffer!.SetData(_indexData, 0, drawData.TotalIdxCount * sizeof(ushort));
        }

        private void RenderCommandLists(ImDrawDataPtr drawData)
        {
            if (drawData.TotalVtxCount == 0) return;

            _device.SetVertexBuffer(_vertexBuffer);
            _device.Indices = _indexBuffer;

            int vtxOffset = 0, idxOffset = 0;
            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                ImDrawListPtr cmdList = drawData.CmdLists[n];

                for (int i = 0; i < cmdList.CmdBuffer.Size; i++)
                {
                    ImDrawCmdPtr cmd = cmdList.CmdBuffer[i];

                    if (cmd.ElemCount == 0) continue;

                    if (!_boundTextures.ContainsKey(cmd.TextureId))
                        throw new InvalidOperationException(
                            $"Could not find a texture bound to id {cmd.TextureId}. " +
                            "Every texture an ImGui draw call names must come from BindTexture.");

                    // Clip rects arrive in framebuffer space and can fall
                    // partly outside it (a window dragged past the edge, a
                    // window taller than the screen). MonoGame throws on a
                    // scissor rectangle that leaves the render target, so the
                    // clamp is not a nicety.
                    _device.ScissorRectangle = ClampScissor(new Rectangle(
                        (int)cmd.ClipRect.X,
                        (int)cmd.ClipRect.Y,
                        (int)(cmd.ClipRect.Z - cmd.ClipRect.X),
                        (int)(cmd.ClipRect.W - cmd.ClipRect.Y)));

                    var effect = UpdateEffect(_boundTextures[cmd.TextureId]);
                    foreach (var pass in effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        _device.DrawIndexedPrimitives(
                            primitiveType: PrimitiveType.TriangleList,
                            baseVertex: (int)cmd.VtxOffset + vtxOffset,
                            startIndex: (int)cmd.IdxOffset + idxOffset,
                            primitiveCount: (int)cmd.ElemCount / 3);
                    }
                }

                vtxOffset += cmdList.VtxBuffer.Size;
                idxOffset += cmdList.IdxBuffer.Size;
            }
        }

        private Rectangle ClampScissor(Rectangle r)
        {
            int maxW = _device.PresentationParameters.BackBufferWidth;
            int maxH = _device.PresentationParameters.BackBufferHeight;

            int x = Math.Clamp(r.X, 0, maxW);
            int y = Math.Clamp(r.Y, 0, maxH);
            int right = Math.Clamp(r.Right, x, maxW);
            int bottom = Math.Clamp(r.Bottom, y, maxH);
            return new Rectangle(x, y, right - x, bottom - y);
        }

        private BasicEffect UpdateEffect(Texture2D texture)
        {
            _effect ??= new BasicEffect(_device);

            var io = ImGui.GetIO();
            _effect.World = Matrix.Identity;
            _effect.View = Matrix.Identity;
            // Y grows downward, matching ImGui's screen space and MonoGame's.
            _effect.Projection = Matrix.CreateOrthographicOffCenter(
                0f, io.DisplaySize.X, io.DisplaySize.Y, 0f, -1f, 1f);
            _effect.TextureEnabled = true;
            _effect.Texture = texture;
            _effect.VertexColorEnabled = true;
            return _effect;
        }

        // ====================================================================
        // DISPOSE
        // ====================================================================

        public void Dispose()
        {
            _game.Window.TextInput -= OnTextInput;
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
            _effect?.Dispose();
            _rasterizer.Dispose();
            _fontTexture?.Dispose();
            // Textures bound by callers stay theirs to dispose — the palette's
            // sheets belong to the ContentManager and must not be freed here.
            _boundTextures.Clear();
        }
    }
}
