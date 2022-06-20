using MMDataIO.Pmx;
using MMDExtensions;
using MMDExtensions.PMX;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Diagnostics;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.WIC;
using SharpDX.Windows;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Buffer = SharpDX.Direct3D11.Buffer;
using Color = SharpDX.Color;
using Device = SharpDX.Direct3D11.Device;
using Mathf = System.Math;

// use namespaces shortcuts to reduce typing and avoid the messing the same class names from different namespaces

namespace PMXRenderer
{
    public class PMXRenderer
    {
        #region Private Members
        private Device device;
        private DeviceContext context;
        private RenderForm form;
        private SwapChainDescription swapChainDescription;
        private RasterizerState rasterizerState;
        private BlendState blendState;
        private Texture2D backBuffer;
        private Texture2D depthBuffer;
        private InputLayout layout;
        private VertexShader vertexShader;
        private PixelShader pixelShader;
        private Buffer constantBuffer;
        private CBuffer cBuffer;
        private RenderTargetView renderView;
        private DepthStencilView depthView;

        private ShaderResourceView whiteTexture;
        private ShaderResourceView blackTexture;


        private int width;
        private int height;

        private string pmxPath;

        private Vector3 cameraPosition;
        private Vector3 lookTarget;

        private readonly Dictionary<string, (ShaderResourceView Resource, bool IsArgb)> textureCache = new Dictionary<string, (ShaderResourceView Resource, bool IsArgb)>();
        private readonly List<ShaderResourceView> toonCache = new List<ShaderResourceView>();
        private List<IDisposable> disposables = new List<IDisposable>();
        private List<Texture2D> texture2DCache = new List<Texture2D>();
        private (Buffer Buffer, int VertexCount)[] vertexBuffers;
        private List<VertexBufferBinding> vertexBufferBindings;

        private PMXFormat model;
        private BoundingBox bounds;

        private const double Rad2Deg = 360 / (Mathf.PI * 2);
        private const double Deg2Rad = (Mathf.PI * 2) / 360;
        private const float Fov = 42f;
        private const float CameraDistance = 0.8f; // Constant factor
        private const float MoveSpeed = 0.1f;
        private const float ModelScale = 0.08f;

        private const string ShaderPath = @"Assets/Toon.hlsl";
        private string AssemblyFolder => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        private Color ClearColor => new Color(new Vector4(0, 0, 0, 0.3f));
        #endregion

        public System.Drawing.Bitmap Blit(Device device, Texture2D src)
        {
            var width = src.Description.Width;
            var height = src.Description.Height;
            var context = device.ImmediateContext;
            var textureDesc = src.Description;

            textureDesc.BindFlags = BindFlags.None;
            textureDesc.OptionFlags &= ResourceOptionFlags.TextureCube;
            textureDesc.Usage = ResourceUsage.Staging;
            textureDesc.CpuAccessFlags = CpuAccessFlags.Read;

            var screenTexture = new Texture2D(device, textureDesc);

            context.CopyResource(src, screenTexture);

            // Get the desktop capture texture
            var mapSource = context.MapSubresource(screenTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

            // Create Drawing.Bitmap
            var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var boundsRect = new System.Drawing.Rectangle(0, 0, width, height);

            // Copy pixels from screen capture Texture to GDI bitmap
            var mapDest = bitmap.LockBits(boundsRect, ImageLockMode.WriteOnly, bitmap.PixelFormat);
            var sourcePtr = mapSource.DataPointer;
            var destPtr = mapDest.Scan0;
            for (int y = 0; y < height; y++)
            {
                // Copy a single line
                Utilities.CopyMemory(destPtr, sourcePtr, width * 4);

                // Advance pointers
                sourcePtr = System.IntPtr.Add(sourcePtr, mapSource.RowPitch);
                destPtr = System.IntPtr.Add(destPtr, mapDest.Stride);
            }

            // Release source and dest locks
            bitmap.UnlockBits(mapDest);
            context.UnmapSubresource(screenTexture, 0);
            screenTexture.Dispose();
            return bitmap;
        }

        #region Render Entry
        public void GeneratePmxPreviewWindow(string absolutePmxPath, int previewWidth = 800, int previewHeight = 600)
        {
            width = previewWidth;
            height = previewHeight;
            pmxPath = absolutePmxPath;
            form = new RenderForm("PMXRenderer");

            PrepareBeforeDevice();
            Device.CreateWithSwapChain(DriverType.Hardware, DeviceCreationFlags.None, swapChainDescription, out device, out var swapChain);
            context = device.ImmediateContext;

            var factory = swapChain.GetParent<Factory>();
            factory.MakeWindowAssociation(form.Handle, WindowAssociationFlags.IgnoreAll);

            PrepareAfterDevice();

            bool userResized = true;
            bool blit = false;
            form.UserResized += (sender, args) => userResized = true;
            form.KeyDown += (sender, args) =>
            {
                switch (args.KeyCode)
                {
                    case Keys.W:
                        cameraPosition += Vector3.ForwardRH * MoveSpeed;
                        break;

                    case Keys.S:
                        cameraPosition -= Vector3.ForwardRH * MoveSpeed;
                        break;

                    case Keys.A:
                        cameraPosition += Vector3.Left * MoveSpeed;
                        break;

                    case Keys.D:
                        cameraPosition += Vector3.Right * MoveSpeed;
                        break;

                    case Keys.Q:
                        cameraPosition += Vector3.Up * MoveSpeed;
                        break;

                    case Keys.E:
                        cameraPosition += Vector3.Down * MoveSpeed;
                        break;

                    case Keys.I:
                        lookTarget += Vector3.ForwardRH * MoveSpeed;
                        break;

                    case Keys.K:
                        lookTarget -= Vector3.ForwardRH * MoveSpeed;
                        break;

                    case Keys.J:
                        lookTarget += Vector3.Left * MoveSpeed;
                        break;

                    case Keys.L:
                        lookTarget += Vector3.Right * MoveSpeed;
                        break;

                    case Keys.U:
                        lookTarget += Vector3.Up * MoveSpeed;
                        break;

                    case Keys.O:
                        lookTarget += Vector3.Down * MoveSpeed;
                        break;

                    case Keys.Enter:
                        blit = true;
                        break;

                    default:
                        break;
                }
            };

            RenderLoop.Run(form, () =>
            {
                // If Form resized
                if (userResized)
                {
                    // Dispose all previous allocated resources
                    Utilities.Dispose(ref backBuffer);
                    Utilities.Dispose(ref renderView);
                    Utilities.Dispose(ref depthBuffer);
                    Utilities.Dispose(ref depthView);

                    width = form.ClientSize.Width;
                    height = form.ClientSize.Height;

                    swapChain.ResizeBuffers(swapChainDescription.BufferCount, form.ClientSize.Width, form.ClientSize.Height, Format.Unknown, SwapChainFlags.None);

                    // Get the backbuffer from the swapchain
                    backBuffer = Texture2D.FromSwapChain<Texture2D>(swapChain, 0);
                    BuildRenderTargets();
                    renderView = new RenderTargetView(device, backBuffer);
                    depthView = new DepthStencilView(device, depthBuffer);
                    context.Rasterizer.SetViewport(new Viewport(0, 0, form.ClientSize.Width, form.ClientSize.Height, 0.0f, 1.0f));
                    context.OutputMerger.SetTargets(depthView, renderView);

                    userResized = false;
                }

                context.ClearDepthStencilView(depthView, DepthStencilClearFlags.Depth, 1.0f, 0);
                context.ClearRenderTargetView(renderView, ClearColor);
                cBuffer.randerParams.Y = 0;
                DrawMaterials();

                swapChain.Present(0, PresentFlags.None);

                if (blit)
                {
                    var bitmap = Blit(device, swapChain.GetBackBuffer<Texture2D>(0));
                    bitmap.Save(Path.Combine(AssemblyFolder, "Assets/image.bmp"));
                    blit = false;
                }
            });

            // Release all resources
            vertexShader.Dispose();
            pixelShader.Dispose();
            layout.Dispose();
            constantBuffer.Dispose();
            depthBuffer.Dispose();
            depthView.Dispose();
            renderView.Dispose();
            backBuffer.Dispose();
            context.ClearState();
            context.Flush();
            device.Dispose();
            context.Dispose();
            swapChain.Dispose();
            factory.Dispose();
        }

        public System.Drawing.Bitmap GeneratePmxPreview(string absolutePmxPath, int previewWidth = 800, int previewHeight = 600)
        {
            //Configuration.EnableObjectTracking = true;
            width = previewWidth;
            height = previewHeight;
            pmxPath = absolutePmxPath;

            //PrepareBeforeDevice();
            device = new Device(DriverType.Hardware, DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport | DeviceCreationFlags.None).QueryInterface<Device>();
            context = device.ImmediateContext;
            try
            {
                PrepareAfterDevice();
            }
            catch (Exception)
            {
                return null;
            }

            var screenDescription = new Texture2DDescription()
            {
                ArraySize = 1,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                CpuAccessFlags = CpuAccessFlags.None,
                Format = Format.R8G8B8A8_UNorm,
                Width = width,
                Height = height,
                MipLevels = 1,
                OptionFlags = ResourceOptionFlags.None,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
            };

            backBuffer = new Texture2D(device, screenDescription);
            BuildRenderTargets();

            renderView = new RenderTargetView(device, backBuffer);
            depthView = new DepthStencilView(device, depthBuffer);

            context.Rasterizer.SetViewport(new Viewport(0, 0, width, height, 0.0f, 1.0f));
            context.OutputMerger.SetTargets(depthView, renderView);
            context.OutputMerger.SetBlendState(blendState);
            context.ClearDepthStencilView(depthView, DepthStencilClearFlags.Depth, 1.0f, 0);
            context.ClearRenderTargetView(renderView, ClearColor);
            cBuffer.randerParams.Y = 1;
            DrawMaterials();

            var bitmap = Blit(device, backBuffer);

            foreach (var texture in texture2DCache)
            {
                if (texture != null) texture.Dispose();
            }

            foreach (var texture in textureCache)
            {
                texture.Value.Resource.Dispose();
            }

            foreach (var texture in toonCache)
            {
                texture.Dispose();
            }

            // Release all resources
            blendState.Dispose();
            rasterizerState.Dispose();
            whiteTexture.Dispose();
            blackTexture.Dispose();
            vertexShader.Dispose();
            pixelShader.Dispose();
            layout.Dispose();
            constantBuffer.Dispose();
            depthBuffer.Dispose();
            depthView.Dispose();
            renderView.Dispose();
            backBuffer.Dispose();
            context.ClearState();
            context.Flush();
            context.Dispose();
            device.Dispose();
            foreach (var buffer in vertexBuffers)
            {
                buffer.Buffer.Dispose();
            }
            //var message = ObjectTracker.ReportActiveObjects();
            //MessageBox.Show(message);
            //Clipboard.SetText(message);
            return bitmap;
        }
        #endregion

        private void LoadModel()
        {
            model = PMXLoaderScript.Import(pmxPath);
            var submeshes = PMXLoaderScript.CreateMeshCreationInfoSingle(model);

            vertexBuffers = new (Buffer Buffer, int VertexCount)[model.material_list.material.Length];
            for (int i = 0; i < model.material_list.material.Length; ++i)
            {
                var submesh = submeshes.value[i];
                var vertexData = new VertexData[submesh.plane_indices.Length];
                for (int j = 0; j < submesh.plane_indices.Length; ++j)
                {
                    var index = submesh.plane_indices[j];
                    var vertex = model.vertex_list.vertex[submeshes.reassign_dictionary[index]];
                    vertexData[j] = new VertexData()
                    {
                        Position = vertex.pos * ModelScale,
                        Normal = vertex.normal_vec,
                        UV = vertex.uv
                    };
                }
                vertexBuffers[i] = (Buffer.Create(device, BindFlags.VertexBuffer, vertexData), submesh.plane_indices.Length);
            }
            vertexBufferBindings = vertexBuffers.Select(x => new VertexBufferBinding(x.Buffer, Utilities.SizeOf<VertexData>(), 0)).ToList();

            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            foreach (var vertex in model.face_vertex_list.face_vert_index.Select(x => model.vertex_list.vertex[x].pos))
            {
                if (vertex.X < min.X)
                    min.X = vertex.X;
                if (vertex.Y < min.Y)
                    min.Y = vertex.Y;
                if (vertex.Z < min.Z)
                    min.Z = vertex.Z;

                if (vertex.X > max.X)
                    max.X = vertex.X;
                if (vertex.Y > max.Y)
                    max.Y = vertex.Y;
                if (vertex.Z > max.Z)
                    max.Z = vertex.Z;
            }

            bounds = new BoundingBox(min * ModelScale, max * ModelScale);

            foreach (var texturePath in model.texture_list.texture_file)
            {
                var directory = Path.GetDirectoryName(pmxPath);
                var fullPath = Path.Combine(directory, texturePath);
                if (File.Exists(fullPath))
                {
                    if (!textureCache.ContainsKey(texturePath))
                    {
                        var (Texture, IsArgb) = LoadTexture(device, fullPath);
                        texture2DCache.Add(Texture);
                        var res = Texture != null ? new ShaderResourceView(device, Texture) : whiteTexture;
                        res.DebugName = Path.GetFileNameWithoutExtension(fullPath);
                        textureCache.Add(texturePath, (res, IsArgb));
                    }
                }
            }
            for (int i = 0; i <= 10; i++)
            {
                var fileName = $"Assets/toon{i:D2}.bmp";
                var path = Path.Combine(AssemblyFolder, fileName);
                var (Texture, IsArgb) = LoadTexture(device, path);
                texture2DCache.Add(Texture);
                toonCache.Add(Texture != null ? new ShaderResourceView(device, Texture) { DebugName = Path.GetFileNameWithoutExtension(fileName) } : whiteTexture);
            }
        }

        private void PrepareBeforeDevice()
        {
            swapChainDescription = new SwapChainDescription()
            {
                BufferCount = 1,
                ModeDescription =
                    new ModeDescription(form.ClientSize.Width, form.ClientSize.Height,
                                        new Rational(60, 1), Format.R8G8B8A8_UNorm),
                IsWindowed = true,
                OutputHandle = form.Handle,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.Discard,
                Usage = Usage.RenderTargetOutput
            };
        }

        private void PrepareAfterDevice()
        {
            var blendStateDescription = new BlendStateDescription
            {
                AlphaToCoverageEnable = true,
                IndependentBlendEnable = true
            };
            blendStateDescription.RenderTarget[0].IsBlendEnabled = true;
            blendStateDescription.RenderTarget[0].SourceBlend = BlendOption.SourceAlpha;
            blendStateDescription.RenderTarget[0].DestinationBlend = BlendOption.InverseSourceAlpha;
            blendStateDescription.RenderTarget[0].BlendOperation = BlendOperation.Add;
            blendStateDescription.RenderTarget[0].SourceAlphaBlend = BlendOption.One;
            blendStateDescription.RenderTarget[0].DestinationAlphaBlend = BlendOption.Zero;
            blendStateDescription.RenderTarget[0].AlphaBlendOperation = BlendOperation.Add;
            blendStateDescription.RenderTarget[0].RenderTargetWriteMask = ColorWriteMaskFlags.All;

            blendState = new BlendState(device, blendStateDescription);
            rasterizerState = new RasterizerState(device, new RasterizerStateDescription()
            {
                CullMode = CullMode.None,
                DepthBias = 0,
                DepthBiasClamp = 0,
                FillMode = FillMode.Solid,
                IsAntialiasedLineEnabled = true,
                IsDepthClipEnabled = false,
                IsFrontCounterClockwise = false,
                IsMultisampleEnabled = true,
                IsScissorEnabled = false,
                SlopeScaledDepthBias = 0f
            });

            // Compile Vertex and Pixel shaders
            var vertexShaderByteCode = ShaderBytecode.CompileFromFile(Path.Combine(AssemblyFolder, ShaderPath), "VS", "vs_4_0");
            vertexShader = new VertexShader(device, vertexShaderByteCode);

            var pixelShaderByteCode = ShaderBytecode.CompileFromFile(Path.Combine(AssemblyFolder, ShaderPath), "PS", "ps_4_0");

            pixelShader = new PixelShader(device, pixelShaderByteCode);

            var signature = ShaderSignature.GetInputSignature(vertexShaderByteCode);

            // Create Constant Buffer
            constantBuffer = new Buffer(device, (int)((Utilities.SizeOf<CBuffer>() + 15) & ~15u), ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
            cBuffer = new CBuffer(default);
            layout = new InputLayout(device, signature, new[] { new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0), new InputElement("NORMAL", 0, Format.R32G32B32_Float, 12, 0), new InputElement("TEXCOORD", 0, Format.R32G32_Float, 24, 0) });

            var whiteBitmap = new System.Drawing.Bitmap(1, 1);
            whiteBitmap.SetPixel(0, 0, System.Drawing.Color.White);
            var blackBitmap = new System.Drawing.Bitmap(1, 1);
            blackBitmap.SetPixel(0, 0, System.Drawing.Color.Black);
            var whiteTexture2D = TextureFromBitmap(whiteBitmap);
            texture2DCache.Add(whiteTexture2D);
            var blackTexture2D = TextureFromBitmap(blackBitmap);
            texture2DCache.Add(blackTexture2D);
            whiteTexture = new ShaderResourceView(device, whiteTexture2D);
            blackTexture = new ShaderResourceView(device, blackTexture2D);

            LoadModel();

            SetupCameraPosition();

            signature.Dispose();
            vertexShaderByteCode.Dispose();
            pixelShaderByteCode.Dispose();

            // Prepare All the stages
            context.InputAssembler.InputLayout = layout;
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            context.VertexShader.SetConstantBuffer(0, constantBuffer);
            context.VertexShader.Set(vertexShader);
            context.Rasterizer.State = rasterizerState;
            context.PixelShader.Set(pixelShader);
            context.PixelShader.SetConstantBuffer(0, constantBuffer);
            context.OutputMerger.SetBlendState(blendState);
        }

        private void SetupCameraPosition()
        {
            cameraPosition = Vector3.Zero;
            lookTarget = bounds.Center;

            Vector3 objectSizes = bounds.Maximum - bounds.Minimum;
            float objectSize = Mathf.Max(Mathf.Max(objectSizes.X, objectSizes.Y), objectSizes.Z);
            float cameraView = 2.0f * (float)Mathf.Tan((double)(0.5f * Deg2Rad * Fov)); // Visible height 1 meter in front
            float distance = CameraDistance * objectSize / cameraView; // Combined wanted distance from the object
            distance += 0.5f * objectSize; // Estimated offset from the center to the outside of the object
            cameraPosition = bounds.Center - distance * Vector3.ForwardLH + new Vector3(0, bounds.Height * 0.15f, 0);
        }

        private void BuildWorldMatrix()
        {
            // Prepare matrices
            var view = Matrix.LookAtLH(cameraPosition, lookTarget, Vector3.Up);
            var proj = Matrix.PerspectiveFovLH((float)(Deg2Rad * Fov), width / (float)height, 0.1f, 10.0f);
            var worldViewProj = Matrix.Multiply(view, proj);
            worldViewProj.Transpose();
            cBuffer.matrix = worldViewProj;
            view.Invert();
            view.Transpose();
            cBuffer.view = view;
            cBuffer.cameraPosition = new Vector4(cameraPosition.X, cameraPosition.Y, cameraPosition.Z, 0);
        }

        private void BuildRenderTargets()
        {
            // Create the depth buffer
            depthBuffer = new Texture2D(device, new Texture2DDescription()
            {
                Format = Format.D32_Float_S8X24_UInt,
                ArraySize = 1,
                MipLevels = 1,
                Width = width,
                Height = height,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.DepthStencil,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None
            });
        }

        /// <summary>
        /// Set and draw materials
        /// </summary>
        /// <param name="onDraw">Called after set material, param is vertex count</param>
        private void DrawMaterials()
        {
            BuildWorldMatrix();
            for (int i = 0; i < vertexBuffers.Length; i++)
            {
                var material = model.material_list.material[i];
                context.InputAssembler.SetVertexBuffers(0, vertexBufferBindings[i]);

                var slot = 0;
                if (material.usually_texture_index != uint.MaxValue)
                {
                    var texturePath = model.texture_list.texture_file[material.usually_texture_index];
                    if (textureCache.ContainsKey(texturePath))
                    {
                        context.PixelShader.SetShaderResource(slot, textureCache[texturePath].Resource);
                        cBuffer.textureSwapRBFlag.X = textureCache[texturePath].IsArgb ? 1 : 0;
                    }
                    else
                    {
                        context.PixelShader.SetShaderResource(slot, whiteTexture);
                    }
                }
                else
                {
                    context.PixelShader.SetShaderResource(slot, whiteTexture);
                }
                slot = 1;
                if (material.sphere_texture_index != uint.MaxValue)
                {
                    var texturePath = model.texture_list.texture_file[material.sphere_texture_index];
                    if (textureCache.ContainsKey(texturePath))
                    {
                        context.PixelShader.SetShaderResource(slot, textureCache[texturePath].Resource);
                        cBuffer.textureSwapRBFlag.Y = textureCache[texturePath].IsArgb ? 1 : 0;
                    }
                    else
                    {
                        context.PixelShader.SetShaderResource(slot, material.sphere_mode == PMXFormat.Material.SphereMode.MulSphere ? whiteTexture : blackTexture);
                    }
                    cBuffer.randerParams.W = (int)material.sphere_mode;
                }
                else
                {
                    context.PixelShader.SetShaderResource(slot, blackTexture);
                    cBuffer.randerParams.W = 0;
                }
                slot = 2;
                if (material.common_toon == 1)
                {
                    context.PixelShader.SetShaderResource(slot, toonCache[material.common_toon + 1]);
                    cBuffer.textureSwapRBFlag.Z = 1;
                }
                else
                {
                    if (material.toon_texture_index != uint.MaxValue)
                    {
                        var texturePath = model.texture_list.texture_file[material.toon_texture_index];
                        if (textureCache.ContainsKey(texturePath))
                        {
                            context.PixelShader.SetShaderResource(slot, textureCache[texturePath].Resource);
                            cBuffer.textureSwapRBFlag.Y = textureCache[texturePath].IsArgb ? 1 : 0;
                        }
                        else
                        {
                            context.PixelShader.SetShaderResource(slot, whiteTexture);
                        }
                    }
                    else
                    {
                        context.PixelShader.SetShaderResource(slot, whiteTexture);
                    }
                }

                cBuffer.randerParams.X = i;
                cBuffer.randerParams.Z = material.specularity;
                cBuffer.color = material.diffuse_color.ToVector4();
                cBuffer.ambientColor = material.ambient_color.ToVector4();
                context.UpdateSubresource(ref cBuffer, constantBuffer);

                context.Draw(vertexBuffers[i].VertexCount, 0);
            }
        }

        public (Texture2D Texture, bool IsArgb) LoadTexture(Device device, string path)
        {
            try
            {
                //var extension = Path.GetExtension(path).ToLower();
                //if (extension.Contains("dds") || extension.Contains("tga"))
                //{
                //    try
                //    {
                //        return LoadTextureFromPFim(path);
                //    }
                //    catch
                //    {
                //        return LoadTextureFromTextureLoader(path);
                //    }
                //}
                //else
                //{
                //    return LoadTextureFromTextureLoader(path);
                //}

                try
                {
                    return LoadTextureFromPFim(path);
                }
                catch
                {
                    return LoadTextureFromTextureLoader(path);
                }
            }
            catch (Exception e)
            {
                //throw e;
                return (null, false);
            }
        }
        public (Texture2D Texture, bool IsArgb) LoadTextureFromPFim(string path)
        {
            var extension = Path.GetExtension(path).ToLower();
            (System.Drawing.Bitmap bitmap, Stream stream) = PFimToBitmap(path);
            var tex = TextureFromBitmap(bitmap, stream);
            return (tex,false/* bitmap.PixelFormat.ToString().ToLower().Contains("argb") ^ extension.Contains("tga")*/);
        }

        public (Texture2D Texture, bool IsArgb) LoadTextureFromTextureLoader(string path)
        {
            var extension = Path.GetExtension(path).ToLower();
            var pack = TextureLoader.LoadBitmap(new ImagingFactory2(), path);
            var result = (TextureLoader.CreateTexture2DFromBitmap(device, pack.Source, pack.Stream),false /*extension.Contains("bmp")*/);
            foreach (var dis in pack.Disposables)
            {
                dis.Dispose();
            }
            return result;
        }

        [HandleProcessCorruptedStateExceptions]
        private Texture2D TextureFromBitmap(System.Drawing.Bitmap bitmap, Stream stream = null)
        {
            var format = Format.B8G8R8A8_UNorm;

            switch (bitmap.PixelFormat)
            {
                case System.Drawing.Imaging.PixelFormat.Format24bppRgb:
                    format = Format.B8G8R8X8_UNorm;
                    break;
                default:
                    break;
            }

            var bitmapData = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);

            // copy our buffer to the texture
            int stride = bitmap.Width * 4;
            var tex = new Texture2D(device, new Texture2DDescription()
            {
                Width = bitmap.Width,
                Height = bitmap.Height,
                Format = format,
                ArraySize = 1,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                Usage = ResourceUsage.Default,
                CpuAccessFlags = CpuAccessFlags.None,
                MipLevels = 1,
                OptionFlags = ResourceOptionFlags.GenerateMipMaps,
                SampleDescription = new SampleDescription(1, 0),
            });
            try
            {
                device.ImmediateContext.UpdateSubresource(tex, 0, null, bitmapData.Scan0, stride, 0);
            }
            catch (Exception e)
            {
                throw e;
            }
            // unlock the bitmap data
            bitmap.UnlockBits(bitmapData);
            if (stream != null) stream.Dispose();
            return tex;
        }

        private (System.Drawing.Bitmap Bitmap, Stream Stream) PFimToBitmap(string path)
        {
            var readStream = File.OpenRead(path);
            using (var image = Pfim.Pfim.FromStream(readStream))
            {
                System.Drawing.Imaging.PixelFormat format;

                // Convert from Pfim's backend agnostic image format into GDI+'s image format
                switch (image.Format)
                {
                    case Pfim.ImageFormat.Rgba32:
                        format = System.Drawing.Imaging.PixelFormat.Format32bppArgb;
                        break;

                    case Pfim.ImageFormat.Rgba16:
                        format = System.Drawing.Imaging.PixelFormat.Format16bppArgb1555;
                        break;

                    case Pfim.ImageFormat.Rgb24:
                        format = System.Drawing.Imaging.PixelFormat.Format24bppRgb;
                        break;

                    case Pfim.ImageFormat.Rgb8:
                        format = System.Drawing.Imaging.PixelFormat.Format8bppIndexed;
                        break;

                    default:
                        // see the sample for more details
                        throw new NotImplementedException();
                }

                // Pin pfim's data array so that it doesn't get reaped by GC, unnecessary
                // in this snippet but useful technique if the data was going to be used in
                // control like a picture box
                //var handle = GCHandle.Alloc(image.Data, GCHandleType.Pinned);
                try
                {
                    var data = Marshal.UnsafeAddrOfPinnedArrayElement(image.Data, 0);
                    var bitmap = new System.Drawing.Bitmap(image.Width, image.Height, image.Stride, format, data);

                    return (bitmap, readStream);
                }
                catch (Exception e)
                {
                    return (null, readStream);
                }
                //finally
                //{
                //    handle.Free();
                //}
            }
        }


    }

    public struct VertexData
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 UV;
    }

    public struct CBuffer
    {
        public Matrix view;
        public Matrix matrix;
        public Vector4 cameraPosition;
        public Vector4 randerParams;    //	x: Drawing index, y: Swap RB, z:shininess, w: (Sphere operation DISBLE = 0,MULT = 1,ADD = 2,SUB_TEXTURE = 3)
        public Vector4 color;
        public Vector4 specularColor;
        public Vector4 ambientColor;
        public Vector4 textureSwapRBFlag;

        public CBuffer(object _)
        {
            view = Matrix.Identity;
            matrix = Matrix.Identity;
            randerParams = Vector4.Zero;
            color = Vector4.One;
            specularColor = Vector4.One;
            ambientColor = Vector4.One;
            textureSwapRBFlag = Vector4.Zero;
            cameraPosition = Vector4.Zero;
        }
    }

    public class TextureLoader
    {
        /// <summary>
        /// Loads a bitmap using WIC.
        /// </summary>
        /// <param name="deviceManager"></param>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static (SharpDX.WIC.BitmapSource Source, Stream Stream, List<IDisposable> Disposables) LoadBitmap(SharpDX.WIC.ImagingFactory2 factory, string filename)
        {
            var readStream = File.OpenRead(filename);
            var bitmapDecoder = new SharpDX.WIC.BitmapDecoder(
                factory,
                readStream,
                DecodeOptions.CacheOnDemand
                );

            var formatConverter = new FormatConverter(factory);
            var frame = bitmapDecoder.GetFrame(0);
            formatConverter.Initialize(
                frame,
                SharpDX.WIC.PixelFormat.Format32bppPRGBA,
                BitmapDitherType.None,
                null,
                0.0,
                BitmapPaletteType.Custom);
            var disposables = new List<IDisposable>()
            {
                factory,
                bitmapDecoder,
                frame
            };
            return (formatConverter, readStream, disposables);
        }

        /// <summary>
        /// Creates a <see cref="SharpDX.Direct3D11.Texture2D"/> from a WIC <see cref="SharpDX.WIC.BitmapSource"/>
        /// </summary>
        /// <param name="device">The Direct3D11 device</param>
        /// <param name="bitmapSource">The WIC bitmap source</param>
        /// <returns>A Texture2D</returns>
        public static SharpDX.Direct3D11.Texture2D CreateTexture2DFromBitmap(SharpDX.Direct3D11.Device device, SharpDX.WIC.BitmapSource bitmapSource, Stream stream)
        {
            // Allocate DataStream to receive the WIC image pixels
            int stride = bitmapSource.Size.Width * 4;
            using (var buffer = new SharpDX.DataStream(bitmapSource.Size.Height * stride, true, true))
            {
                // Copy the content of the WIC to the buffer
                bitmapSource.CopyPixels(stride, buffer);
                stream.Dispose();
                var width = bitmapSource.Size.Width;
                var height = bitmapSource.Size.Height;
                bitmapSource.Dispose();
                return new SharpDX.Direct3D11.Texture2D(device, new SharpDX.Direct3D11.Texture2DDescription()
                {
                    Width = width,
                    Height = height,
                    ArraySize = 1,
                    BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource,
                    Usage = SharpDX.Direct3D11.ResourceUsage.Immutable,
                    CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.None,
                    Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                    MipLevels = 1,
                    OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None,
                    SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                }, new SharpDX.DataRectangle(buffer.DataPointer, stride));
            }
        }
    }
}