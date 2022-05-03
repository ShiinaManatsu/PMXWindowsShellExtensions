using MMDataIO.Pmx;
using MMDExtensions;
using SharpDX;
using SharpDX.D3DCompiler;
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

        private Texture2D whiteTexture;
        private Texture2D blackTexture;

        private int width;
        private int height;

        private string pmxPath;

        private Vector3 cameraPosition;
        private Vector3 lookTarget;

        private readonly Dictionary<string, (ShaderResourceView Resource, bool IsArgb)> textureCache = new Dictionary<string, (ShaderResourceView Resource, bool IsArgb)>();
        private (Buffer Buffer, int VertexCount)[] vertexBuffers;
        private List<VertexBufferBinding> vertexBufferBindings;

        private PmxModelData model;
        private BoundingBox bounds;

        private const double Rad2Deg = 360 / (Mathf.PI * 2);
        private const double Deg2Rad = (Mathf.PI * 2) / 360;
        private const float Fov = 42f;
        private const float CameraDistance = 0.8f; // Constant factor
        private const float MoveSpeed = 0.1f;
        private const float ModelScale = 0.08f;

        private const string ShaderPath = @"Assets/Toon.hlsl";
        private string AssemblyFolder => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
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
                context.ClearRenderTargetView(renderView, Color.White);
                for (int i = 0; i < vertexBuffers.Length; i++)
                {
                    context.InputAssembler.SetVertexBuffers(0, vertexBufferBindings[i]);

                    if (model.MaterialArray[i].TextureId >= 255) continue;

                    var texturePath = model.TextureFiles[model.MaterialArray[i].TextureId];
                    if (textureCache.ContainsKey(texturePath))
                    {
                        context.PixelShader.SetShaderResource(0, textureCache[texturePath].Resource);
                        cBuffer.index.Y = textureCache[texturePath].IsArgb ? 1 : 0;
                    }

                    cBuffer.index.X = i;
                    context.UpdateSubresource(ref cBuffer, constantBuffer);
                    context.Draw(vertexBuffers[i].VertexCount, 0);
                }

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
            width = previewWidth;
            height = previewHeight;
            pmxPath = absolutePmxPath;

            //PrepareBeforeDevice();
            device = new Device(DriverType.Hardware, DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport | DeviceCreationFlags.None).QueryInterface<Device>();
            context = device.ImmediateContext;
            PrepareAfterDevice();

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
            context.ClearRenderTargetView(renderView, Color.DarkGray);

            for (int i = 0; i < vertexBuffers.Length; i++)
            {
                context.InputAssembler.SetVertexBuffers(0, vertexBufferBindings[i]);

                if (model.MaterialArray[i].TextureId >= 255) continue;

                var texturePath = model.TextureFiles[model.MaterialArray[i].TextureId];
                if (textureCache.ContainsKey(texturePath))
                {
                    context.PixelShader.SetShaderResource(0, textureCache[texturePath].Resource);
                    cBuffer.index.Y = textureCache[texturePath].IsArgb ? 1 : 0;
                }
                cBuffer.index.X = i;
                context.UpdateSubresource(ref cBuffer, constantBuffer);
                context.Draw(vertexBuffers[i].VertexCount, 0);
            }

            var bitmap = Blit(device, backBuffer);

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
            context.Dispose();
            device.Dispose();

            return bitmap;
        }
        #endregion

        private void LoadModel()
        {
            model = new PmxModelData();
            using (var r = new BinaryReader(File.OpenRead(pmxPath)))
            {
                model.Read(r);
            }

            vertexBuffers = new (Buffer Buffer, int VertexCount)[model.MaterialArray.Length];
            for (int i = 0, indexOffset = 0; i < model.MaterialArray.Length; ++i)
            {
                var mat = model.MaterialArray[i];
                var vertexData = new VertexData[mat.FaceCount];
                for (int j = 0; j < mat.FaceCount; ++j)
                {
                    var index = model.VertexIndices[indexOffset + j];
                    var vertex = model.VertexArray[Mathf.Abs(index)];
                    vertexData[j] = new VertexData()
                    {
                        Position = vertex.Pos * ModelScale,
                        Normal = vertex.Normal,
                        UV = vertex.Uv
                    };
                }
                indexOffset += mat.FaceCount;
                vertexBuffers[i] = (Buffer.Create(device, BindFlags.VertexBuffer, vertexData), mat.FaceCount);
            }
            vertexBufferBindings = vertexBuffers.Select(x => new VertexBufferBinding(x.Buffer, Utilities.SizeOf<VertexData>(), 0)).ToList();

            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            foreach (var vertex in model.VertexArray.Select(x => x.Pos))
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

            foreach (var texturePath in model.TextureFiles)
            {
                var directory = Path.GetDirectoryName(pmxPath);
                var fullPath = Path.Combine(directory, texturePath);
                if (File.Exists(fullPath))
                {
                    Texture2D texture;
                    if (!textureCache.ContainsKey(texturePath))
                    {
                        var loaded = LoadTexture(device, fullPath);
                        texture = loaded.Texture;
                        textureCache.Add(texturePath, (new ShaderResourceView(device, texture), loaded.IsArgb));
                    }
                }
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
            constantBuffer = new Buffer(device, Utilities.SizeOf<CBuffer>(), ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
            cBuffer = new CBuffer(Matrix.Identity, Vector4.Zero);
            layout = new InputLayout(device, signature, new[] { new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0), new InputElement("NORMAL", 0, Format.R32G32B32_Float, 12, 0), new InputElement("TEXCOORD", 0, Format.R32G32_Float, 24, 0) });

            LoadModel();

            SetupCameraPosition();
            BuildWorldMatrix();

            signature.Dispose();
            vertexShaderByteCode.Dispose();
            pixelShaderByteCode.Dispose();

            var whiteBitmap = new System.Drawing.Bitmap(1, 1);
            whiteBitmap.SetPixel(0, 0, System.Drawing.Color.White);
            var blackBitmap = new System.Drawing.Bitmap(1, 1);
            blackBitmap.SetPixel(0, 0, System.Drawing.Color.Black);
            whiteTexture = TextureFromBitmap(whiteBitmap);
            blackTexture = TextureFromBitmap(blackBitmap);

            // Prepare All the stages
            context.InputAssembler.InputLayout = layout;
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            context.VertexShader.SetConstantBuffer(0, constantBuffer);
            context.VertexShader.Set(vertexShader);
            context.Rasterizer.State = rasterizerState;
            context.PixelShader.Set(pixelShader);
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
            cameraPosition = bounds.Center - distance * Vector3.ForwardLH;
        }

        private void BuildWorldMatrix()
        {
            // Prepare matrices
            var view = Matrix.LookAtLH(cameraPosition, lookTarget, Vector3.Up);
            var proj = Matrix.PerspectiveFovLH((float)(Deg2Rad * Fov), width / (float)height, 0.1f, 100.0f);
            var worldViewProj = Matrix.Multiply(view, proj);
            worldViewProj.Transpose();
            cBuffer.matrix = worldViewProj;
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

        public (Texture2D Texture, bool IsArgb) LoadTexture(Device device, string path)
        {
            var extension = Path.GetExtension(path).ToLower();
            if (extension.Contains("tga") || extension.Contains("dds"))
            {
                var bitmap = PFimToBitmap(path);
                var tex = TextureFromBitmap(bitmap);
                return (tex, bitmap.PixelFormat.ToString().ToLower().Contains("argb"));
            }
            else
            {
                return (TextureLoader.CreateTexture2DFromBitmap(device, TextureLoader.LoadBitmap(new ImagingFactory2(), path)), false);
            }

        }

        private Texture2D TextureFromBitmap(System.Drawing.Bitmap bitmap)
        {
            var bitmapData = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);

            // copy our buffer to the texture
            int stride = bitmap.Width * 4;
            var tex = new Texture2D(device, new Texture2DDescription()
            {
                Width = bitmap.Width,
                Height = bitmap.Height,
                Format = Format.R8G8B8A8_UNorm,
                ArraySize = 1,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                Usage = ResourceUsage.Default,
                CpuAccessFlags = CpuAccessFlags.None,
                MipLevels = 1,
                OptionFlags = ResourceOptionFlags.GenerateMipMaps,
                SampleDescription = new SampleDescription(1, 0),
            });
            device.ImmediateContext.UpdateSubresource(tex, 0, null, bitmapData.Scan0, stride, 0);

            // unlock the bitmap data
            bitmap.UnlockBits(bitmapData);
            return tex;
        }

        private System.Drawing.Bitmap PFimToBitmap(string path)
        {
            using (var image = Pfim.Pfim.FromFile(path))
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
                var handle = GCHandle.Alloc(image.Data, GCHandleType.Pinned);
                try
                {
                    var data = Marshal.UnsafeAddrOfPinnedArrayElement(image.Data, 0);
                    var bitmap = new System.Drawing.Bitmap(image.Width, image.Height, image.Stride, format, data);

                    return bitmap;
                }
                catch (Exception e)
                {
                    return null;
                }
                finally
                {
                    handle.Free();
                }
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
        public Matrix matrix;
        public Vector4 index;

        public CBuffer(Matrix _, Vector4 __)
        {
            this.matrix = Matrix.Identity;
            this.index = Vector4.One;
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
        public static SharpDX.WIC.BitmapSource LoadBitmap(SharpDX.WIC.ImagingFactory2 factory, string filename)
        {
            var bitmapDecoder = new SharpDX.WIC.BitmapDecoder(
                factory,
                filename,
                DecodeOptions.CacheOnDemand
                );

            var formatConverter = new FormatConverter(factory);

            formatConverter.Initialize(
                bitmapDecoder.GetFrame(0),
                SharpDX.WIC.PixelFormat.Format32bppPRGBA,
                BitmapDitherType.None,
                null,
                0.0,
                BitmapPaletteType.Custom);

            return formatConverter;
        }

        /// <summary>
        /// Creates a <see cref="SharpDX.Direct3D11.Texture2D"/> from a WIC <see cref="SharpDX.WIC.BitmapSource"/>
        /// </summary>
        /// <param name="device">The Direct3D11 device</param>
        /// <param name="bitmapSource">The WIC bitmap source</param>
        /// <returns>A Texture2D</returns>
        public static SharpDX.Direct3D11.Texture2D CreateTexture2DFromBitmap(SharpDX.Direct3D11.Device device, SharpDX.WIC.BitmapSource bitmapSource)
        {
            // Allocate DataStream to receive the WIC image pixels
            int stride = bitmapSource.Size.Width * 4;
            using (var buffer = new SharpDX.DataStream(bitmapSource.Size.Height * stride, true, true))
            {
                // Copy the content of the WIC to the buffer
                bitmapSource.CopyPixels(stride, buffer);
                return new SharpDX.Direct3D11.Texture2D(device, new SharpDX.Direct3D11.Texture2DDescription()
                {
                    Width = bitmapSource.Size.Width,
                    Height = bitmapSource.Size.Height,
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