﻿using HelixToolkit.Wpf;
using MMDExtensions;
using MMDExtensions.PMX;
using SharpShell.Attributes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using MediaPixelFormat = System.Windows.Media.PixelFormat;
using SystemPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace Thumbnail.PMX
{
    /// <summary>
    /// The PMXThumbnailHandler is a ThumbnailHandler for pmx files
    /// </summary>
    [ComVisible(true)]
    [Guid("88615FC3-2F4A-463B-805B-1ED5BFF4F393")]
    [COMServerAssociation(AssociationType.FileExtension, ".pmx")]
    public class PMXThumbnailHandler : FileThumbnailHandler
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PMXThumbnailHandler"/> class
        /// </summary>
        public PMXThumbnailHandler()
        {
            //  Create our lazy objects
        }

        /// <summary>
        /// Gets the thumbnail image
        /// </summary>
        /// <param name="width">The width of the image that should be returned.</param>
        /// <returns>
        /// The image for the thumbnail
        /// </returns>
        protected override Bitmap GetThumbnailImage(uint width)
        {
            //  Attempt to open the stream with a reader
            var pmx = PMXLoaderScript.Import(SelectedItemPath);

            MeshCreationInfo creation_info = CreateMeshCreationInfoSingle(pmx);

            var diffuseMat = MaterialHelper.CreateMaterial(Colors.Gray);

            var models = new Model3DGroup();

            for (int i = 0, i_max = creation_info.value.Length; i < i_max; ++i)
            {
                int[] indices = creation_info.value[i].plane_indices.Select(x => (int)creation_info.reassign_dictionary[x])
                                                                    .ToArray();
                var mesh = new MeshGeometry3D
                {
                    Positions = new Point3DCollection(pmx.vertex_list.vertex.Select(x => x.pos)),

                    TextureCoordinates = new PointCollection(pmx.vertex_list.vertex.Select(x => new System.Windows.Point(x.uv.X, x.uv.Y)))
                };



                indices.ToList()
                    .ForEach(x => mesh.TriangleIndices.Add(x));
                var textureIndex = pmx.material_list.material[creation_info.value[i].material_index].usually_texture_index;
                var texturePath = pmx.texture_list.texture_file.ElementAtOrDefault((int)textureIndex);

                var material = diffuseMat;
                if (!string.IsNullOrWhiteSpace(texturePath))
                {
                    texturePath = Path.Combine(Path.GetDirectoryName(SelectedItemPath), texturePath);
                    //Log($"Texture found: {texturePath}");

                    if (!string.IsNullOrWhiteSpace(texturePath) && File.Exists(texturePath))
                    {
                        //  dds and tga
                        if (new string[] { ".dds", ".tga" }.Any(x => x.Equals(Path.GetExtension(texturePath))))
                        {
                            var bitmap = PFimToBitmap(texturePath);
                            material = MaterialHelper.CreateImageMaterial(Bitmap2BitmapImage(bitmap), 1);
                        }
                        else
                        {

                            material = MaterialHelper.CreateImageMaterial(BitmapImageFromFile(texturePath), 1);
                        }
                    }
                }

                models.Children.Add(new GeometryModel3D(mesh, material));
            }

            var sorting = new SortingVisual3D()
            {
                Content = models
            };

            var view = new HelixViewport3D();
            view.Children.Add(sorting);
            view.Camera.Position = new Point3D(0, 15, -30);
            view.Camera.LookDirection = new Vector3D(0, -5, 30);

            view.Background = System.Windows.Media.Brushes.Transparent;

            view.Children.Add(new SunLight() { Altitude = 260 });

            view.Children.Add(new DefaultLights());

            try
            {
                var bitmap = view.Viewport.RenderBitmap(width, width, new SolidColorBrush(Colors.Transparent));
                view.Children.Clear();
                view = null;
                sorting = null;
                models = null;
                GC.Collect();
                return BitmapFromSource(bitmap);
            }
            catch (Exception exception)
            {
                view.Children.Clear();
                view = null;
                sorting = null;
                models = null;
                GC.Collect();
                LogError("An exception occurred Rendering bitmap.", exception);
                //MessageBox.Show(exception.Message);
                //MessageBox.Show(exception.StackTrace);
                return null;
            }
        }

        public BitmapImage BitmapImageFromFile(string path)
        {
            using (var ms = new MemoryStream(File.ReadAllBytes(path)))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad; // here
                image.StreamSource = ms;
                image.EndInit();
                return image;
            }
        }

        private Bitmap BitmapFromSource(BitmapSource bitmapsource)
        {
            System.Drawing.Bitmap bitmap;
            using (MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bitmapsource));
                enc.Save(outStream);
                bitmap = new System.Drawing.Bitmap(outStream);
            }
            return bitmap;
        }

        private Bitmap PFimToBitmap(string path)
        {
            using (var image = Pfim.Pfim.FromFile(path))
            {
                SystemPixelFormat format;

                // Convert from Pfim's backend agnostic image format into GDI+'s image format
                switch (image.Format)
                {
                    case Pfim.ImageFormat.Rgba32:
                        format = SystemPixelFormat.Format32bppArgb;
                        break;
                    case Pfim.ImageFormat.Rgba16:
                        format = SystemPixelFormat.Format16bppArgb1555;
                        break;
                    case Pfim.ImageFormat.Rgb24:
                        format = SystemPixelFormat.Format24bppRgb;
                        break;
                    case Pfim.ImageFormat.Rgb8:
                        format = SystemPixelFormat.Format8bppIndexed;
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
                    var bitmap = new Bitmap(image.Width, image.Height, image.Stride, format, data);
                    return bitmap;
                }
                catch (Exception e)
                {
                    LogError(e.Message);
                    return null;
                }
                finally
                {
                    handle.Free();
                }
            }
        }

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        private BitmapImage Bitmap2BitmapImage(Bitmap bitmap)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            BitmapImage retval;

            try
            {
                retval = Imaging.CreateBitmapSourceFromHBitmap(
                             hBitmap,
                             IntPtr.Zero,
                             Int32Rect.Empty,
                             BitmapSizeOptions.FromEmptyOptions()) as BitmapImage;
            }
            finally
            {
                DeleteObject(hBitmap);
            }

            return retval;
        }

        MeshCreationInfo CreateMeshCreationInfoSingle(PMXFormat format)
        {
            MeshCreationInfo result = new MeshCreationInfo
            {
                //全マテリアルを設定
                value = CreateMeshCreationInfoPacks(format),
                //全頂点を設定
                all_vertices = Enumerable.Range(0, format.vertex_list.vertex.Length).Select(x => (uint)x).ToArray()
            };
            //頂点リアサインインデックス用辞書作成
            result.reassign_dictionary = new Dictionary<uint, uint>(result.all_vertices.Length);
            for (uint i = 0, i_max = (uint)result.all_vertices.Length; i < i_max; ++i)
            {
                result.reassign_dictionary[i] = i;
            }
            return result;
        }
        MeshCreationInfo.Pack[] CreateMeshCreationInfoPacks(PMXFormat format)
        {
            uint plane_start = 0;
            //マテリアル単位のMeshCreationInfo.Packを作成する
            return Enumerable.Range(0, format.material_list.material.Length)
                            .Select(x =>
                            {
                                MeshCreationInfo.Pack pack = new MeshCreationInfo.Pack();
                                pack.material_index = (uint)x;
                                uint plane_count = format.material_list.material[x].face_vert_count;
                                pack.plane_indices = format.face_vertex_list.face_vert_index.Skip((int)plane_start)
                                                                                                        .Take((int)plane_count)
                                                                                                        .ToArray();
                                pack.vertices = pack.plane_indices.Distinct() //重複削除
                                                                        .ToArray();
                                plane_start += plane_count;
                                return pack;
                            })
                            .ToArray();
        }


        public class MeshCreationInfo
        {
            public class Pack
            {
                public uint material_index; //マテリアル
                public uint[] plane_indices;    //面
                public uint[] vertices;     //頂点
            }
            public Pack[] value;
            public uint[] all_vertices;         //総頂点
            public Dictionary<uint, uint> reassign_dictionary;  //頂点リアサインインデックス用辞書
        }
    }

}